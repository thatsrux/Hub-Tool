using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace HubTool;

/// <summary>Queries standard DirectShow camera properties without creating a capture graph.</summary>
public static class CameraControls
{
    public sealed record ResetResult(Dictionary<string, double> Values, int Applied, List<string> Errors);
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid category, out IEnumMoniker? enumerator, int flags);
    }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
    [ComImport, Guid("C6E13370-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMCameraControl
    {
        [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int defaultValue, out int caps);
        [PreserveSig] int Set(int property, int value, int flags);
        [PreserveSig] int Get(int property, out int value, out int flags);
    }
    [ComImport, Guid("C6E13360-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMVideoProcAmp
    {
        [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int defaultValue, out int caps);
        [PreserveSig] int Set(int property, int value, int flags);
        [PreserveSig] int Get(int property, out int value, out int flags);
    }

    private delegate int RangeGetter(int property, out int min, out int max, out int step, out int defaultValue, out int caps);
    private delegate int ValueGetter(int property, out int value, out int flags);
    private delegate int ValueSetter(int property, int value, int flags);
    private static readonly string[] CameraNames = ["Pan", "Tilt", "Rotazione", "Zoom", "Esposizione (log₂ secondi)", "Iris", "Messa a fuoco"];
    private static readonly string[] VideoNames = ["Luminosità", "Contrasto", "Tonalità", "Saturazione", "Nitidezza", "Gamma", "Bilanciamento del bianco", "Colore", "Compensazione controluce", "Gain video"];

    private static void Visit(Action<string, string, IMoniker> visit)
    {
        var system = Activator.CreateInstance(Type.GetTypeFromCLSID(new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"), true)!)!;
        IEnumMoniker? enumerator = null;
        try
        {
            var category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            int hr = ((ICreateDevEnum)system).CreateClassEnumerator(ref category, out enumerator, 0);
            if (hr == 1 || enumerator == null) return; // Empty category is S_FALSE, not an error.
            Marshal.ThrowExceptionForHR(hr);
            var item = new IMoniker[1];
            while (enumerator.Next(1, item, IntPtr.Zero) == 0)
            {
                var moniker = item[0];
                try
                {
                    moniker.GetDisplayName(null!, null!, out var path);
                    string name = "Webcam";
                    object? bag = null;
                    try
                    {
                        var iid = typeof(IPropertyBag).GUID;
                        moniker.BindToStorage(null!, null!, ref iid, out bag);
                        if (((IPropertyBag)bag).Read("FriendlyName", out var value, IntPtr.Zero) == 0)
                            name = value?.ToString() ?? name;
                    }
                    finally { if (bag != null) Marshal.ReleaseComObject(bag); }
                    visit("camera:" + path, name, moniker);
                }
                finally { Marshal.ReleaseComObject(moniker); }
            }
        }
        finally
        {
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(system);
        }
    }

    private static object Bind(IMoniker moniker)
    {
        var iid = new Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"); // IBaseFilter
        moniker.BindToObject(null!, null!, ref iid, out object filter);
        return filter;
    }

    private static void ReadProperties(Device device, string prefix, string[] labels, RangeGetter range, ValueGetter read)
    {
        for (int property = 0; property < labels.Length; property++)
        {
            if (range(property, out int min, out int max, out int step, out _, out int caps) < 0 || max < min || step <= 0) continue;
            if (read(property, out int value, out int flags) < 0) continue;
            var key = prefix + property;
            if ((caps & 2) != 0 && max > min)
            {
                device.Controls.Add(new(key, labels[property], min, max, step));
                device.Values[key] = Math.Clamp(value, min, max);
            }
            if ((caps & 3) == 3)
            {
                device.Controls.Add(new(key + ":auto", labels[property] + " · automatico", 0, 1, 1, Toggle: true));
                device.Values[key + ":auto"] = (flags & 1) != 0 ? 1 : 0;
            }
        }
    }

    public static int PreferredDefaultFlags(int caps) => (caps & 1) != 0 ? 1 : 2;

    private static int ResetProperties(string[] labels, RangeGetter range, ValueSetter write, List<string> errors)
    {
        int applied = 0;
        for (int property = 0; property < labels.Length; property++)
        {
            int hr = range(property, out int min, out int max, out int step, out int defaultValue, out int caps);
            if (hr < 0 || max < min || step <= 0) continue;
            int flags = PreferredDefaultFlags(caps);
            if ((caps & flags) == 0)
            {
                errors.Add(labels[property] + ": modalità predefinita non esposta");
                continue;
            }
            hr = write(property, Math.Clamp(defaultValue, min, max), flags);
            if (hr < 0) errors.Add(labels[property] + $": errore 0x{hr:X8}");
            else applied++;
        }
        return applied;
    }

    public static List<Device> Enumerate()
    {
        var result = new List<Device>();
        Visit((id, name, moniker) =>
        {
            var device = new Device { Id = id, Name = name, Kind = "Camera", Connected = true };
            object? filter = null;
            try
            {
                filter = Bind(moniker);
                if (filter is IAMCameraControl camera) ReadProperties(device, "camera:", CameraNames, camera.GetRange, camera.Get);
                if (filter is IAMVideoProcAmp video) ReadProperties(device, "video:", VideoNames, video.GetRange, video.Get);
            }
            catch (Exception ex) { device.Error = "Controlli webcam non disponibili: " + ex.Message; }
            finally { if (filter != null) Marshal.ReleaseComObject(filter); }
            result.Add(device);
        });
        return result;
    }

    public static Dictionary<string, double> Set(string id, string key, double value)
    {
        Dictionary<string, double>? actual = null;
        Visit((foundId, name, moniker) =>
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(id, foundId)) return;
            object filter = Bind(moniker);
            try
            {
                var parts = key.Split(':');
                if (parts.Length is < 2 or > 3 || !int.TryParse(parts[1], out int property) || (parts.Length == 3 && parts[2] != "auto"))
                    throw new NotSupportedException(key);
                RangeGetter range; ValueGetter read; ValueSetter write;
                if (parts[0] == "camera" && filter is IAMCameraControl camera)
                { range = camera.GetRange; read = camera.Get; write = camera.Set; }
                else if (parts[0] == "video" && filter is IAMVideoProcAmp video)
                { range = video.GetRange; read = video.Get; write = video.Set; }
                else throw new NotSupportedException(key);
                Marshal.ThrowExceptionForHR(range(property, out int min, out int max, out int step, out _, out int caps));
                Marshal.ThrowExceptionForHR(read(property, out int current, out _));
                bool auto = parts.Length == 3;
                if (!double.IsFinite(value) || (auto ? value is not (0 or 1) : value < min || value > max || step <= 0 || (value - min) % step != 0))
                    throw new ArgumentOutOfRangeException(nameof(value));
                int flags = auto && value == 1 ? 1 : 2;
                if ((caps & flags) == 0) throw new NotSupportedException("Modalità non supportata dalla webcam");
                Marshal.ThrowExceptionForHR(write(property, auto ? current : (int)value, flags));
                var device = new Device();
                if (filter is IAMCameraControl c) ReadProperties(device, "camera:", CameraNames, c.GetRange, c.Get);
                if (filter is IAMVideoProcAmp v) ReadProperties(device, "video:", VideoNames, v.GetRange, v.Get);
                actual = device.Values;
            }
            finally { Marshal.ReleaseComObject(filter); }
        });
        return actual ?? throw new InvalidOperationException("Webcam non più disponibile");
    }

    public static ResetResult Reset(string id)
    {
        ResetResult? result = null;
        Visit((foundId, name, moniker) =>
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(id, foundId)) return;
            object filter = Bind(moniker);
            try
            {
                var errors = new List<string>();
                int applied = 0;
                if (filter is IAMCameraControl camera)
                    applied += ResetProperties(CameraNames, camera.GetRange, camera.Set, errors);
                if (filter is IAMVideoProcAmp video)
                    applied += ResetProperties(VideoNames, video.GetRange, video.Set, errors);
                var device = new Device();
                if (filter is IAMCameraControl c) ReadProperties(device, "camera:", CameraNames, c.GetRange, c.Get);
                if (filter is IAMVideoProcAmp v) ReadProperties(device, "video:", VideoNames, v.GetRange, v.Get);
                result = new(device.Values, applied, errors);
            }
            finally { Marshal.ReleaseComObject(filter); }
        });
        return result ?? throw new InvalidOperationException("Webcam non più disponibile");
    }
}

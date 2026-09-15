namespace HubTool;

public static class PeripheralCatalog
{
    private static bool IsGenericInput(Device device)
    {
        if (device.Id.StartsWith("windows:", StringComparison.Ordinal)) return false;
        if (device.Kind is not ("Keyboard" or "Mouse")) return false;
        if (device.Id.StartsWith("ROOT\\MOUSE", StringComparison.OrdinalIgnoreCase)) return true;
        var product = device.ProductName.Split('\0')[0].Trim();
        if (product.Length > 0 && !product.Contains('%') && !product.StartsWith('@')) return false;
        var name = device.Name.Split('\0')[0].Trim().ToLowerInvariant();
        return name is "keyboard" or "mouse" or "tastiera" or "tastiera hid" or "hid keyboard device" or "mouse compatibile hid" or "hid-compliant mouse"
            || name.Contains("hid keyboard") || name.Contains("tastiera hid") || name.Contains("mouse hid") || name.Contains("hid-compliant mouse");
    }

    private static bool IsVirtualCamera(Device device)
    {
        if (!device.Id.StartsWith("camera:", StringComparison.Ordinal)) return false;
        var name = device.Name.ToLowerInvariant();
        return device.Id.StartsWith("camera:@device:sw:", StringComparison.OrdinalIgnoreCase)
            || name.Contains("virtual camera") || name.Contains("virtual webcam") || name.Contains("obs camera");
    }

    private static bool IsVirtualPrintQueue(Device device)
    {
        if (!device.Kind.Equals("PrintQueue", StringComparison.OrdinalIgnoreCase)) return false;
        var name = device.Name.ToLowerInvariant();
        return name.Contains("onenote") || name.Contains("pdf") || name.Contains("xps") || name == "fax"
            || name.Contains("document writer") || name.Contains("print to file") || name.Contains("stampa su file");
    }

    public static bool IsNativePeripheral(string kind, string id, string name) => kind.ToLowerInvariant() switch
    {
        "keyboard" or "mouse" or "monitor" or "camera" or "image" or "wpd" => true,
        "printqueue" => !id.EndsWith("\\PRINTQUEUES", StringComparison.OrdinalIgnoreCase),
        "printer" => !name.Contains("Class Driver", StringComparison.OrdinalIgnoreCase),
        "usb" => id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) && name.Contains("hub", StringComparison.OrdinalIgnoreCase),
        "diskdrive" => id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase),
        "hidclass" => name.Contains("gamepad", StringComparison.OrdinalIgnoreCase) || name.Contains("joystick", StringComparison.OrdinalIgnoreCase)
            || name.Contains("game controller", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static bool IsPeripheral(Device device) => !IsGenericInput(device) && !IsVirtualCamera(device) && !IsVirtualPrintQueue(device)
        && (device.Id.StartsWith("audio:") || device.Id.StartsWith("monitor:") || device.Id.StartsWith("camera:") || device.Id.StartsWith("light:")
        || device.Id.StartsWith("input:") || device.Id.StartsWith("windows:") || IsNativePeripheral(device.Kind, device.Id, device.Name));

    public static bool IsVisible(Device device) => IsPeripheral(device);

    public static string Category(Device device) => device.Kind.ToLowerInvariant() switch
    {
        "microfono" => "Microfoni", "uscita audio" => "Audio", "mouse" or "keyboard" or "hidclass" => "Input",
        "monitor" => "Monitor", "camera" => "Videocamere", "light" => "Illuminazione", _ => "USB e altro"
    };

    public static string Icon(Device device)
    {
        if (device.Kind == "Microfono") return "microphone";
        if (device.Kind == "Uscita audio") return device.AudioFormFactor is 3 or 5 or 6
            || device.Name.Contains("head", StringComparison.OrdinalIgnoreCase) || device.Name.Contains("cuffi", StringComparison.OrdinalIgnoreCase) ? "headphones" : "speaker";
        return device.Kind.ToLowerInvariant() switch
        {
            "keyboard" => "keyboard", "mouse" => "mouse", "monitor" => "monitor", "camera" => "camera", "light" => "light", "image" => "scanner",
            "printer" or "printqueue" => "printer", "diskdrive" => "drive", "hidclass" => "gamepad", _ => "usb"
        };
    }

    public static List<Device> Prepare(List<Device> discovered)
    {
        var specialized = discovered.Where(d => d.Id.StartsWith("monitor:") || d.Id.StartsWith("camera:")).ToList();
        var raw = discovered.Where(d => !d.Id.Contains(':')).ToList();
        foreach (var device in specialized)
        {
            var match = raw.FirstOrDefault(d => device.Id.Replace('#', '\\').Contains(d.Id, StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;
            if (match.ProductName.Length > 0) device.Name = match.ProductName;
            else if (device.Name == "Generic PnP Monitor") device.Name = match.Name;
            device.PhysicalId = match.Id;
        }
        var specializedIds = specialized.Select(d => d.PhysicalId).Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<Device>();
        var inputGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imageContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in discovered)
        {
            device.Name = device.Name.Split('\0')[0];
            device.ProductName = device.ProductName.Split('\0')[0];
            if (device.ProductName.Contains('%') || device.ProductName.StartsWith('@')) device.ProductName = "";
            if (device.Kind == "Monitor" && device.Name.StartsWith("Generic Monitor (") && device.Name.EndsWith(')')) device.Name = device.Name[17..^1];
            if (!IsPeripheral(device) || specializedIds.Contains(device.Id)) continue;
            if (device.Kind == "Image" && device.ContainerId.Length > 0)
            {
                if (!imageContainers.Add(device.ContainerId))
                {
                    var existing = result.FirstOrDefault(d => d.Kind == "Image" && d.ContainerId.Equals(device.ContainerId, StringComparison.OrdinalIgnoreCase));
                    if (existing != null && device.ProductName.Length > 0 && existing.ProductName.Length == 0)
                    {
                        int position = result.IndexOf(existing); result[position] = device;
                    }
                    continue;
                }
            }
            if (device.Kind is "Keyboard" or "Mouse" && !device.Id.StartsWith("windows:"))
            {
                var group = device.Kind + ":" + (device.ContainerId.Length > 0 ? device.ContainerId : device.Id);
                if (!inputGroups.Add(group)) continue;
                var preferences = discovered.FirstOrDefault(d => d.Id == (device.Kind == "Keyboard" ? InputControls.KeyboardId : InputControls.MouseId));
                if (preferences != null) { device.Controls = preferences.Controls.ToList(); device.Values = new(preferences.Values); }
                if (device.ProductName.Length > 0) device.Name = device.ProductName;
                if (device.ContainerId.Length > 0) { device.PhysicalId = device.Id; device.Id = "input:" + group; }
            }
            result.Add(device);
        }
        return result;
    }
}

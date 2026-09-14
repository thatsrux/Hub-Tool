using System.Runtime.InteropServices;
using System.Text;
namespace HubTool;
internal static class Native
{
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] internal static extern int WindowStyle(IntPtr window,int index);
 [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 internal static void DarkCaption(IntPtr window) { int enabled=1; DwmSetWindowAttribute(window,20,ref enabled,sizeof(int)); }
 [StructLayout(LayoutKind.Sequential)] internal struct ScreenRect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] struct ScreenInfo { public uint Size; public ScreenRect Monitor,Work; public uint Flags; }
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref ScreenInfo info);
 internal static ScreenRect WorkArea(IntPtr window)
 {
  var info=new ScreenInfo{Size=(uint)Marshal.SizeOf<ScreenInfo>()};
  if(!GetMonitorInfo(MonitorFromWindow(window,2),ref info))throw new System.ComponentModel.Win32Exception();
  return info.Work;
 }
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(IntPtr guid,string? enumerator,IntPtr parent,uint flags);
 [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref Info info);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref Info info,StringBuilder id,uint size,out uint required);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set,ref Info info,uint property,out uint type,byte[] buffer,uint size,out uint required);
 [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
 [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceProperty(IntPtr set,ref Info info,ref PropertyKey key,out uint type,byte[] buffer,uint size,out uint required,uint flags);
 [StructLayout(LayoutKind.Sequential)]struct Info { public uint Size; public Guid Guid;public uint DevInst;public UIntPtr Reserved; }
 [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr window,int id);
 public static List<Device> Enumerate()
 {
  var result=new List<Device>();var set=SetupDiGetClassDevs(IntPtr.Zero,null,IntPtr.Zero,6);if(set==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
  try{for(uint i=0;;i++){var info=new Info{Size=(uint)Marshal.SizeOf<Info>()};if(!SetupDiEnumDeviceInfo(set,i,ref info)){if(Marshal.GetLastWin32Error()!=259)throw new System.ComponentModel.Win32Exception();break;}
   string Property(uint p){var bytes=new byte[8192];return SetupDiGetDeviceRegistryProperty(set,ref info,p,out _,bytes,(uint)bytes.Length,out _)?Encoding.Unicode.GetString(bytes).Split('\0')[0]:"";}
   var id=new StringBuilder(4096);if(!SetupDiGetDeviceInstanceId(set,ref info,id,4096,out _))continue;
   var name=Property(12);if(name.Length==0)name=Property(0);var kind=Property(7);
   if(!PeripheralCatalog.IsNativePeripheral(kind,id.ToString(),name))continue;
   var buffer=new byte[2048];var containerKey=new PropertyKey{Format=new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),Id=2};
   var container=SetupDiGetDeviceProperty(set,ref info,ref containerKey,out _,buffer,(uint)buffer.Length,out var count,0)&&count==16?new Guid(buffer.AsSpan(0,16)).ToString():"";
   if(container==Guid.Empty.ToString()||container=="00000000-0000-0000-ffff-ffffffffffff")container="";
   var descKey=new PropertyKey{Format=new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),Id=4};
   var product=SetupDiGetDeviceProperty(set,ref info,ref descKey,out _,buffer,(uint)buffer.Length,out count,0)?Encoding.Unicode.GetString(buffer,0,(int)count).Split('\0')[0]:"";
   result.Add(new Device{Id=id.ToString(),Name=name.Length>0?name:id.ToString(),Kind=kind,ProductName=product,ContainerId=container,Manufacturer=Property(11),Driver=Property(9),Connected=true});
  }}finally{SetupDiDestroyDeviceInfoList(set);}return result;
 }
}

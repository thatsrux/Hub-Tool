using System.Runtime.InteropServices;
using System.Text;
namespace HubTool;
internal static class Native
{
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(IntPtr guid,string? enumerator,IntPtr parent,uint flags);
 [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref Info info);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref Info info,StringBuilder id,uint size,out uint required);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set,ref Info info,uint property,out uint type,byte[] buffer,uint size,out uint required);
 [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
 [StructLayout(LayoutKind.Sequential)]struct Info { public uint Size; public Guid Guid;public uint DevInst;public UIntPtr Reserved; }
 [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr window,int id);
 [DllImport("user32.dll",SetLastError=true)]internal static extern bool SystemParametersInfo(uint action,uint param,IntPtr value,uint flags);
 public static List<Device> Enumerate()
 {
  var result=new List<Device>();var set=SetupDiGetClassDevs(IntPtr.Zero,null,IntPtr.Zero,6);if(set==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
  try{for(uint i=0;;i++){var info=new Info{Size=(uint)Marshal.SizeOf<Info>()};if(!SetupDiEnumDeviceInfo(set,i,ref info)){if(Marshal.GetLastWin32Error()!=259)throw new System.ComponentModel.Win32Exception();break;}
   string Property(uint p){var bytes=new byte[8192];return SetupDiGetDeviceRegistryProperty(set,ref info,p,out _,bytes,(uint)bytes.Length,out _)?Encoding.Unicode.GetString(bytes).TrimEnd('\0'):"";}
   var id=new StringBuilder(4096);if(!SetupDiGetDeviceInstanceId(set,ref info,id,4096,out _))continue;
   var name=Property(12);if(name.Length==0)name=Property(0);result.Add(new Device{Id=id.ToString(),Name=name.Length>0?name:id.ToString(),Kind=Property(7),Manufacturer=Property(11),Driver=Property(9),Connected=true});
  }}finally{SetupDiDestroyDeviceInfoList(set);}return result;
 }
}

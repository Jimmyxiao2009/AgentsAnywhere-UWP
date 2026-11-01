param([string]$Arguments='')
$ErrorActionPreference='Stop'
$pkg=Get-AppxPackage -Name 'AgentsAnywhere.Mobile'
if(-not $pkg){throw 'Deploy the UWP app first.'}
if(-not ('AgentsAnywhere.Launcher' -as [type])) {
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace AgentsAnywhere {
 [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
 class ApplicationActivationManager {}
 [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IApplicationActivationManager {
  [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,[MarshalAs(UnmanagedType.LPWStr)] string args,uint options,out uint pid);
  [PreserveSig] int ActivateForFile(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)] string verb,out uint pid);
  [PreserveSig] int ActivateForProtocol(IntPtr items,out uint pid);
 }
 public static class Launcher {
  public static uint Launch(string id,string args) {
   var manager=(IApplicationActivationManager)new ApplicationActivationManager();
   uint pid;Marshal.ThrowExceptionForHR(manager.ActivateApplication(id,args,0,out pid));return pid;
  }
 }
}
'@
}
$processId=[AgentsAnywhere.Launcher]::Launch($pkg.PackageFamilyName+'!App',$Arguments)
Write-Output "App launched: pid=$processId, args=$Arguments"


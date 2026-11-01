param([ValidateSet('Debug','Release')][string]$Configuration='Debug',[ValidateSet('x86','x64')][string]$Platform='x86')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$buildDir=Join-Path $projectRoot "AgentsAnywhere.Mobile\bin\$Platform\$Configuration"
$recipePath=Join-Path $buildDir 'AgentsAnywhere.Mobile.build.appxrecipe'
if(-not(Test-Path -LiteralPath $recipePath)){throw 'Build the application first.'}
Get-Process -Name AgentsAnywhere.Mobile -ErrorAction SilentlyContinue | Stop-Process
[xml]$recipe=Get-Content -LiteralPath $recipePath
$ns=[Xml.XmlNamespaceManager]::new($recipe.NameTable)
$ns.AddNamespace('m','http://schemas.microsoft.com/developer/msbuild/2003')
$layout=Join-Path $buildDir 'AppX'
[IO.Directory]::CreateDirectory($layout) | Out-Null
foreach($item in $recipe.SelectNodes('//m:AppxPackagedFile|//m:AppXManifest',$ns)){
 $source=[Uri]::UnescapeDataString($item.GetAttribute('Include'))
 $relative=$item.SelectSingleNode('m:PackagePath',$ns).InnerText
 $target=[IO.Path]::GetFullPath((Join-Path $layout $relative))
 if(-not $target.StartsWith($layout+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Invalid package path $relative"}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
 Copy-Item -LiteralPath $source -Destination $target -Force
}
$packageSuffix=if($Configuration -eq 'Debug'){'_Debug'}else{''}
$dependencyDir=Join-Path $projectRoot "AgentsAnywhere.Mobile\AppPackages\AgentsAnywhere.Mobile_1.0.0.5_${Platform}${packageSuffix}_Test\Dependencies\$Platform"
$dependencies=@(Get-ChildItem -LiteralPath $dependencyDir -Filter '*.appx' -ErrorAction SilentlyContinue | ForEach-Object {$_.FullName})
foreach($dependency in $dependencies){Add-AppxPackage -Path $dependency -ForceApplicationShutdown}
$manifest=Join-Path $layout 'AppxManifest.xml'
Add-AppxPackage -Register $manifest -ForceApplicationShutdown
Get-AppxPackage -Name 'AgentsAnywhere.Mobile' | Select-Object Name,Version,PackageFamilyName,InstallLocation


param([string]$DeviceOrigin='http://192.168.3.159')
$ErrorActionPreference='Stop'
$base=$DeviceOrigin.TrimEnd('/')
$json=& curl.exe --noproxy '*' --silent --show-error --fail --max-time 15 "$base/api/app/packagemanager/packages"
if($LASTEXITCODE -ne 0){throw 'Cannot reach Windows Device Portal.'}
$app=($json | ConvertFrom-Json).InstalledPackages | Where-Object {$_.PackageFullName -like 'AgentsAnywhere.Mobile_*'} | Select-Object -First 1
if(-not $app){throw 'Agents Anywhere UWP is not installed.'}
$appId=[Uri]::EscapeDataString([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($app.PackageRelativeId)))
$packageId=[Uri]::EscapeDataString([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($app.PackageFullName)))
& curl.exe --noproxy '*' --silent --show-error --fail --max-time 15 -X POST -H 'Content-Length: 0' "$base/api/taskmanager/app?appid=$appId&package=$packageId"
if($LASTEXITCODE -ne 0){throw 'App launch failed.'}
Write-Output "Launched $($app.PackageFullName)"

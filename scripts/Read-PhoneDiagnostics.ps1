param([string]$DeviceOrigin='http://192.168.3.159')
$ErrorActionPreference='Stop'
$base=$DeviceOrigin.TrimEnd('/')
$packagesJson=& curl.exe --noproxy '*' --silent --show-error --fail --max-time 15 "$base/api/app/packagemanager/packages"
if($LASTEXITCODE -ne 0){throw 'Cannot reach Windows Device Portal.'}
$package=($packagesJson | ConvertFrom-Json).InstalledPackages | Where-Object {$_.PackageFullName -like 'AgentsAnywhere.Mobile_*'} | Select-Object -First 1
if(-not $package){throw 'Agents Anywhere UWP is not installed.'}
$url="$base/api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=$([Uri]::EscapeDataString($package.PackageFullName))&path=%5CLocalState&filename=diagnostics.log"
& curl.exe --noproxy '*' --silent --show-error --fail --max-time 15 $url
if($LASTEXITCODE -ne 0){throw 'Diagnostics unavailable. Launch the updated app and try login first.'}

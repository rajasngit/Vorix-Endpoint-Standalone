$ErrorActionPreference='Stop'
$engine=Join-Path $PSScriptRoot 'pilot/windows/clamav'
$zip=Join-Path $PSScriptRoot 'pilot/clamav-1.5.4.win.x64.zip'
$source=Join-Path $PSScriptRoot 'standalone/upstream/clamav-1.5.4.tar.gz'
function Download-Verified($url,$path,$sha) {
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    if(!(Test-Path $path)){Invoke-WebRequest -Uri $url -OutFile $path}
    if((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha){throw "Vendor SHA-256 mismatch: $path"}
}
Download-Verified 'https://www.clamav.net/downloads/production/clamav-1.5.4.win.x64.zip' $zip '0d9e0228b2674137ea1a2853566c98a0278ad52ab2582c3d6dbd75373848c395'
Download-Verified 'https://www.clamav.net/downloads/production/clamav-1.5.4.tar.gz' $source '1af1117a228f1b5bc7fa91a0dabc37848a99e7d25188e9be8043332ce721dfd3'
if(!(Test-Path "$engine/freshclam.exe")){New-Item -ItemType Directory -Force $engine | Out-Null;$vendor=Join-Path $PSScriptRoot 'pilot/windows/vendor';Expand-Archive -Path $zip -DestinationPath $vendor -Force;Copy-Item "$vendor/clamav-1.5.4.win.x64/*" $engine -Recurse -Force}
$pythonZip=Join-Path $PSScriptRoot 'pilot/python-3.14.10-embed-amd64.zip'
$pythonHome=Join-Path $PSScriptRoot 'pilot/windows/python'
Download-Verified 'https://www.python.org/ftp/python/3.14.10/python-3.14.10-embed-amd64.zip' $pythonZip '90905e64a840557d8c1bc4f5b74db3d7ee85e3ffb217611cac1c7813acea4159'
if(!(Test-Path "$pythonHome/python.exe")){New-Item -ItemType Directory -Force $pythonHome | Out-Null;Expand-Archive -Path $pythonZip -DestinationPath $pythonHome -Force}
& "$PSScriptRoot/packaging/build-standalone.ps1"

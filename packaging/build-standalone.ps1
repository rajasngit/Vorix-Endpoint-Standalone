$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
& "$workspace\standalone\build.ps1"
$payload=Join-Path $workspace 'dist\Vorix-Endpoint-Standalone-1.1.0-win-x64'
Copy-Item "$PSScriptRoot\windows\Build-Setup.ps1" "$payload\source" -Force
(Get-Content "$PSScriptRoot\windows\Setup.cs" -Raw) | Set-Content "$payload\source\Setup.cs" -Encoding utf8
$zip=Join-Path $workspace 'dist\standalone-setup-payload-1.1.0.zip'
Compress-Archive -Path "$payload\*" -DestinationPath $zip -CompressionLevel Optimal -Force
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $workspace 'dist\Vorix-Standalone-Setup-1.1.0-x64.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.Web.Extensions.dll "/win32manifest:$workspace\standalone\admin.manifest" "/win32icon:$workspace\standalone\vorix.ico" "/resource:$zip,payload.zip" "/resource:$workspace\server\static\vorix-logo-transparent.png,logo.png" "/out:$exe" "$payload\source\Setup.cs"
if($LASTEXITCODE){throw 'Standalone setup compilation failed'}
(Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()+'  '+(Split-Path $exe -Leaf) | Set-Content "$exe.sha256.txt" -Encoding ascii
Write-Output $exe




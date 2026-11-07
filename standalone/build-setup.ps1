$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot\build.ps1" -SkipArchive
if($LASTEXITCODE){throw 'Standalone package build failed'}
$package=Join-Path $workspace 'dist\Vorix-Endpoint-Standalone-1.2.0-win-x64'
$native=Join-Path $workspace 'packaging\windows'
Copy-Item "$native\Setup.cs","$native\Build-Setup.ps1" "$package\source" -Force
(Get-Content "$package\source\Setup.cs" -Raw -Encoding UTF8) | Set-Content "$package\source\Setup.cs" -Encoding utf8
Get-ChildItem $package -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $relative=$_.FullName.Substring($package.Length+1).Replace('\','/')
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()+'  '+$relative
} | Set-Content "$package\SHA256SUMS.txt" -Encoding utf8
Compress-Archive -Path $package -DestinationPath "$package.zip" -CompressionLevel Optimal -Force
(Get-FileHash "$package.zip" -Algorithm SHA256).Hash.ToLower()+'  '+(Split-Path "$package.zip" -Leaf) | Set-Content "$package.zip.sha256.txt" -Encoding ascii
$payload=Join-Path $workspace 'dist\standalone-setup-payload.zip'
Compress-Archive -Path "$package\*" -DestinationPath $payload -CompressionLevel Optimal -Force
$exe=Join-Path $workspace 'dist\Vorix-Standalone-Setup-1.2.0-x64.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.Web.Extensions.dll "/win32manifest:$PSScriptRoot\admin.manifest" "/win32icon:$PSScriptRoot\vorix.ico" "/resource:$payload,payload.zip" "/resource:$workspace\server\static\vorix-logo-transparent.png,logo.png" "/out:$exe" "$package\source\Setup.cs"
if($LASTEXITCODE){throw 'Standalone installer compilation failed'}
(Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()+'  '+(Split-Path $exe -Leaf) | Set-Content "$exe.sha256.txt" -Encoding ascii
Write-Output $exe

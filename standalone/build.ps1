$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$package = Join-Path $workspace 'dist\Vorix-Endpoint-Standalone-1.1.0-win-x64'
New-Item -ItemType Directory -Force $package, "$package\clamav", "$package\source" | Out-Null
if (Test-Path -LiteralPath "$package\source\build.ps1") { Remove-Item -LiteralPath "$package\source\build.ps1" }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$PSScriptRoot\update-public-key.xml,update-key.xml" "/win32icon:$PSScriptRoot\vorix.ico" "/out:$package\Vorix Endpoint.exe" "$PSScriptRoot\Vorix.cs" "$PSScriptRoot\Automation.cs" "$PSScriptRoot\Quarantine.cs"
if ($LASTEXITCODE) { throw 'Compilation failed' }
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll "/win32manifest:$PSScriptRoot\admin.manifest" "/out:$package\Uninstall.exe" "$PSScriptRoot\Uninstall.cs"
if ($LASTEXITCODE) { throw 'Uninstaller compilation failed' }
Copy-Item "$PSScriptRoot\README.txt", "$PSScriptRoot\Install.cmd", "$workspace\server\static\vorix-logo-transparent.png" $package -Force
Copy-Item "$PSScriptRoot\Vorix.cs", "$PSScriptRoot\Automation.cs", "$PSScriptRoot\Build-Application.ps1", "$PSScriptRoot\vorix.ico" "$package\source" -Force
Copy-Item "$PSScriptRoot\Quarantine.cs", "$PSScriptRoot\Automation.cs", "$PSScriptRoot\update-public-key.xml", "$PSScriptRoot\Uninstall.cs", "$PSScriptRoot\admin.manifest" "$package\source" -Force
$engine = Join-Path $workspace 'pilot\windows\clamav'
Get-ChildItem $engine -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -in @('clamscan.exe','freshclam.exe','sigtool.exe','COPYING.txt','README.md','NEWS.md') } | Copy-Item -Destination "$package\clamav" -Force
Copy-Item "$engine\COPYING", "$engine\certs" "$package\clamav" -Recurse -Force
Copy-Item "$engine\COPYING.txt" "$package\source\LICENSE.txt" -Force
Copy-Item "$engine\COPYING.txt" "$package\LICENSE.txt" -Force
Copy-Item "$PSScriptRoot\upstream\clamav-1.5.4.tar.gz" "$package\source" -Force
Get-ChildItem $package -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length + 1).Replace('\','/')
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower() + '  ' + $relative
} | Set-Content "$package\SHA256SUMS.txt" -Encoding utf8
$zip = "$package.zip"
Compress-Archive -Path $package -DestinationPath $zip -CompressionLevel Optimal -Force
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path $zip -Leaf) | Set-Content "$zip.sha256.txt" -Encoding ascii
Write-Output $zip





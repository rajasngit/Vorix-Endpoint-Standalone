# Rebuild the Vorix application from the source delivered in this package.
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'Vorix Endpoint.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$PSScriptRoot\update-public-key.xml,update-key.xml" "/win32icon:$PSScriptRoot\vorix.ico" "/out:$destination" "$PSScriptRoot\Vorix.cs" "$PSScriptRoot\Automation.cs" "$PSScriptRoot\Quarantine.cs"
if ($LASTEXITCODE) { throw 'Compilation failed' }
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll "/win32manifest:$PSScriptRoot\admin.manifest" "/out:$(Split-Path $PSScriptRoot -Parent)\Uninstall.exe" "$PSScriptRoot\Uninstall.cs"
if ($LASTEXITCODE) { throw 'Uninstaller compilation failed' }

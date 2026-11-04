param([switch]$Managed)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$zip=Join-Path (Split-Path $root -Parent) 'rebuild-setup-payload.zip'
Compress-Archive -Path "$root\*" -DestinationPath $zip -Force
$output=Join-Path (Split-Path $root -Parent) 'Vorix-Rebuilt-Setup.exe'
$options=@('/nologo','/target:winexe','/platform:x64','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.Web.Extensions.dll',"/win32manifest:$PSScriptRoot\admin.manifest","/resource:$zip,payload.zip","/resource:$root\vorix-logo-transparent.png,logo.png","/out:$output")
if($Managed){$options+='/define:MANAGED'}
& $compiler @options "$PSScriptRoot\Setup.cs"
if($LASTEXITCODE){throw 'Compilation failed'}

$ErrorActionPreference='Stop'
& "$(Split-Path $PSScriptRoot -Parent)\standalone\build-setup.ps1"
if($LASTEXITCODE){throw 'Standalone build failed'}

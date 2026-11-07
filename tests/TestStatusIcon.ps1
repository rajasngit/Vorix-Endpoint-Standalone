param([string]$App,[int]$ProcessId=0)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $App))
$method=$assembly.GetType('StandaloneStatus').GetMethod('IsMonitorProcessPresent')
if($method.Invoke($null,@([int]2147483647))){throw 'Missing process incorrectly reported as running'}
if($ProcessId -gt 0 -and !$method.Invoke($null,@($ProcessId))){throw 'Live SYSTEM Python monitor was not detected'}
'PASS: monitor process detection needs no privileged process handle'

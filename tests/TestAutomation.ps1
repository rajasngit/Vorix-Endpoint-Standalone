param([string]$App,[string]$Manifest,[string]$Signature)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $App))
$type=$assembly.GetType('Vorix')
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$validate=$type.GetMethod('ValidateOptions',$flags)
$options=[Activator]::CreateInstance($assembly.GetType('Vorix+Options'))
$validate.Invoke($null,@($options)) | Out-Null
if($options.ScanDay -ne 0 -or $options.ScanTime -ne '02:00' -or !$options.ScanEnabled -or $options.UpgradeMode -ne 'prompt'){throw 'Unexpected defaults'}
$options.ScanDay=7
$rejected=$false;try{$validate.Invoke($null,@($options)) | Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Invalid day accepted'}
$options.ScanDay=0;$options.ScanTime='25:00'
$rejected=$false;try{$validate.Invoke($null,@($options)) | Out-Null}catch{$rejected=$true};if(!$rejected){throw 'Invalid time accepted'}
if($Manifest){
 $verify=$type.GetMethod('VerifiedManifest',$flags)
 $bytes=[IO.File]::ReadAllBytes((Resolve-Path $Manifest))
 $sig=[Convert]::FromBase64String([IO.File]::ReadAllText((Resolve-Path $Signature)))
 $null=$verify.Invoke($null,@($bytes,$sig))
 $bytes[0]=$bytes[0] -bxor 1
 $rejected=$false;try{$null=$verify.Invoke($null,@($bytes,$sig))}catch{$rejected=$true};if(!$rejected){throw 'Tampered manifest accepted'}
}
'PASS: weekly defaults, invalid settings rejected, publisher signature valid and tampering rejected'

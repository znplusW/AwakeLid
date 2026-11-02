[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RecycleSource,[Parameter(Mandatory=$true)][string]$ScratchRoot,[switch]$ClassificationOnly)
$ErrorActionPreference='Stop'
Add-Type -Path $RecycleSource
foreach($tag in @([uint32]0,[uint32]0x9000001AL,[uint32]0x9000E01AL,[uint32]0x9000F01AL)) {
    if(-not [AwakeLidRecycleOnly]::IsSupportedReparseTag($tag)){throw ('Valid cloud tag rejected: {0:X8}' -f $tag)}
}
foreach($tag in @([uint32]0xA0000003L,[uint32]0xA000000CL,[uint32]0x80000018L)) {
    if([AwakeLidRecycleOnly]::IsSupportedReparseTag($tag)){throw ('Unknown or redirect tag accepted: {0:X8}' -f $tag)}
}
if($ClassificationOnly){Write-Output 'PASS: recycle-tag classification (Shell recycling explicitly skipped).';return}
$root=[IO.Path]::GetFullPath($ScratchRoot)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$real=Join-Path $root 'real'
$alias=Join-Path $root 'alias'
New-Item -ItemType Directory -Path $real -Force | Out-Null
if(-not (Test-Path -LiteralPath $alias)){New-Item -ItemType Junction -Path $alias -Target $real | Out-Null}
if([AwakeLidRecycleOnly]::CanonicalPath($alias) -ne [AwakeLidRecycleOnly]::CanonicalPath($real)){throw 'Canonical path must resolve a final parent junction alias'}
$child=Join-Path $alias 'child'
New-Item -ItemType Directory -Path $child -Force | Out-Null
$expected=Join-Path $real 'child'
if([AwakeLidRecycleOnly]::CanonicalPath($child) -ne [AwakeLidRecycleOnly]::CanonicalPath($expected)){throw 'Parent junction alias did not resolve to same target'}
[AwakeLidRecycleOnly]::ValidateTarget($child) | Out-Null
$blocked=$false
try{[AwakeLidRecycleOnly]::ValidateTarget($alias) | Out-Null}catch{$blocked=$true}
if(-not $blocked){throw 'Top-level junction must be rejected without touching its target'}
$recycled=[AwakeLidRecycleOnly]::Send($child)
if((Test-Path -LiteralPath $child) -or -not (Test-Path -LiteralPath $recycled)){throw 'Child was not verifiably recycled'}
if(-not (Test-Path -LiteralPath $alias) -or -not (Test-Path -LiteralPath $real)){throw 'Parent junction or its target was changed'}
$sink=New-Object AwakeLidRecycleOnly+RecycleSink
if($sink.PreDeleteItem(0,$null) -ge 0){throw 'Permanent-delete request was not rejected'}
Write-Output 'PASS: cloud-tag classification, redirect rejection, canonical aliases, parent junction preservation, verified recycle and permanent-delete veto.'

$ErrorActionPreference='Stop'
$process=Start-Process -FilePath (Join-Path $PSScriptRoot 'AwakeLid.exe') -ArgumentList '--stop' -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0) { throw '恢复未完成，请查看同目录“操作结果.txt”和 data 中的恢复记录。' }
Write-Host '外挂已停止，临时合盖设置已检查恢复。'

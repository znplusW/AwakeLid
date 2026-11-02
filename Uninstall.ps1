[CmdletBinding()]
param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$markerPath = Join-Path $package 'package.json'
if (-not (Test-Path -LiteralPath $markerPath)) { throw '找不到本外挂的包标识，停止卸载。' }
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
if ($marker.id -ne 'AwakeLid-414C4944-v1') { throw '包标识不匹配，停止卸载。' }
$executable = Join-Path $package 'AwakeLid.exe'
$nativeDll = Join-Path $package 'AwakeLidHook.dll'
if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath $nativeDll)) { throw '包文件不完整，请先恢复文件再卸载。' }
if ($package -eq [IO.Path]::GetPathRoot($package).TrimEnd('\') -or $package -eq [Environment]::GetFolderPath('UserProfile')) { throw '卸载路径不安全。' }
Add-Type -Path (Join-Path $package 'RecycleOnly.cs')
$targetTag=[AwakeLidRecycleOnly]::ValidateTarget($package)
$canonicalPackage=[AwakeLidRecycleOnly]::CanonicalPath($package)
$canonicalDll=[AwakeLidRecycleOnly]::CanonicalPath($nativeDll)
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'AwakeLid.lnk'
$ownStartup = $false
if (Test-Path -LiteralPath $startup) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startup)
    $ownStartup = (Test-Path -LiteralPath $shortcut.TargetPath) -and ([AwakeLidRecycleOnly]::CanonicalPath($shortcut.TargetPath) -eq [AwakeLidRecycleOnly]::CanonicalPath($executable))
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
}
if ($DryRun) {
    [pscustomobject]@{ Package=$package; CanonicalPackage=$canonicalPackage; ReparseTag=('0x{0:X8}' -f $targetTag); StartupOwned=$ownStartup; Action='恢复设置、退出助手、检查 DLL 卸载，将本目录送入系统回收站' } | ConvertTo-Json
    exit 0
}
# Stop only this package. Never terminate PowerToys, Awake, or unrelated helpers.
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.', ('AwakeLid.v1.' + [Environment]::UserName), [IO.Pipes.PipeDirection]::InOut)
try {
    try { $pipe.Connect(1500) } catch [TimeoutException] { }
    if ($pipe.IsConnected) {
        $writer = New-Object IO.StreamWriter($pipe, (New-Object Text.UTF8Encoding($false)), 1024, $true)
        $reader = New-Object IO.StreamReader($pipe, [Text.Encoding]::UTF8, $true, 1024, $true)
        $writer.AutoFlush=$true
        $writer.WriteLine('status')
        $task=$reader.ReadLineAsync()
        if (-not $task.Wait(5000)) { throw '后台助手未响应，停止卸载。' }
        $remote=$task.Result | ConvertFrom-Json
        if ([AwakeLidRecycleOnly]::CanonicalPath($remote.directory) -ne $canonicalPackage) { throw '另一目录中的外挂正在运行，停止本次卸载。' }
        $reader.Dispose(); $writer.Dispose()
    }
} finally { $pipe.Dispose() }
$stop = Start-Process -FilePath $executable -ArgumentList '--stop' -WindowStyle Hidden -Wait -PassThru
if ($stop.ExitCode -ne 0) { throw '停止或恢复设置失败。请查看“操作结果.txt”和 data 记录，文件已保留。' }
$statePath=Join-Path $package 'data\state.json'
if (Test-Path -LiteralPath $statePath) {
    $state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if (@($state.Leases).Count -gt 0) { throw '仍存在未恢复的合盖设置，停止卸载并保留记录。' }
}
# Windows may lazily release a hook DLL on the target's next message.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AwakeLidUninstallNative {
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback, IntPtr data);
 public delegate bool Callback(IntPtr window, IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr window,uint msg,IntPtr wp,IntPtr lp,uint flags,uint timeout,out UIntPtr result);
}
'@
$deadline=[DateTime]::UtcNow.AddSeconds(6)
do {
    $loaded=$false
    foreach ($target in @(Get-Process -Name 'PowerToys.Awake' -ErrorAction SilentlyContinue)) {
        try {
            foreach ($module in $target.Modules) {
                if ($module.ModuleName -eq 'AwakeLidHook.dll' -and [AwakeLidRecycleOnly]::CanonicalPath($module.FileName) -eq $canonicalDll) { $loaded=$true }
            }
        } catch { throw '无法核实 Awake 是否已释放外挂 DLL。请用与 PowerToys 相同的权限再次运行卸载；文件已保留。' }
        if ($loaded) {
            $targetPid=$target.Id
            [AwakeLidUninstallNative]::EnumWindows({ param($hwnd,$unused)
                [uint32]$owner=0
                [AwakeLidUninstallNative]::GetWindowThreadProcessId($hwnd,[ref]$owner) | Out-Null
                if ($owner -eq $targetPid) {
                    [uintptr]$reply=0
                    [AwakeLidUninstallNative]::SendMessageTimeoutW($hwnd,0,[IntPtr]::Zero,[IntPtr]::Zero,2,500,[ref]$reply) | Out-Null
                }
                return $true
            },[IntPtr]::Zero) | Out-Null
        }
    }
    if ($loaded) { Start-Sleep -Milliseconds 200 }
} while ($loaded -and [DateTime]::UtcNow -lt $deadline)
if ($loaded) { throw 'Awake 尚未释放 DLL。请正常重启 Awake 后再次卸载；不会强制结束 PowerToys。' }
# FOFX_RECYCLEONDELETE plus a sink that rejects non-recycle deletion. No permanent-delete fallback.
if ($ownStartup) {
    [AwakeLidRecycleOnly]::Send($startup) | Out-Null
    if (Test-Path -LiteralPath $startup) { throw '开机启动项未能送入回收站，停止卸载。' }
}
Set-Location -LiteralPath ([IO.Path]::GetDirectoryName($package))
Write-Host '合盖设置已检查恢复，Hook 已移除。正在将外挂目录送入系统回收站。'
[AwakeLidRecycleOnly]::Send($package) | Out-Null
if (Test-Path -LiteralPath $package) { throw '目录未能进入回收站；未使用永久删除。' }
Write-Host '卸载完成。PowerToys 和系统其他睡眠设置未被修改。'

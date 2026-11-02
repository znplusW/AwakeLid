[CmdletBinding()]
param([string]$ZigPath,[switch]$Ci)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$build=Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $build -Force | Out-Null
if(-not $ZigPath){$ZigPath=& (Join-Path $root 'Get-Zig.ps1')}
$ZigPath=(Resolve-Path -LiteralPath $ZigPath).Path
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $build 'zig-global-cache'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $build 'zig-local-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
function Check-Exit([string]$step){if($LASTEXITCODE -ne 0){throw "$step failed: $LASTEXITCODE"}}
& $ZigPath cc -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -o (Join-Path $build 'AwakeLidHook.dll') (Join-Path $root 'src\NativeHook\hook.c') -lcomctl32 -luser32 -lkernel32
Check-Exit 'Native build'
Push-Location -LiteralPath $root
try {
    dotnet build (Join-Path $root 'src\AwakeLid\AwakeLid.csproj') -c Release -o (Join-Path $build 'app') -p:NuGetAudit=false --nologo
    Check-Exit 'Guardian build'
    dotnet build (Join-Path $root 'tests\Tests.csproj') -c Release -o (Join-Path $build 'tests') -p:NuGetAudit=false --nologo
    Check-Exit 'Test build'
} finally {Pop-Location}
& $ZigPath cc -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -o (Join-Path $build 'MockAwake.exe') (Join-Path $root 'tests\mock.c') -luser32 -lkernel32
Check-Exit 'Mock build'
& $ZigPath cc -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -o (Join-Path $build 'AwakeLid.exe') (Join-Path $root 'tests\recovery-recorder.c') -lkernel32
Check-Exit 'Recovery recorder build'
$arguments=@((Join-Path $build 'tests\Tests.dll'),(Join-Path $build 'MockAwake.exe'),(Join-Path $build 'AwakeLidHook.dll'))
if($Ci){$arguments+='--skip-real-power'}
dotnet @arguments
Check-Exit 'Core/native tests'
$recycleArgs=@{RecycleSource=(Join-Path $root 'RecycleOnly.cs');ScratchRoot=(Join-Path $build 'recycle-tests')}
if($Ci){$recycleArgs.ClassificationOnly=$true}
& (Join-Path $root 'tests\Test-Recycle.ps1') @recycleArgs
Write-Output 'BUILD AND TESTS PASSED'

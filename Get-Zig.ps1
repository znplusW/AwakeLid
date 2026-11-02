[CmdletBinding()]
param([string]$CacheDirectory=(Join-Path $PSScriptRoot 'artifacts\toolchain'))
$ErrorActionPreference='Stop'
$version='0.14.1'
$expected='554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$archive=Join-Path $CacheDirectory 'zig-0.14.1.zip'
if(-not(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://ziglang.org/download/$version/zig-x86_64-windows-$version.zip" -OutFile $archive
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Zig archive checksum mismatch. Archive preserved; do not execute its contents.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $CacheDirectory -Force
$executable=Join-Path $CacheDirectory "zig-x86_64-windows-$version\zig.exe"
if(-not(Test-Path -LiteralPath $executable)){throw 'Verified Zig archive did not contain the expected executable.'}
Write-Output ([IO.Path]::GetFullPath($executable))

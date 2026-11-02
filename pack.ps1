[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src\AwakeLid\AwakeLid.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
$app=Join-Path $root 'artifacts\app'
$exe=Join-Path $app 'AwakeLid.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Run build.ps1 first.'}
if((Get-Item -LiteralPath (Join-Path $app 'AwakeLid.dll')).VersionInfo.ProductVersion.Split('+')[0] -ne $version){throw 'Built version does not match the source version.'}
$name="AwakeLid-v$version-win-x64"
$stage=Join-Path $root ('artifacts\stage\'+[Guid]::NewGuid().ToString('N')+'\'+$name)
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $stage,$dist -Force | Out-Null
foreach($file in @('AwakeLid.exe','AwakeLid.dll','AwakeLid.deps.json','AwakeLid.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path $app $file) -Destination $stage}
Copy-Item -LiteralPath (Join-Path $root 'artifacts\AwakeLidHook.dll') -Destination $stage
foreach($file in @('README.md','README.zh-CN.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md','Uninstall.ps1','Uninstall.cmd','RecycleOnly.cs','Stop.ps1','Stop.cmd','Status.cmd','InstallStartup.cmd')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $stage -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null
foreach($file in @('VALIDATION.md','VALIDATION.zh-CN.md','VALIDATION-0.1.2.md','LOCAL-RELEASE-VERIFICATION.md','RELEASING.md')){Copy-Item -LiteralPath (Join-Path $root ('docs\'+$file)) -Destination (Join-Path $stage 'docs')}
foreach($script in @(Get-ChildItem -LiteralPath $stage -Filter '*.ps1')) {
    [IO.File]::WriteAllText($script.FullName,[IO.File]::ReadAllText($script.FullName),[Text.UTF8Encoding]::new($true))
}
@{id='AwakeLid-414C4944-v1';version=$version;architecture='x64';validatedAwake='0.101.2362.0'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'package.json') -Encoding UTF8
$hashes=Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($stage.Length+1)
}
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
$zip=Join-Path $dist ($name+'.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip+'.sha256',$hash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output $zip

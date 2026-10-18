#requires -Version 7.1
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$EditorDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$coreProject = Join-Path $PSScriptRoot 'Core\Mo2.Core.csproj'
$testProject = Join-Path $PSScriptRoot 'Tests\Mo2.Tests.csproj'
$ordinaryProject = Join-Path $repo 'src\DarkestDungeonSaveEditor.Core\DarkestDungeonSaveEditor.Core.csproj'
foreach ($project in @($coreProject, $testProject, $ordinaryProject)) {
    & dotnet build $project -c $Configuration -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
$testHost = Join-Path $PSScriptRoot "Tests\bin\$Configuration\net8.0-windows\Mo2.Compatibility.Tests.dll"
& dotnet $testHost (Join-Path $PSScriptRoot 'Tests\workspaces')
if ($LASTEXITCODE -ne 0) { throw 'MO2 compatibility contracts failed.' }
$ordinaryDll = Join-Path $repo "src\DarkestDungeonSaveEditor.Core\bin\$Configuration\net8.0-windows\DarkestDungeonSaveEditor.Core.dll"
$version = ([xml][IO.File]::ReadAllText((Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version
if ($EditorDirectory) {
    $ordinaryDll = (Resolve-Path -LiteralPath (Join-Path $EditorDirectory 'DarkestDungeonSaveEditor.Core.dll')).Path
    $ordinaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($ordinaryDll).ProductVersion
    if (($ordinaryVersion -split '\+', 2)[0] -cne $version) {
        throw "The editor package version does not match $version ($ordinaryVersion)."
    }
}
& dotnet $testHost --api $ordinaryDll
if ($LASTEXITCODE -ne 0) { throw 'Replacement Core API/identity verification failed.' }

$revision = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source revision.' }
$run = Join-Path $PSScriptRoot ('artifacts\package-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$name = "DarkestDungeonSaveEditor-$version-MO2-patch-20261018-r4"
$package = Join-Path $run $name
$payload = Join-Path $package 'payload'
New-Item -ItemType Directory -Path (Join-Path $payload 'zh-CN') | Out-Null
$coreOutput = Join-Path $PSScriptRoot "Core\bin\$Configuration\net8.0-windows"
Copy-Item -LiteralPath (Join-Path $coreOutput 'DarkestDungeonSaveEditor.Core.dll') -Destination $payload
Copy-Item -LiteralPath (Join-Path $coreOutput 'zh-CN\DarkestDungeonSaveEditor.Core.resources.dll') -Destination (Join-Path $payload 'zh-CN')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mo2-compat.example.json') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'), (Join-Path $repo 'NOTICE') -Destination $package
$codecNotices = Join-Path $package 'tools\DDSaveEditor'
New-Item -ItemType Directory -Path $codecNotices | Out-Null
foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "tools\DDSaveEditor\$file") -Destination $codecNotices
}
# Ship the exact compatibility source and ordinary Core inputs used by this build.
# This also makes the optional replacement component independently rebuildable.
$sourceRoot = Join-Path $package 'source'
$patchSource = Join-Path $sourceRoot 'patch\mo2'
New-Item -ItemType Directory -Path (Join-Path $patchSource 'Core'), (Join-Path $patchSource 'Tests') | Out-Null
foreach ($directory in @('Core', 'Tests')) {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $directory) -File |
        Where-Object { $_.Extension -in @('.cs', '.csproj') } |
        Copy-Item -Destination (Join-Path $patchSource $directory)
}
foreach ($file in @('README.md', 'Generate-Core.ps1', 'Build.ps1', 'mo2-compat.example.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $patchSource
}
$ordinarySource = Join-Path $repo 'src\DarkestDungeonSaveEditor.Core'
$ordinaryDestination = Join-Path $sourceRoot 'src\DarkestDungeonSaveEditor.Core'
$sourceFiles = & git -C $repo ls-files --cached --others --exclude-standard -- src/DarkestDungeonSaveEditor.Core
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate ordinary Core source.' }
foreach ($relative in $sourceFiles) {
    $destination = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $destination
}
$assetDirectory = Join-Path $sourceRoot 'src\DarkestDungeonSaveEditor.App\Assets'
New-Item -ItemType Directory -Path $assetDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'src\DarkestDungeonSaveEditor.App\Assets\save-editor-icon.png') -Destination $assetDirectory
foreach ($file in @('Directory.Build.props', 'LICENSE', 'NOTICE')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $sourceRoot
}
$buildInfo = [ordered]@{
    editorVersion = $version
    assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($ordinaryDll).Version.ToString()
    sourceRevision = "$revision"
    configuration = $Configuration
    builtAtUtc = [DateTime]::UtcNow.ToString('O')
    ordinaryCoreSha256 = (Get-FileHash -LiteralPath $ordinaryDll -Algorithm SHA256).Hash
    replacementCoreSha256 = (Get-FileHash -LiteralPath (Join-Path $payload 'DarkestDungeonSaveEditor.Core.dll') -Algorithm SHA256).Hash
    compatibilityRevision = 'mapper-20261016-guards-20261018-r4'
    supportedMapper = 'game_support_DarkestDungeon_20261016_004446 (merge mod mapper)'
}
$buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'build-info.json') -Encoding utf8NoBOM
$zip = Join-Path $run ($name + '.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $run 'SHA256SUMS.txt'), "$hash  $name.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"

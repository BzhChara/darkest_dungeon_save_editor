#Requires -Version 7.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) {
    throw 'Build the Windows release on Windows.'
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $projectRoot 'src/DarkestDungeonSaveEditor.App/DarkestDungeonSaveEditor.App.csproj'
$versionProperties = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)
$version = [string]$versionProperties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$') {
    throw "Invalid release version in Directory.Build.props: $version"
}
$releaseNotes = Join-Path $projectRoot "docs/releases/v$version.md"
$installationNotes = Join-Path $projectRoot 'docs/release-installation.md'
foreach ($source in @($appProject, $releaseNotes, $installationNotes)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing release input: $source"
    }
}

# Each run gets a new directory. Never clean or overwrite a previous package.
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$releasesRoot = Join-Path $artifactsRoot 'releases'
foreach ($directory in @($artifactsRoot, $releasesRoot)) {
    if (Test-Path -LiteralPath $directory) {
        $entry = Get-Item -LiteralPath $directory -Force
        if (-not $entry.PSIsContainer -or ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "Release output must be an ordinary project directory: $directory"
        }
    }
}
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff') + '_' + [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $releasesRoot $runId
if (Test-Path -LiteralPath $runDirectory) {
    throw "Refusing to overwrite an existing release directory: $runDirectory"
}
$packageName = "DarkestDungeonSaveEditor-v$version-win-x64"
$packageDirectory = Join-Path $runDirectory $packageName
[void][System.IO.Directory]::CreateDirectory($packageDirectory)

$publishArguments = @(
    'publish', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
    '-m:1', '-o', $packageDirectory
)
& dotnet @publishArguments | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE. No release archive was created."
}

$requiredFiles = @(
    'DarkestDungeonSaveEditor.App.exe', 'DarkestDungeonSaveEditor.App.dll',
    'DarkestDungeonSaveEditor.Core.dll', 'DarkestDungeonSaveEditor.App.deps.json',
    'zh-CN/DarkestDungeonSaveEditor.Core.resources.dll',
    'DarkestDungeonSaveEditor.App.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
    'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'LICENSE', 'NOTICE',
    'tools/DDSaveEditor/DDSaveEditor.jar', 'tools/DDSaveEditor/LICENSE',
    'tools/DDSaveEditor/THIRD-PARTY-NOTICES.md'
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $packageDirectory $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "The published package is missing a required file: $relativePath"
    }
}
foreach ($assemblyName in @('DarkestDungeonSaveEditor.App', 'DarkestDungeonSaveEditor.Core')) {
    $assemblyPath = Join-Path $packageDirectory "$assemblyName.dll"
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
    if (($productVersion -split '\+', 2)[0] -cne $version) {
        throw "Published assembly version does not match ${version}: $assemblyName ($productVersion)"
    }
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $packageDirectory 'DarkestDungeonSaveEditor.App.runtimeconfig.json') -Raw |
    ConvertFrom-Json -AsHashtable
$runtimeOptions = $runtimeConfig.runtimeOptions
if ($runtimeOptions.ContainsKey('framework') -or $runtimeOptions.ContainsKey('frameworks') -or
    -not $runtimeOptions.ContainsKey('includedFrameworks')) {
    throw 'The package must contain its own .NET runtime, without a shared-framework dependency.'
}
foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    if (@($runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName }).Count -ne 1) {
        throw "The package is missing its included framework: $frameworkName"
    }
}

# Publish omits the runtime packs' license files. Copy them from the exact
# restored packages, using NuGet's recorded package roots instead of a user path.
$assetsPath = Join-Path (Split-Path -Parent $appProject) 'obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($framework in $runtimeOptions.includedFrameworks) {
    $packageId = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
    $packDirectory = $null
    foreach ($cacheRoot in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cacheRoot "$packageId/$($framework.version)"
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packDirectory = $candidate
            break
        }
    }
    if ($null -eq $packDirectory) {
        throw "Cannot locate the license source for $packageId $($framework.version)."
    }
    $noticeFiles = @(Get-ChildItem -LiteralPath $packDirectory -File | Where-Object {
        $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.(txt|md))?$'
    })
    if (@($noticeFiles | Where-Object { $_.Name -match '^LICENSE(\.(txt|md))?$' }).Count -eq 0) {
        throw "The restored runtime package has no license file: $packDirectory"
    }
    if ($framework.name -eq 'Microsoft.NETCore.App' -and
        @($noticeFiles | Where-Object { $_.Name -match '^THIRD-PARTY-NOTICES\.' }).Count -eq 0) {
        throw "The restored .NET runtime package has no third-party notices: $packDirectory"
    }
    $licenseDirectory = Join-Path $packageDirectory "licenses/$($framework.name)"
    [void][System.IO.Directory]::CreateDirectory($licenseDirectory)
    foreach ($notice in $noticeFiles) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseDirectory $notice.Name)
    }
}

Copy-Item -LiteralPath $installationNotes -Destination (Join-Path $packageDirectory 'START-HERE.md')
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $packageDirectory 'RELEASE-NOTES.md')
$archivePath = Join-Path $runDirectory "$packageName.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $packageDirectory, $archivePath, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $runDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText(
    $checksumPath, "$sha256  $packageName.zip`n", [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Version = $version
    PackageDirectory = $packageDirectory
    Archive = $archivePath
    Checksum = $checksumPath
    Sha256 = $sha256
}

#Requires -Version 7.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GameDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$coreProject = Join-Path $projectRoot "src/DarkestDungeonSaveEditor.Core/DarkestDungeonSaveEditor.Core.csproj"
if (-not (Test-Path -LiteralPath $coreProject -PathType Leaf)) {
    throw "Run the generator from the save-editor repository's tools directory."
}
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)

function Get-BytesSha256([byte[]]$Bytes) {
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function New-HeroProbe([string]$HeroId, [int]$SpeedBonus) {
    $relativePath = "heroes/$HeroId/$HeroId.info.darkest"
    $sourcePath = Join-Path $gameRoot $relativePath
    $sourceBytes = [System.IO.File]::ReadAllBytes($sourcePath)
    $sourceText = $utf8.GetString($sourceBytes)
    # Decoding retains a possible BOM as U+FEFF, so re-encoding preserves it.
    if ((Get-BytesSha256 $sourceBytes) -ne (Get-BytesSha256 $utf8.GetBytes($sourceText))) {
        throw "The source cannot be preserved as UTF-8: $sourcePath"
    }

    $weapons = [regex]::Matches($sourceText, '(?m)^weapon:[^\r\n]*')
    if ($weapons.Count -ne 5) {
        throw "Expected exactly five vanilla weapon ranks: $sourcePath"
    }
    $changes = @()
    foreach ($weapon in $weapons) {
        $name = [regex]::Match($weapon.Value, '\.name\s+"' + [regex]::Escape($HeroId) + '_weapon_([0-4])"')
        $speeds = [regex]::Matches($weapon.Value, '\.spd\s+(?<speed>[+-]?\d+)(?=\s|$)')
        if (-not $name.Success -or $speeds.Count -ne 1) {
            throw "Unexpected weapon definition; no package was generated: $sourcePath"
        }
        $speed = $speeds[0].Groups['speed']
        $original = [int]::Parse($speed.Value, [System.Globalization.CultureInfo]::InvariantCulture)
        if ($original -lt -20 -or $original -gt 20) {
            throw "Source speed is outside the probe's vanilla range: $sourcePath"
        }
        $changes += [pscustomobject]@{
            Rank = [int]$name.Groups[1].Value
            Offset = $weapon.Index + $speed.Index
            OriginalToken = $speed.Value
            OriginalSpeed = $original
            ProbeSpeed = $original + $SpeedBonus
        }
    }
    if ((($changes | ForEach-Object { $_.Rank } | Sort-Object -Unique) -join ',') -ne '0,1,2,3,4') {
        throw "Weapon ranks are duplicated or missing: $sourcePath"
    }

    $probeText = $sourceText
    foreach ($change in ($changes | Sort-Object Offset -Descending)) {
        $probeText = $probeText.Remove($change.Offset, $change.OriginalToken.Length).Insert(
            $change.Offset, $change.ProbeSpeed.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }
    $probeBytes = $utf8.GetBytes($probeText)
    return [pscustomobject]@{
        HeroId = $HeroId
        RelativePath = $relativePath
        SourcePath = $sourcePath
        SourceSha256 = Get-BytesSha256 $sourceBytes
        ProbeSha256 = Get-BytesSha256 $probeBytes
        Bytes = $probeBytes
        SpeedBonus = $SpeedBonus
        Changes = @($changes | Select-Object Rank, OriginalSpeed, ProbeSpeed)
    }
}

# Validate all source files before creating output. No installed Mod or save is read or written.
$probes = @(
    New-HeroProbe -HeroId 'crusader' -SpeedBonus 20
    New-HeroProbe -HeroId 'highwayman' -SpeedBonus 50
)
$modFolder = 'DDSE_Manifest_Loading_Probe'
$modTitle = 'DDSE Manifest Loading Probe'
$intendedInstallPath = (Join-Path $gameRoot "mods/$modFolder").Replace('\', '/').TrimEnd('/') + '/'
$escapedInstallPath = [System.Security.SecurityElement]::Escape($intendedInstallPath)
$projectXml = @"
<?xml version="1.0" encoding="utf-8"?>
<project>
  <PreviewIconFile/>
  <ItemDescriptionShort>Local A/B manifest probe. Temporary test profile only.</ItemDescriptionShort>
  <ModDataPath>$escapedInstallPath</ModDataPath>
  <Title>$modTitle</Title>
  <Language>english</Language>
  <Visibility>private</Visibility>
  <UploadMode>dont_submit</UploadMode>
  <VersionMajor>0</VersionMajor>
  <VersionMinor>1</VersionMinor>
  <TargetBuild>0</TargetBuild>
  <Tags><Tags>Gameplay Tweaks</Tags></Tags>
  <ItemDescription>Crusader weapon speed +20 is the listed control. Highwayman weapon speed +50 is listed in A and unlisted in B. Do not upload or use on existing profiles.</ItemDescription>
  <PublishedFileId>0</PublishedFileId>
</project>
"@
$sharedFiles = [ordered]@{ 'project.xml' = $utf8.GetBytes($projectXml) }
foreach ($probe in $probes) {
    $sharedFiles[$probe.RelativePath] = $probe.Bytes
}

$outputRoot = Join-Path $projectRoot 'workspaces/manifest_loading_probe'
# Do not follow an existing junction/symlink when creating the project-local package.
foreach ($directory in @((Join-Path $projectRoot 'workspaces'), $outputRoot)) {
    if (Test-Path -LiteralPath $directory) {
        $entry = Get-Item -LiteralPath $directory -Force
        if (-not $entry.PSIsContainer -or ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "Output must be an ordinary project directory: $directory"
        }
    }
}
$runName = [DateTime]::Now.ToString('yyyyMMdd_HHmmss_fff') + '_' + [Guid]::NewGuid().ToString('N')
$packageRoot = Join-Path $outputRoot $runName
if (Test-Path -LiteralPath $packageRoot) {
    throw "Refusing to overwrite an existing package: $packageRoot"
}
[void][System.IO.Directory]::CreateDirectory($packageRoot)

function Write-NewPackageFile([string]$RelativePath, [byte[]]$Bytes) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $packageRoot $RelativePath))
    $prefix = $packageRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output escaped the new package directory: $path"
    }
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path))
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    try {
        $stream.Write($Bytes)
    }
    finally {
        $stream.Dispose()
    }
}

$fileEvidence = @()
foreach ($variant in @('A-listed', 'B-unlisted')) {
    $manifestLines = @()
    foreach ($relativePath in $sharedFiles.Keys) {
        $bytes = $sharedFiles[$relativePath]
        $outputRelativePath = "$variant/$modFolder/$relativePath"
        Write-NewPackageFile -RelativePath $outputRelativePath -Bytes $bytes
        if ($variant -eq 'A-listed' -or $relativePath -ne 'heroes/highwayman/highwayman.info.darkest') {
            $manifestLines += "$relativePath $($bytes.Length)"
        }
        $fileEvidence += [pscustomobject]@{
            Path = $outputRelativePath
            Length = $bytes.Length
            Sha256 = Get-BytesSha256 $bytes
        }
    }
    $manifestBytes = $utf8.GetBytes(($manifestLines -join "`r`n") + "`r`n")
    $manifestRelativePath = "$variant/$modFolder/modfiles.txt"
    Write-NewPackageFile -RelativePath $manifestRelativePath -Bytes $manifestBytes
    $fileEvidence += [pscustomobject]@{
        Path = $manifestRelativePath
        Length = $manifestBytes.Length
        Sha256 = Get-BytesSha256 $manifestBytes
    }
}

foreach ($probe in $probes) {
    if ((Get-BytesSha256 ([System.IO.File]::ReadAllBytes($probe.SourcePath))) -ne $probe.SourceSha256) {
        throw "Source changed during generation; discard this package: $($probe.SourcePath)"
    }
}
foreach ($file in $fileEvidence) {
    if ((Get-FileHash -LiteralPath (Join-Path $packageRoot $file.Path) -Algorithm SHA256).Hash -ine $file.Sha256) {
        throw "Generated file failed its hash check: $($file.Path)"
    }
}

$guidePath = Join-Path $projectRoot 'docs/manifest-loading-probe.md'
Write-NewPackageFile -RelativePath 'README.zh-CN.md' -Bytes ([System.IO.File]::ReadAllBytes($guidePath))
$evidence = [ordered]@{
    SchemaVersion = 1
    GeneratedAt = [DateTimeOffset]::Now.ToString('o')
    PackageDirectory = $packageRoot
    GameDirectory = $gameRoot
    IntendedInstallDirectory = $intendedInstallPath
    ModTitle = $modTitle
    Scope = 'Local Mod; existing hero .info.darkest overrides; only weapon speed changed. Runtime behavior is not yet verified.'
    Difference = 'B omits only heroes/highwayman/highwayman.info.darkest from modfiles.txt; all other bytes match A.'
    SourcesUnchangedAfterGeneration = $true
    Heroes = @($probes | Select-Object HeroId, RelativePath, SourcePath, SourceSha256, ProbeSha256, SpeedBonus, Changes)
    Files = $fileEvidence
}
Write-NewPackageFile -RelativePath 'evidence.json' -Bytes ($utf8.GetBytes(($evidence | ConvertTo-Json -Depth 8) + "`r`n"))

[pscustomobject]@{
    PackageDirectory = $packageRoot
    Instructions = Join-Path $packageRoot 'README.zh-CN.md'
    A = Join-Path $packageRoot "A-listed/$modFolder"
    BManifest = Join-Path $packageRoot "B-unlisted/$modFolder/modfiles.txt"
    Installed = $false
    SourceFilesUnchanged = $true
}

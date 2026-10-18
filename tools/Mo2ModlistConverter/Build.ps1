#Requires -Version 7.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Build the Windows utility on Windows.' }
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $PSScriptRoot 'App/ModOrder.App.csproj'
$properties = [xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$') { throw 'Invalid package version.' }
$runId = [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $PSScriptRoot "artifacts/package-$runId"
if (Test-Path -LiteralPath $runDirectory) { throw 'Package output already exists.' }
$packageName = "DarkestDungeonModOrderConverter-$version-win-x64"
$package = Join-Path $runDirectory $packageName
[void][IO.Directory]::CreateDirectory($package)

& dotnet publish $project -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false' -m:1 -o $package
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

foreach ($file in @('DarkestDungeonModOrderConverter.exe', 'DarkestDungeonModOrderConverter.dll',
    'ModOrder.Core.dll', 'DarkestDungeonSaveEditor.Core.dll', 'coreclr.dll', 'PresentationFramework.dll',
    'LICENSE', 'NOTICE', 'README.md', 'licenses/NativeCodec/LICENSE')) {
    $path = Join-Path $package $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Missing package file: $file"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $package 'DarkestDungeonModOrderConverter.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
if ($runtime.runtimeOptions.ContainsKey('framework') -or $runtime.runtimeOptions.ContainsKey('frameworks') -or
    -not $runtime.runtimeOptions.ContainsKey('includedFrameworks')) { throw 'The package is not self-contained.' }
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $id = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
    $pack = $null
    foreach ($cache in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cache "$id/$($framework.version)"
        if (Test-Path -LiteralPath $candidate -PathType Container) { $pack = $candidate; break }
    }
    if ($null -eq $pack) { throw "Cannot find licenses for $id $($framework.version)." }
    $destination = Join-Path $package "licenses/$($framework.name)"
    [void][IO.Directory]::CreateDirectory($destination)
    $notices = @(Get-ChildItem -LiteralPath $pack -File | Where-Object {
        $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.(txt|md))?$'
    })
    if (@($notices | Where-Object { $_.Name -match '^LICENSE(\.(txt|md))?$' }).Count -eq 0) {
        throw "Missing runtime license: $pack"
    }
    if ($framework.name -eq 'Microsoft.NETCore.App' -and
        @($notices | Where-Object { $_.Name -match '^THIRD-PARTY-NOTICES\.' }).Count -eq 0) {
        throw "Missing runtime third-party notices: $pack"
    }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $destination $notice.Name)
    }
}
$zip = Join-Path $runDirectory "$packageName.zip"
Compress-Archive -LiteralPath $package -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"

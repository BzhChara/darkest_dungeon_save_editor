#Requires -Version 7.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Build the Windows desktop test on Windows.' }
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $projectRoot 'src/DarkestDungeonSaveEditor.App/DarkestDungeonSaveEditor.App.csproj'
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff') + '_' + [Guid]::NewGuid().ToString('N')
$outputRoot = Join-Path $projectRoot 'artifacts/native-desktop-builds'
foreach ($directory in @((Join-Path $projectRoot 'artifacts'), $outputRoot)) {
    if (Test-Path -LiteralPath $directory) {
        $entry = Get-Item -LiteralPath $directory -Force
        if (-not $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Test output must be an ordinary project directory: $directory"
        }
    }
}
$output = Join-Path $outputRoot $runId
if (Test-Path -LiteralPath $output) { throw "Refusing to overwrite $output" }
[void][IO.Directory]::CreateDirectory($output)
# This local test package uses the installed .NET 8 Desktop Runtime. The normal
# release package includes its own runtime. Both builds default to native C#.
& dotnet publish $appProject -c Release --self-contained false -p:NativeCodecTestBuild=true -m:1 -o $output
if ($LASTEXITCODE -ne 0) { throw "Native test publish failed: $LASTEXITCODE" }
$files = @('DarkestDungeonSaveEditor.App.exe', 'DarkestDungeonSaveEditor.App.dll',
    'DarkestDungeonSaveEditor.Core.dll', 'LICENSE', 'NOTICE', 'tools/DDSaveEditor/DDSaveEditor.jar',
    'tools/DDSaveEditor/LICENSE', 'tools/DDSaveEditor/THIRD-PARTY-NOTICES.md')
$hashes = [ordered]@{}
foreach ($relative in $files) {
    $path = Join-Path $output $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing test package file: $relative" }
    $hashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output 'Start-JarReference.cmd'), "@echo off`r`n`"%~dp0DarkestDungeonSaveEditor.App.exe`" --codec=java`r`n", $encoding)
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/dson-codec-migration.md') -Destination (Join-Path $output 'NATIVE-TEST.md')
$metadata = [ordered]@{ NativeCodecTestBuild = $true; DefaultCodec = 'native-csharp';
    JavaReferenceArgument = '--codec=java'; Requires = '.NET 8 Desktop Runtime';
    ValidationScope = 'This package needs validation; earlier sample results are documented in docs/dson-codec-migration.md'; Files = $hashes }
[IO.File]::WriteAllText((Join-Path $output 'native-build.json'), ($metadata | ConvertTo-Json -Depth 4), $encoding)
Write-Output "Native desktop test package: $output"

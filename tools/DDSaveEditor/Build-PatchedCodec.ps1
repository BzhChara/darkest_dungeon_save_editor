[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OriginalJar,
    [Parameter(Mandatory = $true)][string]$OutputJar,
    [string]$JdkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$originalPath = (Resolve-Path -LiteralPath $OriginalJar).Path
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputJar)
$expectedHash = 'FD7A052F5DA21FDE991893BC3D5AE9B03DBC59A9002B206388C3396199E10B90'
if ((Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'OriginalJar must be the unmodified upstream v0.0.70 JAR (see README.md).'
}
if (Test-Path -LiteralPath $outputPath) {
    throw 'OutputJar already exists. Choose a new output path; this script does not overwrite files.'
}
if ($JdkDirectory) {
    $javac = Join-Path $JdkDirectory 'bin/javac.exe'
} else {
    $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $javac)) { throw "javac was not found: $javac" }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$buildRoot = Join-Path $repositoryRoot ('artifacts/codec-build-' + [Guid]::NewGuid().ToString('N'))
$classesPath = Join-Path $buildRoot 'classes'
[IO.Directory]::CreateDirectory($classesPath) | Out-Null
$sourcePath = Join-Path $PSScriptRoot 'src/de/robojumper/ddsavereader/file/DsonFile.java'
& $javac --release 8 -encoding UTF-8 -cp $originalPath -d $classesPath $sourcePath
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }

# Replace this class and its nested classes only. Gson and the binary writer
# already live in the upstream JAR and must remain unchanged.
$packagePath = 'de/robojumper/ddsavereader/file/'
$compiledClasses = @(Get-ChildItem -LiteralPath (Join-Path $classesPath $packagePath) -Filter 'DsonFile*.class' | Sort-Object Name)
if ($compiledClasses.Count -eq 0) { throw 'No compiled DsonFile classes were found.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$originalArchive = [IO.Compression.ZipFile]::OpenRead($originalPath)
try {
    $originalClassNames = @($originalArchive.Entries | Where-Object {
        $_.FullName -match '^de/robojumper/ddsavereader/file/DsonFile(?:\$[^/]+)?\.class$'
    } | ForEach-Object { $_.Name } | Sort-Object)
    if (Compare-Object $originalClassNames @($compiledClasses.Name)) {
        throw 'Compiled class names differ from the upstream DsonFile class set.'
    }
    foreach ($classFile in $compiledClasses) {
        $bytes = [IO.File]::ReadAllBytes($classFile.FullName)
        if ($bytes.Length -lt 8 -or [BitConverter]::ToString($bytes, 0, 4) -ne 'CA-FE-BA-BE' -or
            $bytes[6] -ne 0 -or $bytes[7] -ne 52) {
            throw "Expected a Java 8 class file: $($classFile.Name)"
        }
    }
} finally { $originalArchive.Dispose() }

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[IO.File]::Copy($originalPath, $outputPath, $false)
$archive = [IO.Compression.ZipFile]::Open($outputPath, [IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($classFile in $compiledClasses) {
        $entryName = $packagePath + $classFile.Name
        $oldEntry = $archive.GetEntry($entryName)
        $timestamp = $oldEntry.LastWriteTime
        $oldEntry.Delete()
        $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $timestamp
        $stream = $entry.Open()
        try {
            $bytes = [IO.File]::ReadAllBytes($classFile.FullName)
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }

Write-Output "Patched codec: $outputPath"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash)"
Write-Output "Build artifacts: $buildRoot"

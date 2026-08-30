[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $projectRoot "DarkestDungeonSaveEditor.sln"
$testProject = Join-Path $projectRoot "tests\DarkestDungeonSaveEditor.ContractTests\DarkestDungeonSaveEditor.ContractTests.csproj"

dotnet build $solutionPath -c Release -m:1
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE"
}

dotnet run --project $testProject -c Release --no-build -- $projectRoot
if ($LASTEXITCODE -ne 0) {
    throw "Contract test failed with exit code $LASTEXITCODE"
}

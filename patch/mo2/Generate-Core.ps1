param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText($Source).Replace("`r`n", "`n")
$anchor = @'
        foreach (var entry in appliedEntries)
        {
'@
if (($text.Split([string[]]@($anchor), [StringSplitOptions]::None)).Length -ne 2) {
    throw 'The upstream resolver changed. Review the MO2 insertion point before rebuilding.'
}
$insertion = @'
        // Present the MO2 virtual merge as one saved Mod; retain native ordering and guards.
        Mo2Compatibility.Apply(baseRoot, gameDirectory, normalizedWorkshopDirectory,
            localSources, cancellationToken);

'@
$text = $text.Replace($anchor, $insertion + "`n" + $anchor)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
if (!(Test-Path -LiteralPath $Destination) -or [IO.File]::ReadAllText($Destination) -ne $text) {
    [IO.File]::WriteAllText($Destination, $text, [Text.UTF8Encoding]::new($false))
}

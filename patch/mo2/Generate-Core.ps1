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
            localSources, cancellationToken, validateBindingsOnly);

'@
$text = $text.Replace($anchor, $insertion + "`n" + $anchor)
$signature = @'
        string gameDirectory, string? workshopDirectory, string? normalizedAdditionalLocalModDirectory,
        CancellationToken cancellationToken)
'@
$validation = @'
        var current = ResolveSources(configuration, resolution.GameDirectory, resolution.WorkshopDirectory,
            resolution.AdditionalLocalModDirectory, cancellationToken);
'@
foreach ($required in @($signature, $validation)) {
    if (($text.Split([string[]]@($required), [StringSplitOptions]::None)).Length -ne 2) {
        throw 'The upstream source-binding validator changed. Review the MO2 validation hook before rebuilding.'
    }
}
$text = $text.Replace($signature, $signature.Replace('CancellationToken cancellationToken)',
    'CancellationToken cancellationToken, bool validateBindingsOnly = false)'))
$text = $text.Replace($validation, $validation.Replace('cancellationToken);',
    'cancellationToken, validateBindingsOnly: true);'))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
if (!(Test-Path -LiteralPath $Destination) -or [IO.File]::ReadAllText($Destination) -ne $text) {
    [IO.File]::WriteAllText($Destination, $text, [Text.UTF8Encoding]::new($false))
}

<#
.SYNOPSIS
    Writes the locale catalogue's key count into the documents that quote it.

.DESCRIPTION
    LocaleCatalogueParityTests.TheDocumentedKeyCountMatchesTheCatalogue fails as soon as a key
    is added or removed, and six documents carry the number, three in English and three in
    French. Fixing them by hand on every pull request that touches a catalogue was a chore that
    went wrong in its own ways; this script does it.

    The count is the number of keys in locales/en.json. In each document the number is found
    as a thousands-grouped figure followed by "keys" or "cles" (accented), with an optional
    "i18n" between them, so the other figures of the same pages are left alone. A document
    that holds no such figure, or more than one, is reported and left unchanged, and the run
    exits 1.

.PARAMETER Check
    Reports what would change without writing anything. Exits 1 when a document is out of date.

.PARAMETER Count
    Writes this count instead of the catalogue's. For testing the script.

.EXAMPLE
    ./scripts/Update-DocumentedKeyCount.ps1

.NOTES
    Copyright 2026 Julien Bombled
    Licensed under the Apache License, Version 2.0

    Keep the document list in step with DocumentsStatingTheKeyCount in
    tests/Heimdall.Core.Tests/LocaleCatalogueParityTests.cs.
    Kept compatible with Windows PowerShell 5.1.
#>

[CmdletBinding()]
param(
    [switch] $Check,
    [int] $Count = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# The same list, and the same separators, as the test that guards the count.
$documents = @(
    @{ Path = 'docs/FEATURES.md'; Separator = ',' },
    @{ Path = 'docs/SECURITY.md'; Separator = ',' },
    @{ Path = 'docs/DEVELOPMENT.md'; Separator = ',' },
    @{ Path = 'docs/fr/FEATURES.md'; Separator = ' ' },
    @{ Path = 'docs/fr/SECURITY.md'; Separator = ' ' },
    @{ Path = 'docs/fr/DEVELOPMENT.md'; Separator = ' ' }
)

if ($Count -le 0) {
    $catalogue = Get-Content -LiteralPath (Join-Path $repoRoot 'locales/en.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $Count = @($catalogue.PSObject.Properties).Count
}

$grouped = $Count.ToString('#,0', [System.Globalization.CultureInfo]::InvariantCulture)
# "cles" with its accent, written as a code point so this file stays ASCII.
$frenchKeys = 'cl' + [char]0x00E9 + 's'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$problems = 0
$outdated = 0

foreach ($document in $documents) {
    $path = Join-Path $repoRoot $document.Path
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = $utf8.GetString($bytes).TrimStart([char]0xFEFF)

    $separator = [regex]::Escape($document.Separator)
    $pattern = '\d{1,3}(?:' + $separator + '\d{3})+(?=\s+(?:i18n\s+)?(?:keys|' + $frenchKeys + '))'
    $found = [regex]::Matches($text, $pattern)
    if ($found.Count -ne 1) {
        Write-Host "SKIP $($document.Path): $($found.Count) key count(s) found, expected exactly one." -ForegroundColor Red
        $problems++
        continue
    }

    $wanted = $grouped.Replace(',', $document.Separator)
    if ($found[0].Value -eq $wanted) {
        Write-Host "ok   $($document.Path): $wanted" -ForegroundColor DarkGray
        continue
    }

    $outdated++
    if ($Check) {
        Write-Host "OUT  $($document.Path): $($found[0].Value), the catalogue holds $wanted" -ForegroundColor Yellow
        continue
    }

    $updated = $text.Substring(0, $found[0].Index) + $wanted + $text.Substring($found[0].Index + $found[0].Length)
    [System.IO.File]::WriteAllText($path, $updated, (New-Object System.Text.UTF8Encoding($hasBom)))
    Write-Host "SET  $($document.Path): $($found[0].Value) -> $wanted" -ForegroundColor Green
}

if ($problems -gt 0 -or ($Check -and $outdated -gt 0)) {
    exit 1
}

exit 0

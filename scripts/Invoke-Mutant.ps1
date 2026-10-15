<#
.SYNOPSIS
    Runs one mutant end to end: apply, build, test, restore, rebuild, retest.

.DESCRIPTION
    CONTRIBUTING asks for a test that fails without the change, shown by breaking the fix on
    purpose. Every step of doing that by hand went wrong at least once, so each is enforced:

      - the mutant must land: the text to replace occurs exactly once (line endings
        normalised), otherwise nothing is touched and the run fails;
      - the mutant must build: a build error is a failed run, never a verdict;
      - the filter must run at least one test, or the red proves nothing;
      - the file is restored byte for byte from a copy and touched, so the incremental
        build cannot keep the mutant DLL, and the project is REBUILT whatever happened:
        a restored-but-not-rebuilt binary handed to a person for a hand test measures the
        mutant, not the fix;
      - the verdict needs both directions: red with the mutant AND green after the clean
        rebuild. Anything else exits non-zero.

.PARAMETER File
    The production file to mutate.

.PARAMETER Find
    The exact text to replace. Must occur exactly once.

.PARAMETER Replace
    The mutant text. May be empty.

.PARAMETER Project
    The test project to build and run.

.PARAMETER Filter
    The dotnet test filter that must go red with the mutant.

.EXAMPLE
    ./scripts/Invoke-Mutant.ps1 -File src/Heimdall.App/Services/TabDropDecision.cs `
        -Find '&& (!releasedInsideWindow || releasedOverSessionContent)' -Replace '' `
        -Project tests/Heimdall.App.Tests/Heimdall.App.Tests.csproj `
        -Filter 'FullyQualifiedName~ShouldDetach'

.NOTES
    Copyright 2026 Julien Bombled
    Licensed under the Apache License, Version 2.0

    Kept compatible with Windows PowerShell 5.1, like Build.ps1.
    Exit codes: 0 killed and clean green; 1 survived or clean red; 2 not landed;
    3 mutant did not build, filter ran nothing, or the restore or clean rebuild failed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $File,
    [Parameter(Mandatory)] [string] $Find,
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $Replace,
    [Parameter(Mandatory)] [string] $Project,
    [Parameter(Mandatory)] [string] $Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Native commands write to stderr; under 'Stop' Windows PowerShell would throw on the first line.
function Invoke-Quiet {
    param([scriptblock] $Command)

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $Command 2>&1 | ForEach-Object { "$_" })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
    } finally {
        $ErrorActionPreference = $prev
    }
}

function Invoke-Build {
    $run = Invoke-Quiet { dotnet build $Project -c Debug --nologo -v q }
    $errors = @($run.Lines | Where-Object { $_ -match ' error ' })
    return [pscustomobject]@{ Ok = ($run.ExitCode -eq 0 -and $errors.Count -eq 0); Errors = $errors }
}

function Invoke-Filter {
    $run = Invoke-Quiet { dotnet test $Project --no-build --nologo --filter $Filter }
    $summary = @($run.Lines | Where-Object { $_ -match 'total\s*:|Total tests' })
    $ran = ($run.Lines -join "`n") -match '(total|Total tests)\s*:\s*[1-9]'
    return [pscustomobject]@{ Green = ($run.ExitCode -eq 0); Ran = $ran; Summary = ($summary -join ' ') }
}

$path = (Resolve-Path -LiteralPath $File).Path
$original = [System.IO.File]::ReadAllBytes($path)
$hasBom = $original.Length -ge 3 -and $original[0] -eq 0xEF -and $original[1] -eq 0xBB -and $original[2] -eq 0xBF
$text = (New-Object System.Text.UTF8Encoding($false)).GetString($original).TrimStart([char]0xFEFF)
$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$needle = $Find.Replace("`r`n", "`n").Replace("`n", $newline)
$occurrences = ([regex]::Matches($text, [regex]::Escape($needle))).Count
if ($occurrences -ne 1) {
    Write-Host "NOT LANDED: the text occurs $occurrences time(s) in $File; nothing was changed." -ForegroundColor Red
    exit 2
}

$backup = Join-Path ([System.IO.Path]::GetTempPath()) ("mutant-" + [Guid]::NewGuid().ToString('N') + '-' + (Split-Path $path -Leaf))
[System.IO.File]::WriteAllBytes($backup, $original)

$killed = $false
$failure = $null
$restored = $false
$clean = $null
try {
    $mutated = $text.Replace($needle, $Replace.Replace("`r`n", "`n").Replace("`n", $newline))
    [System.IO.File]::WriteAllText($path, $mutated, (New-Object System.Text.UTF8Encoding($hasBom)))
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    Write-Host "LANDED in $File" -ForegroundColor DarkGray

    $build = Invoke-Build
    if (-not $build.Ok) {
        $failure = 'MUTANT DID NOT BUILD: no verdict.'
        $build.Errors | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
    } else {
        $mutantRun = Invoke-Filter
        if (-not $mutantRun.Ran) {
            $failure = "MUTANT RUN EXECUTED NO TEST: the filter matches nothing. $($mutantRun.Summary)"
        } else {
            $killed = -not $mutantRun.Green
            Write-Host "Mutant: $(if ($killed) { 'RED (killed)' } else { 'GREEN (survived)' }) - $($mutantRun.Summary)"
        }
    }
} finally {
    [System.IO.File]::WriteAllBytes($path, [System.IO.File]::ReadAllBytes($backup))
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    Remove-Item -LiteralPath $backup -Force
    $restored = [System.Linq.Enumerable]::SequenceEqual([byte[]][System.IO.File]::ReadAllBytes($path), [byte[]]$original)
    Write-Host "Restored byte for byte: $restored" -ForegroundColor DarkGray

    # Whatever happened above, the binaries on disk must not stay the mutant's.
    $clean = Invoke-Build
    if (-not $clean.Ok) {
        Write-Host 'CLEAN REBUILD FAILED: the binaries may still be the mutant.' -ForegroundColor Red
        $clean.Errors | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host 'Clean rebuild done.' -ForegroundColor DarkGray
    }
}

if ($null -ne $failure) {
    Write-Host $failure -ForegroundColor Red
    exit 3
}

if (-not $restored -or -not $clean.Ok) {
    exit 3
}

$cleanRun = Invoke-Filter
Write-Host "Clean:  $(if ($cleanRun.Green) { 'GREEN' } else { 'RED' }) - $($cleanRun.Summary)"

if ($killed -and $cleanRun.Green) {
    Write-Host 'VERDICT: the tests kill the mutant and pass on the clean build.' -ForegroundColor Green
    exit 0
}

if (-not $killed) {
    Write-Host 'VERDICT: the mutant survived; the tests do not catch this change.' -ForegroundColor Yellow
} else {
    Write-Host 'VERDICT: the clean build is red too; the red proves nothing about the mutant.' -ForegroundColor Yellow
}

exit 1

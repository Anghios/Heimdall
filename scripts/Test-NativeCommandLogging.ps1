<#
.SYNOPSIS
    Tests that the build's test step survives a failing run and keeps its output
    (Build.ps1: Invoke-NativeLogged, Select-TestFailureLines).

.DESCRIPTION
    Build.ps1 runs under $ErrorActionPreference = 'Stop'. Windows PowerShell turns the
    first stderr line of a native command redirected with 2>&1 into a terminating
    error, so the test step died on xUnit's first [FAIL] line, before it printed the
    failure. A red gate named a test and never said why.

    The suite proves both directions, because a guard that only ever passes proves
    nothing:

      1. The naive form, under Windows PowerShell 5.1 and 'Stop', throws on a
         command that writes to stderr        -> the hazard is real.
      2. Invoke-NativeLogged, under 'Stop', returns the exit code and every line,
         stdout and stderr, and writes them to its log.
      3. Select-TestFailureLines keeps a failing test, its assertion message and
         the expected and actual values, and stops at the stack trace.

    SCOPE. This suite covers the two helpers, not the build: a green run does not
    prove that the test step calls them.

.NOTES
    Copyright 2026 Julien Bombled
    Licensed under the Apache License, Version 2.0

    Kept compatible with Windows PowerShell 5.1, like Build.ps1 itself.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $repoRoot 'Build.ps1'

if (-not (Test-Path -LiteralPath $buildScript)) {
    throw "Build.ps1 not found next to this suite: $buildScript"
}

# Build.ps1 runs its whole pipeline when dot-sourced, so the helpers are lifted out of
# it by parsing the file and re-declaring only those functions.
$ast = [System.Management.Automation.Language.Parser]::ParseFile($buildScript, [ref]$null, [ref]$null)
foreach ($name in @('Invoke-NativeLogged', 'Select-TestFailureLines')) {
    $fn = $ast.FindAll(
        { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name },
        $true)
    if ($fn.Count -ne 1) {
        throw "Expected exactly one '$name' in Build.ps1, found $($fn.Count). The suite cannot test what it cannot find."
    }
    . ([scriptblock]::Create($fn[0].Extent.Text))
}

$failures = New-Object System.Collections.Generic.List[string]
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ("heimdall-nativelog-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

# A native command that writes one line to stdout, one to stderr, and fails.
$noisyFailure = { cmd.exe /d /c "echo out-line& echo err-line 1>&2& exit /b 3" }

try {
    # 1. The hazard, measured in the shell Build.ps1 runs in locally.
    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -eq $windowsPowerShell) {
        Write-Host "SKIP 1: Windows PowerShell is not available here." -ForegroundColor DarkYellow
    } else {
        $naive = '$ErrorActionPreference = ''Stop''; $r = cmd.exe /d /c "echo out-line& echo err-line 1>&2& exit /b 3" 2>&1; "reached"'
        # Read with 'Continue' here: under 'Stop' this very call would hit the hazard it measures,
        # since the child's error arrives on its stderr.
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $naiveOutput = & $windowsPowerShell.Source -NoProfile -NonInteractive -Command $naive 2>&1 | ForEach-Object { "$_" }
        } finally {
            $ErrorActionPreference = $prevEAP
        }
        if (($naiveOutput -join "`n") -match 'reached') {
            $failures.Add("1: the naive form did not throw under 'Stop'; the hazard this suite guards is not reproduced.")
        } else {
            Write-Host "PASS 1: the naive form throws before its next statement." -ForegroundColor Green
        }
    }

    # 2. The helper survives the same command under 'Stop' and keeps everything.
    $log = Join-Path $sandbox 'logs\tests.log'
    $run = Invoke-NativeLogged -LogPath $log -Command $noisyFailure
    if ($run.ExitCode -ne 3) {
        $failures.Add("2: expected exit code 3, got $($run.ExitCode).")
    }
    $joined = $run.Lines -join "`n"
    if ($joined -notmatch 'out-line' -or $joined -notmatch 'err-line') {
        $failures.Add("2: the returned lines lost stdout or stderr: $joined")
    }
    if (-not (Test-Path -LiteralPath $log)) {
        $failures.Add("2: no log written at $log.")
    } else {
        $logged = Get-Content -LiteralPath $log -Raw
        if ($logged -notmatch 'out-line' -or $logged -notmatch 'err-line') {
            $failures.Add("2: the log lost stdout or stderr: $logged")
        }
    }
    if ($failures.Count -eq 0) {
        Write-Host "PASS 2: exit code and every line kept, on screen and in the log." -ForegroundColor Green
    }

    # 3. The on-screen summary keeps a failure and its message, drops the noise.
    $sample = @(
        'Starting test execution, please wait...',
        '[xUnit.net 00:00:02.14]     Heimdall.Sftp.Tests.FtpBrowserAtomicUploadTests.Disconnect_WaitsForOperationLock [FAIL]',
        '  Failed Heimdall.Sftp.Tests.FtpBrowserAtomicUploadTests.Disconnect_WaitsForOperationLock [5 ms]',
        '  Error Message:',
        '   Assert.Equal() Failure: Strings differ',
        'Expected: "gate-probe-expected"',
        'Actual:   "gate-probe-actual"',
        '  Stack Trace:',
        '     at Heimdall.Sftp.Tests.FtpBrowserAtomicUploadTests.Disconnect_WaitsForOperationLock()',
        'Passed!  - Failed:     0, Passed:   576'
    )
    $summary = Select-TestFailureLines -Lines $sample
    $summaryText = $summary -join "`n"
    if ($summaryText -notmatch '\[FAIL\]' -or $summaryText -notmatch 'Assert\.Equal' -or $summaryText -notmatch 'gate-probe-actual') {
        $failures.Add("3: the summary dropped the failure, its message or its values: $summaryText")
    } elseif ($summaryText -match 'Passed!|Stack Trace|at Heimdall') {
        $failures.Add("3: the summary kept unrelated lines: $summaryText")
    } else {
        Write-Host "PASS 3: the summary shows the failing test and its assertion." -ForegroundColor Green
    }
} finally {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Native command logging: all checks passed." -ForegroundColor Green
exit 0

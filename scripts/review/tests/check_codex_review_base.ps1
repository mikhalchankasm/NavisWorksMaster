# Exercises the -Base resolution block of ../codex-review.ps1 (#292).
#
# Windows PowerShell 5.1 turned the failing remote-tracking lookup for
# `-Base origin/main` into a terminating error, so reviewers fell back to a stale
# local `main` and got already-merged diffs in the bundle. CI runs this file under
# both `powershell` (5.1) and `pwsh`.
#
# As in check_codex_review_writeback.ps1, the block is EXTRACTED FROM THE SHIPPED
# FILE between its markers, and runs against a throwaway repository with a bare
# `origin`. Needs no Codex, no network and no credential.

$ErrorActionPreference = 'Stop'
$wrapper = Join-Path $PSScriptRoot '..\codex-review.ps1'
$lines = Get-Content -LiteralPath $wrapper
$start = ($lines | Select-String -Pattern '# --- base resolution (begin) ---' -SimpleMatch | Select-Object -First 1).LineNumber
$end = ($lines | Select-String -Pattern '# --- base resolution (end) ---' -SimpleMatch | Select-Object -First 1).LineNumber
if (-not $start -or -not $end -or $end -le $start) {
    throw "Could not locate the base resolution block (start=$start end=$end). Re-point this test."
}
$block = [ScriptBlock]::Create(($lines[$start..($end - 2)] -join "`n"))

$root = Join-Path ([IO.Path]::GetTempPath()) ('reviewbase-' + [Guid]::NewGuid().ToString('N'))
$failures = 0

function Invoke-Git {
    # The fixture's own git chatter on stderr must not trip 5.1 the way the bug did.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git @args 2>&1 | Out-Null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw ('git ' + ($args -join ' ') + ' exited ' + $LASTEXITCODE) }
}

function Resolve-Base([string]$Base) {
    # Dot-sourced so the block's assignments land in this scope, as in the wrapper.
    $baseSha = $null
    $remoteSha = $null
    $warnings = @(. $block 3>&1 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
    return @{ Base = $baseSha; Warnings = @($warnings | ForEach-Object { $_.Message }) }
}

function Check([string]$name, [scriptblock]$test) {
    try {
        & $test
        Write-Output ('PASS ' + $name)
    } catch {
        $script:failures++
        Write-Output ('FAIL ' + $name + ': ' + $_.Exception.Message)
    }
}

$origin = Join-Path $root 'origin.git'
$work = Join-Path $root 'work'
$here = Get-Location
try {
    [void](New-Item -ItemType Directory -Path $work -Force)
    Invoke-Git init -q --bare -b main $origin
    Set-Location -LiteralPath $work
    Invoke-Git init -q -b main
    Invoke-Git config user.email test@example.invalid
    Invoke-Git config user.name test
    Invoke-Git remote add origin $origin
    Set-Content -LiteralPath 'a.txt' -Value 'one'
    Invoke-Git add a.txt
    Invoke-Git commit -q -m one
    $first = (& git rev-parse --short HEAD)
    Set-Content -LiteralPath 'a.txt' -Value 'two'
    Invoke-Git commit -q -am two
    $second = (& git rev-parse --short HEAD)
    Invoke-Git push -q origin main
    # Local main falls one commit behind origin/main, as in a stale worktree checkout.
    Invoke-Git reset -q --hard $first

    Check '-Base origin/main resolves to the remote tip without a warning' {
        $result = Resolve-Base 'origin/main'
        if ($result.Base -ne $second) { throw ('base ' + $result.Base + ', expected ' + $second) }
        if ($result.Warnings.Count -ne 0) { throw ('unexpected warning: ' + $result.Warnings[0]) }
    }
    Check '-Base <SHA> resolves without a warning' {
        $full = (& git rev-parse $second)
        $result = Resolve-Base $full
        if ($result.Base -ne $second) { throw ('base ' + $result.Base + ', expected ' + $second) }
        if ($result.Warnings.Count -ne 0) { throw ('unexpected warning: ' + $result.Warnings[0]) }
    }
    Check '-Base main behind origin/main warns and names both commits' {
        $result = Resolve-Base 'main'
        if ($result.Base -ne $first) { throw ('base ' + $result.Base + ', expected ' + $first) }
        if ($result.Warnings.Count -ne 1) { throw ('expected one warning, got ' + $result.Warnings.Count) }
        foreach ($expected in @($first, $second, 'pass -Base origin/main')) {
            if ($result.Warnings[0] -notlike ('*' + $expected + '*')) { throw ('warning lacks ' + $expected + ': ' + $result.Warnings[0]) }
        }
    }
    Check 'a missing base fails with a clear message' {
        $message = $null
        try { [void](Resolve-Base 'no-such-branch') } catch { $message = $_.Exception.Message }
        if ($message -ne "Base 'no-such-branch' does not resolve to a commit in this repository.") {
            throw ('message was: ' + $message)
        }
    }
} finally {
    Set-Location -LiteralPath $here
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output ('PowerShell ' + $PSVersionTable.PSVersion + ': ' + $failures + ' failure(s)')
# Explicit: CI dot-sources this file and then exits with $LASTEXITCODE, which the
# deliberately failing `rev-parse` of the missing-base case left at 1.
if ($failures -ne 0) { exit 1 }
exit 0

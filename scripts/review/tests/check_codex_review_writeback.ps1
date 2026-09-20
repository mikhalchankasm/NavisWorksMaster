# Exercises the credential write-back block of ../codex-review.ps1.
#
# Why a test and not a note: the wrapper copies a real Codex credential into a
# disposable home, and a refresh inside that home CONSUMES the old refresh token.
# Getting the write-back wrong logs the operator out on their *next* run, with the
# credential file looking untouched -- a failure both delayed and misleading, which no
# reviewer would catch by reading.
#
# A real rotation cannot be forced on demand, so the branches are driven with
# fixtures. The block under test is EXTRACTED FROM THE SHIPPED FILE between its two
# markers rather than retyped here, so this exercises the text that actually runs.
#
# Needs no Codex, no network and no credential of its own.
#
# Named `check_` rather than `test_` on purpose. In this repository `test_*.ps1`
# under scripts/ means a live installer test, and check_agent_docs.py requires
# every such name to carry deny rules in .claude/settings.json -- about eight
# entries per script. Applying that here would have told a future reader this
# fixture-only guard can touch a live system, which it cannot. Worth noting that
# the classifier decides from the file NAME, so it is a text match rather than a
# resolution; that is recorded, not fixed here.

$ErrorActionPreference = 'Stop'
$wrapper = Join-Path $PSScriptRoot '..\codex-review.ps1'
if (-not (Test-Path -LiteralPath $wrapper)) {
    throw "Expected the wrapper next door at $wrapper"
}
$lines = Get-Content -LiteralPath $wrapper
$start = ($lines | Select-String -Pattern '# --- credential write-back (begin) ---' -SimpleMatch | Select-Object -First 1).LineNumber
$end = ($lines | Select-String -Pattern '# --- credential write-back (end) ---' -SimpleMatch | Select-Object -First 1).LineNumber
if (-not $start -or -not $end -or $end -le $start) {
    throw "Could not locate the write-back block (start=$start end=$end). Re-point this test."
}
$blockText = ($lines[($start - 1)..($end - 2)] -join "`n")
if ($blockText -notmatch 'Copy-Item -LiteralPath \$runAuth -Destination \$operatorAuth') {
    throw 'The extracted block does not contain the write-back itself; the anchors are wrong.'
}
$block = [ScriptBlock]::Create($blockText)

$root = Join-Path ([IO.Path]::GetTempPath()) ('authwriteback-' + [Guid]::NewGuid().ToString('N'))
$failures = 0

function New-Case([string]$name, [AllowNull()][object]$afterContent) {
    $dir = Join-Path $script:root $name
    [void](New-Item -ItemType Directory -Path $dir -Force)
    $operator = Join-Path $dir 'operator-auth.json'
    $run = Join-Path $dir 'auth.json'
    $original = '{"OPENAI_API_KEY":null,"tokens":{"id_token":"a","access_token":"b","refresh_token":"OLD"},"last_refresh":"2026-09-20T00:00:00Z"}'
    [IO.File]::WriteAllText($operator, $original)
    [IO.File]::WriteAllText($run, $original)
    $hashBefore = (Get-FileHash -LiteralPath $run -Algorithm SHA256).Hash
    $operatorHash = (Get-FileHash -LiteralPath $operator -Algorithm SHA256).Hash
    $keysBefore = @((Get-Content -LiteralPath $run -Raw | ConvertFrom-Json).PSObject.Properties.Name | Sort-Object)
    # $null means leave the copy untouched. Guard on emptiness too, because a
    # [string]-typed parameter would have turned $null into '' and written an
    # empty credential -- which the wrapper then correctly refused, making the
    # unchanged case look like a failure.
    if ($null -ne $afterContent -and "$afterContent" -ne '') {
        [IO.File]::WriteAllText($run, [string]$afterContent)
    }
    return @{ Operator = $operator; Run = $run; HashBefore = $hashBefore; KeysBefore = $keysBefore; OperatorHash = $operatorHash }
}

function Invoke-Block($case) {
    $runAuth = $case.Run
    $operatorAuth = $case.Operator
    $authHashBefore = $case.HashBefore
    $authKeysBefore = $case.KeysBefore
    $operatorHashBefore = $case.OperatorHash
    $keepRunHome = $false
    $warnings = @()
    # Dot-source, not call: `& $block` runs in a CHILD scope, so the block's
    # assignment to $keepRunHome would land on a copy and every refusal case would
    # look like a wrapper bug. In the wrapper itself the assignment and the finally
    # block share one scope, which is what this reproduces.
    #
    # Warnings are collected by redirecting stream 3 into the pipeline. A scriptblock
    # is not a cmdlet, so `-WarningVariable` would have been passed to it as a plain
    # argument and quietly ignored -- an earlier version of this test did that and
    # then asserted the warning had been raised.
    . $block 3>&1 4>$null | ForEach-Object {
        if ($_ -is [System.Management.Automation.WarningRecord]) { $warnings += $_.Message }
    }
    return @{ Kept = $keepRunHome; Warnings = $warnings; Operator = (Get-Content -LiteralPath $operatorAuth -Raw) }
}

function Assert([string]$label, [bool]$condition, [string]$detail) {
    if ($condition) {
        Write-Host ("  PASS  " + $label)
    } else {
        Write-Host ("  FAIL  " + $label + " -- " + $detail) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host 'case 1: rotated, valid, same shape -> carried back'
$rotated = '{"OPENAI_API_KEY":null,"tokens":{"id_token":"a","access_token":"b2","refresh_token":"NEW"},"last_refresh":"2026-09-20T18:00:00Z"}'
$c1 = New-Case 'rotated' $rotated
$r1 = Invoke-Block $c1
Assert 'operator file now holds the new refresh token' ($r1.Operator -match 'NEW') $r1.Operator
Assert 'run home is not kept' (-not $r1.Kept) 'keepRunHome was set'
Assert 'no warning raised' ($r1.Warnings.Count -eq 0) ($r1.Warnings -join '; ')

Write-Host 'case 2: rotated but unparseable -> refused, home kept, warned'
$c2 = New-Case 'garbage' '{"tokens": tru'
$r2 = Invoke-Block $c2
Assert 'operator file still holds the old token' ($r2.Operator -match 'OLD') $r2.Operator
Assert 'run home is kept for inspection' ([bool]$r2.Kept) 'keepRunHome was not set'
Assert 'a warning was raised' ($r2.Warnings.Count -ge 1) 'no warning'

Write-Host 'case 3: rotated, parses, but a key vanished -> refused'
$c3 = New-Case 'shapechange' '{"tokens":{"refresh_token":"NEW"}}'
$r3 = Invoke-Block $c3
Assert 'operator file still holds the old token' ($r3.Operator -match 'OLD') $r3.Operator
Assert 'run home is kept for inspection' ([bool]$r3.Kept) 'keepRunHome was not set'

Write-Host 'case 5: operator signed in again mid-review -> our older copy is refused'
$c5 = New-Case 'concurrent-login' $rotated
# Simulate the operator (or another Codex) writing the real store while we ran.
[IO.File]::WriteAllText($c5.Operator, '{"OPENAI_API_KEY":null,"tokens":{"id_token":"z","access_token":"z","refresh_token":"NEWER-FROM-LOGIN"},"last_refresh":"2026-09-20T19:00:00Z"}')
$r5 = Invoke-Block $c5
Assert 'the newer login survives' ($r5.Operator -match 'NEWER-FROM-LOGIN') $r5.Operator
Assert 'run home is kept for inspection' ([bool]$r5.Kept) 'keepRunHome was not set'
Assert 'the warning names the reason' (($r5.Warnings -join ' ') -match 'changed during the review') ($r5.Warnings -join '; ')

Write-Host 'case 4: unchanged -> nothing written, nothing kept'
$c4 = New-Case 'unchanged' $null
$before4 = (Get-FileHash -LiteralPath $c4.Operator -Algorithm SHA256).Hash
$r4 = Invoke-Block $c4
$after4 = (Get-FileHash -LiteralPath $c4.Operator -Algorithm SHA256).Hash
Assert 'operator file untouched' ($before4 -eq $after4) 'hash changed'
Assert 'run home is not kept' (-not $r4.Kept) 'keepRunHome was set'

Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($failures -gt 0) {
    Write-Host ("$failures assertion(s) failed") -ForegroundColor Red
    exit 1
}
Write-Host 'all assertions passed'

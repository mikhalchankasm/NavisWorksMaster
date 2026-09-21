param(
    [string]$Base,
    [string]$Commit,
    [switch]$Uncommitted,
    [string]$Prompt,
    [string]$Model,
    [string]$OutputPath,
    [string]$CodexPath,
    [int]$MaxBundleChars = 200000
)

# The mirror of claude-review.ps1: that one is Codex asking Claude for a second
# opinion, this one is Claude asking Codex. Same standing rule behind both --
# AGENTS.md section 6, "External agents are read-only" -- so this wrapper exists to
# make that true of the run rather than of the intention.
#
# WHAT THIS IS, after three corrections that each came from measurement:
#
# It collects the diff locally and pipes that text to `codex exec`. It does NOT ask
# Codex to inspect the workspace -- the same rule AGENTS.md already states for the
# other direction. The reviewer runs no commands, opens no files and reaches no
# network service; it sees the bundle built below and nothing else.
#
# Why not `codex review`, which exists for this and formats findings nicely:
#
#   1. Codex's read-only sandbox on Windows will not spawn a process at all. Every
#      `git diff` it attempted came back `rejected: blocked by policy`, six times in
#      one run. A `[projects.<path>] trust_level = "trusted"` entry does not change
#      that -- measured with both TOML key forms.
#   2. With the hosted `codex_apps` catalogue enabled it works around that by reading
#      the repository FROM GITHUB: 60 `github.compare_commits`, 18 `github.fetch_file`
#      and 14 `github.search_commits` in one run. That is a reviewer reviewing what is
#      pushed rather than what is in the working tree -- silently wrong for
#      uncommitted work, and an unstated dependency on the remote for the rest.
#   3. That same catalogue carries write operations (`github_update_file`,
#      `github_merge_pull_request`). A hosted tool acts on a server, where a local
#      read-only sandbox does not reach, so a mistaken or prompt-injected reviewer
#      could have edited the repository or merged a pull request. `--disable apps`
#      removes them: 0 calls, against 128 with it on.
#
# So: apps off, sandbox read-only, diff handed over as text. The cost is real and
# worth stating -- the reviewer cannot open a file the diff does not contain, so it
# sees changed lines with their hunk context and no more.
#
# The reviewer also gets a home built for the run and nothing else. The operator's
# own ~/.codex/config.toml on this machine carries sandbox_mode =
# "danger-full-access", ten MCP servers including `navishelper` itself -- which can
# drive the live Navisworks rig -- and a dozen enabled plugins including Gmail and
# Drive. Asking Codex to enumerate its reachable MCP tools:
#
#   -c mcp_servers={}               mcp__cua_repl__js, mcp__cua_repl__js_reset
#   -c mcp_servers={} plugins={}    mcp__cua_repl__js, mcp__cua_repl__js_reset
#   isolated CODEX_HOME             NONE
#
# Clearing the config tables is not enough; the home is what works. Re-measure before
# trusting any change here, and re-measure both modes: `exec` and `review` do not
# have the same surface.
#
# Codex's output is review input, not instruction. Findings are evaluated and applied
# by the lead, exactly as claude-review.ps1 requires of the other direction.

$ErrorActionPreference = 'Stop'

$selectors = @($Base, $Commit) | Where-Object { $_ }
if ($selectors.Count + [int]$Uncommitted.IsPresent -gt 1) {
    throw 'Pass one of -Base, -Commit or -Uncommitted.'
}

# npm installs codex as a shim set -- `codex`, `codex.cmd`, `codex.ps1` -- and
# Process.Start cannot launch the .ps1 one ("not a valid application for this OS
# platform"). The first version of this wrapper fell back to `cmd.exe /c` with the
# .cmd shim, and a Codex review of that version caught the hole: cmd re-parses its
# command line with its own rules, so a value containing `" & …` would have run a
# command of its own choosing OUTSIDE the read-only sandbox this wrapper exists to
# impose. There is therefore no shell fallback at all -- the real executable is
# found, or the run refuses and says how to point at it.
$codexExecutable = $null
if ($CodexPath) {
    if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
        throw ('-CodexPath does not exist: ' + $CodexPath)
    }
    $codexExecutable = (Resolve-Path -LiteralPath $CodexPath).Path
} else {
    $onPath = Get-Command 'codex.exe' -ErrorAction SilentlyContinue
    if ($onPath -and $onPath.Source -and [IO.Path]::GetExtension($onPath.Source) -eq '.exe') {
        $codexExecutable = $onPath.Source
    } else {
        # The npm package vendors a per-platform binary under its own directory.
        # Recurse from the package root -- with -LiteralPath, because a wildcard
        # path through `@openai` silently matches nothing -- and keep the newest
        # if a machine has several. Bounded there it costs ~40 ms; the whole of
        # node_modules costs six seconds, and the exact vendor path is versioned.
        $shim = Get-Command 'codex.cmd', 'codex' -ErrorAction SilentlyContinue |
            Where-Object { $_.Source } | Select-Object -First 1
        if ($shim) {
            $packageRoot = Join-Path (Split-Path -Parent $shim.Source) 'node_modules\@openai\codex'
            if (Test-Path -LiteralPath $packageRoot -PathType Container) {
                $vendored = Get-ChildItem -LiteralPath $packageRoot -Filter 'codex.exe' -Recurse -File `
                    -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
                if ($vendored) {
                    $codexExecutable = $vendored.FullName
                }
            }
        }
    }
}
if (-not $codexExecutable) {
    throw ('A real codex executable was not found (only npm shims, which would need a shell). ' +
        'Pass -CodexPath <path to codex.exe>, or install a build that puts codex.exe on PATH.')
}

# The run home is built inside the try below, so that a failure between creating it
# and starting Codex still deletes the copied credential.
$operatorHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$operatorAuth = Join-Path $operatorHome 'auth.json'
if (-not (Test-Path -LiteralPath $operatorAuth -PathType Leaf)) {
    throw ('No Codex credentials found at ' + $operatorAuth + '. Run `codex login` first; this wrapper does not authenticate.')
}

# --- the bundle: what the reviewer sees, and the only thing it sees ----------------
if ($Base) {
    # Two traps, both found by running this against a stale base and getting a review of
    # months-old code back:
    #
    #   * a bare `main` is the LOCAL branch, which in a worktree checkout can sit far
    #     behind its remote -- here it was 10d29f4 against origin/main at 86d243b, and the
    #     diff came to 467 KB of unrelated history that then hit the truncation cap;
    #   * `<base>...HEAD` is a range of COMMITS, so staged and unstaged work is absent.
    #     A review of "the branch" that silently skips what is not committed yet is worse
    #     than no review, because it looks like one.
    #
    # So: the resolved commit is reported, a diverging remote-tracking ref of the same name
    # is named, and the working tree is appended to the committed range.
    # ^{commit}, not the bare name: `rev-parse --verify` is happy with a blob or tree
    # SHA too, and the triple-dot diff below then exits 128 for something that looked
    # like a valid base.
    $baseSha = (& git rev-parse --short --verify ($Base + '^{commit}') 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $baseSha) {
        throw ("Base '" + $Base + "' does not resolve to a commit in this repository.")
    }
    $remoteSha = (& git rev-parse --short --verify ("refs/remotes/origin/" + $Base) 2>$null)
    if ($remoteSha -and $remoteSha -ne $baseSha) {
        Write-Warning ("Base '" + $Base + "' resolves to " + $baseSha + ", while origin/" + $Base +
            " is " + $remoteSha + ". Reviewing against the local ref as asked; pass -Base origin/" +
            $Base + " to compare against the remote instead.")
    }
    $headSha = (& git rev-parse --short --verify HEAD)
    $scope = "changes against '$Base' ($baseSha), HEAD at $headSha, plus the working tree"
    $diff = @(('--- committed on this branch, ' + $Base + '...HEAD ---'), '')
    $diff += (& git diff "$Base...HEAD")
    # Checked here, immediately. The working-tree and untracked commands below succeed
    # and overwrite $LASTEXITCODE, so a check after them reads their status and not this
    # one -- the bundle would carry only uncommitted work while the header claimed the
    # whole base range.
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff " + $Base + "...HEAD failed with exit " + $LASTEXITCODE +
            "; the review bundle would have claimed a range it does not contain.")
    }
    $stat = & git diff --stat "$Base...HEAD"
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff --stat " + $Base + "...HEAD failed with exit " + $LASTEXITCODE + ".")
    }
    $working = & git diff HEAD
    # Checked here for the same reason as the range above, and because the failure is
    # silent in the worst way: a failed `git diff HEAD` produces no stdout, so the
    # branch below would label the working tree clean, and the `ls-files` that follows
    # would overwrite the status that proved otherwise. The reviewer would then be told
    # it is seeing uncommitted work that was never in the bundle.
    if ($LASTEXITCODE -ne 0) {
        throw ("git diff HEAD failed with exit " + $LASTEXITCODE +
            "; the bundle would have claimed to include the working tree without doing so.")
    }
    if ($working) {
        $diff += @('', '--- staged and unstaged, not yet committed ---', '')
        $diff += $working
        $stat = @($stat) + @('', 'working tree:') + (& git diff --stat HEAD)
        if ($LASTEXITCODE -ne 0) {
            throw ("git diff --stat HEAD failed with exit " + $LASTEXITCODE + ".")
        }
    } else {
        $diff += @('', '--- the working tree is clean; everything above is committed ---')
    }
    $untracked = & git -c core.quotepath=false ls-files --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw ("git ls-files failed with exit " + $LASTEXITCODE +
            "; a new file could have been left out of the bundle unnoticed.")
    }
    if ($untracked) {
        $diff += @('', '--- untracked files, absent from every diff above ---')
        foreach ($path in $untracked) {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $diff += @('', ('=== ' + $path), (Get-Content -LiteralPath $path -Raw))
            } else {
                Write-Warning ('Untracked path could not be read, so it is listed without contents: ' + $path)
                $diff += @('', ('=== ' + $path + '   [CONTENTS UNAVAILABLE - not reviewed]'))
            }
        }
    }
} elseif ($Commit) {
    $scope = "the changes introduced by commit $Commit"
    $diff = & git show $Commit
    $stat = & git show --stat --oneline $Commit
} else {
    $scope = 'staged, unstaged and untracked changes in the working tree'
    $diff = & git diff HEAD
    $stat = & git diff --stat HEAD
    # core.quotepath=false, because git otherwise returns a non-ASCII name as a
    # quoted, backslash-escaped string -- and this repository has Cyrillic paths.
    # Passing that to -LiteralPath finds nothing, and the bundle would then name a
    # new file and omit its contents.
    $untracked = & git -c core.quotepath=false ls-files --others --exclude-standard
    if ($untracked) {
        # An untracked file is invisible to `git diff`, so it is named and included
        # whole. Without this the most common case -- a new file -- reviews as empty.
        $diff += @('', '--- untracked files, absent from the diff above ---')
        foreach ($path in $untracked) {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $diff += @('', ('=== ' + $path), (Get-Content -LiteralPath $path -Raw))
            } else {
                # Loud, not silent: a named file with no contents would be reviewed as
                # an empty change, and the reviewer could not tell.
                Write-Warning ('Untracked path could not be read, so it is listed without contents: ' + $path)
                $diff += @('', ('=== ' + $path + '   [CONTENTS UNAVAILABLE - not reviewed]'))
            }
        }
    }
}
# For -Commit and the working-tree default this is still the first check after their
# git calls. The -Base path checks each command as it runs, above, because the commands
# that follow it would overwrite the status being tested.
if ($LASTEXITCODE -ne 0) {
    throw ('git failed while building the review bundle (exit ' + $LASTEXITCODE +
        '). Is this a git repository, and does the base exist?')
}
$diffText = ($diff -join "`n")
$truncated = $false
if ($diffText.Length -gt $MaxBundleChars) {
    # An agent's output budget is finite and reading counts against it; a bundle that
    # overflows kills the run instead of shortening the answer. Cut it here and say
    # so, so the reviewer knows it is looking at part of a change.
    $diffText = $diffText.Substring(0, $MaxBundleChars)
    $truncated = $true
}

$instructions = @(
    'You are reviewing a diff as an external reviewer. Report correctness defects first:',
    'logic errors, unhandled cases, contract violations, security issues. Then, briefly,',
    'anything duplicated, needlessly complex or measurably wasteful.',
    '',
    'Format each finding as:  - [P1|P2|P3] <one-line claim> -- <file>:<line>',
    'followed by an indented paragraph giving the concrete failure: the inputs or state,',
    'and the wrong result. Most severe first. If you find nothing, say so plainly rather',
    'than inventing something.',
    '',
    'You cannot run commands and you cannot open files. Everything you have is below. If',
    'the bundle is not enough to judge something, say which file you would need instead',
    'of guessing.',
    ''
)
if ($Prompt) {
    $instructions += @('Additional instructions from the requester:', $Prompt, '')
}
$header = @(
    ('Repository: ' + (Get-Location).Path),
    ('Scope: ' + $scope),
    ''
)
if ($truncated) {
    $header += @(('NOTE: the diff below was truncated at ' + $MaxBundleChars +
        ' characters. You are seeing part of the change; say so if that prevents a judgement.'), '')
}
$bundle = (($instructions + $header + @('--- diff --stat ---') + $stat +
    @('', '--- diff ---', $diffText)) -join "`n")

Write-Host ($codexExecutable + ' exec  (' + $scope + ', bundle ' + $bundle.Length + ' chars)') -ForegroundColor DarkGray

$arguments = @(
    'exec',
    '--sandbox', 'read-only',
    # The hosted `codex_apps` catalogue, off: its write operations act on a server
    # where the local sandbox does not reach, and it is how earlier runs silently
    # read this repository from GitHub instead of from here.
    '--disable', 'apps',
    '-c', 'sandbox_mode="read-only"',
    '-c', 'approval_policy="never"',
    '-c', 'mcp_servers={}',
    '-'
)

$reviewEncoding = New-Object System.Text.UTF8Encoding($false)
$oldOutputEncoding = [Console]::OutputEncoding
$reviewProcess = $null
$runHome = $null
$runAuth = $null
$keepRunHome = $false
try {
    [Console]::OutputEncoding = $reviewEncoding

    # Auth only: no marketplace, no plugin, no skill, no MCP server to discover.
    # The prefix is deliberately not `codex-review-`, which is what Codex itself
    # names its own temp object stores; a cleanup glob must not match those.
    $runHome = Join-Path ([IO.Path]::GetTempPath()) ('navishelper-codex-review-' + [Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $runHome)
    $runAuth = Join-Path $runHome 'auth.json'
    Copy-Item -LiteralPath $operatorAuth -Destination $runAuth
    # Remembered so a rotated credential can be carried back. See the write-back
    # below for why deleting this directory blindly can log the operator out.
    $authHashBefore = (Get-FileHash -LiteralPath $runAuth -Algorithm SHA256).Hash
    $authKeysBefore = @((Get-Content -LiteralPath $runAuth -Raw | ConvertFrom-Json).PSObject.Properties.Name | Sort-Object)
    # The destination is snapshotted as well. If the operator signs in again, or
    # another Codex process writes, while this review runs, then writing our copy
    # back would replace that newer state with an older one.
    $operatorHashBefore = (Get-FileHash -LiteralPath $operatorAuth -Algorithm SHA256).Hash

    # The model is inherited, because a bare home takes the service default and drops
    # reasoning effort to none -- which is not the reviewer the operator configured.
    $inheritedModel = $Model
    $inheritedEffort = ''
    $operatorConfig = Join-Path $operatorHome 'config.toml'
    if (Test-Path -LiteralPath $operatorConfig -PathType Leaf) {
        foreach ($line in Get-Content -LiteralPath $operatorConfig) {
            if ($line -match '^\s*\[') { break }  # top-level keys only
            if (-not $inheritedModel -and $line -match '^\s*model\s*=\s*"([^"]+)"') {
                $inheritedModel = $Matches[1]
            } elseif ($line -match '^\s*model_reasoning_effort\s*=\s*"([^"]+)"') {
                $inheritedEffort = $Matches[1]
            }
        }
    }
    $runConfigLines = @(
        'sandbox_mode = "read-only"',
        'approval_policy = "never"',
        'suppress_unstable_features_warning = true'
    )
    if ($inheritedModel) {
        $runConfigLines += ('model = "{0}"' -f $inheritedModel)
    }
    if ($inheritedEffort) {
        $runConfigLines += ('model_reasoning_effort = "{0}"' -f $inheritedEffort)
    }
    [IO.File]::WriteAllLines((Join-Path $runHome 'config.toml'), $runConfigLines)

    # Quote Windows argv entries without involving a shell, the same way
    # claude-review.ps1 does: double the backslashes before a quote and at the
    # closing quote so a value survives intact.
    $quotedArguments = foreach ($argument in $arguments) {
        $escaped = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $codexExecutable
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $reviewEncoding
    $startInfo.StandardErrorEncoding = $reviewEncoding
    # Only the child's environment changes; the operator's own home is untouched.
    $startInfo.EnvironmentVariables['CODEX_HOME'] = $runHome
    $reviewProcess = New-Object System.Diagnostics.Process
    $reviewProcess.StartInfo = $startInfo
    [void]$reviewProcess.Start()
    $stdoutTask = $reviewProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $reviewProcess.StandardError.ReadToEndAsync()
    # Write UTF-8 bytes directly, as claude-review.ps1 does: Windows PowerShell's
    # native pipeline can fall back to ASCII when a wrapper is nested, and a diff in
    # this repository carries Cyrillic identifiers.
    $bundleBytes = $reviewEncoding.GetBytes($bundle + [Environment]::NewLine)
    $reviewProcess.StandardInput.BaseStream.Write($bundleBytes, 0, $bundleBytes.Length)
    $reviewProcess.StandardInput.Close()
    $reviewProcess.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    [Console]::Out.Write($stdout)
    [Console]::Error.Write($stderrTask.GetAwaiter().GetResult())
    if ($OutputPath) {
        [IO.File]::WriteAllText($OutputPath, $stdout, $reviewEncoding)
        Write-Host ('Review written to ' + $OutputPath) -ForegroundColor DarkGray
    }
    $reviewExitCode = $reviewProcess.ExitCode

} finally {
    # --- credential write-back (begin) ---
    #
    # Carry a rotated credential back BEFORE the home is deleted.
    #
    # Providers rotate refresh tokens: refreshing CONSUMES the old one and issues a
    # replacement. If Codex refreshes inside this disposable home and the home is then
    # deleted, the replacement goes with it and the operator is left holding a retired
    # token -- the next run fails with "authorization grant is invalid" while the
    # credential file looks untouched, so the cause is invisible.
    #
    # This lives in `finally`, not at the end of the try: a failure on the way out --
    # an unwritable -OutputPath, say -- would otherwise skip it and delete the only
    # copy of the new token.
    #
    # Measured once and it did NOT rotate, which proves only that the token had not
    # expired during that run. The write-back does not wait to have seen it happen.
    if ($runAuth -and (Test-Path -LiteralPath $runAuth)) {
        $authHashAfter = (Get-FileHash -LiteralPath $runAuth -Algorithm SHA256).Hash
        if ($authHashAfter -ne $authHashBefore) {
            $carried = $false
            $reason = ''
            $operatorHashNow = (Get-FileHash -LiteralPath $operatorAuth -Algorithm SHA256).Hash
            if ($operatorHashNow -ne $operatorHashBefore) {
                # Somebody else wrote the real store while this review ran -- an
                # operator signing in again, or a concurrent Codex process. Ours is
                # the older state now, so it must not win.
                $reason = 'the operator credential changed during the review, so this run''s older copy was not written back'
            } else {
                # Refuse to write back anything that does not parse, or whose
                # top-level shape changed: an overwritten credential store is not
                # recoverable.
                try {
                    $parsed = Get-Content -LiteralPath $runAuth -Raw | ConvertFrom-Json
                    $keysAfter = @($parsed.PSObject.Properties.Name | Sort-Object)
                    if (Compare-Object $authKeysBefore $keysAfter) {
                        $reason = 'the new credential file has a different top-level shape'
                    } else {
                        Copy-Item -LiteralPath $runAuth -Destination $operatorAuth -Force
                        $carried = $true
                        Write-Host 'Codex rotated its credential during the review; carried the new one back.' -ForegroundColor DarkGray
                    }
                } catch {
                    $reason = 'the new credential file does not parse as JSON'
                }
            }
            if (-not $carried) {
                Write-Warning ('Codex changed its credential during the review and it was NOT written back: ' +
                    $reason + '. The next codex run may need `codex login`. The file is at: ' +
                    $runAuth + ' -- inspect it, then delete it.')
                $keepRunHome = $true
            }
        }
    }
    # --- credential write-back (end) ---

    if ($reviewProcess) {
        $reviewProcess.Dispose()
    }
    [Console]::OutputEncoding = $oldOutputEncoding
    if ($runHome -and -not $keepRunHome -and (Test-Path -LiteralPath $runHome)) {
        # Loud on failure, not silent: this directory holds a copy of a real
        # credential, and on Windows a read-only file (a git object, say) can defeat
        # the delete. A quiet failure leaves the copy in %TEMP% indefinitely.
        Remove-Item -LiteralPath $runHome -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $runHome) {
            Get-ChildItem -LiteralPath $runHome -Recurse -Force -File -ErrorAction SilentlyContinue |
                ForEach-Object { try { $_.IsReadOnly = $false } catch { } }
            Remove-Item -LiteralPath $runHome -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $runHome) {
            Write-Warning ('Could not delete the review home, which holds a copy of your Codex credential. ' +
                'Delete it by hand: ' + $runHome)
        }
    }
}
exit $reviewExitCode

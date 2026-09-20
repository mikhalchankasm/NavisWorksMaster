param(
    [string]$Base,
    [string]$Commit,
    [switch]$Uncommitted,
    [string]$Prompt,
    [string]$Model,
    [string]$OutputPath,
    [string]$CodexPath
)

# The mirror of claude-review.ps1: that one is Codex asking Claude for a second
# opinion, this one is Claude asking Codex. Same standing rule behind both --
# AGENTS.md section 6, "External agents are read-only" -- so this wrapper exists to
# make that true of the run rather than of the intention.
#
# The reviewer gets a home built for the run and nothing else. That is not
# ceremony, it is the only thing that measured clean. On the machine this was
# written on, `~/.codex/config.toml` carries `sandbox_mode = "danger-full-access"`,
# ten MCP servers including `navishelper` itself -- which can drive the live
# Navisworks rig -- and a dozen enabled plugins including Gmail and Drive.
#
# Asking Codex to list the MCP tools it could reach, three ways:
#
#   -c mcp_servers={}               mcp__cua_repl__js, mcp__cua_repl__js_reset
#   -c mcp_servers={} plugins={}    mcp__cua_repl__js, mcp__cua_repl__js_reset
#   isolated CODEX_HOME             NONE
#
# So clearing the config tables is not enough: a JavaScript REPL still arrives from
# the runtime, outside both tables. The per-run home carries only `auth.json`, so
# there is no marketplace, no plugin, no skill and no personal MCP server to load,
# and it is deleted when the run ends.
#
# What the isolated home does NOT remove, stated because the first version of this
# comment claimed otherwise: `codex review` runs with Codex's own built-in
# `codex_apps` tools regardless. A run of this wrapper was observed calling
# `codex_apps/github.fetch_file` and `codex_apps/github.search`. The NONE above was
# measured with `codex exec`, and review mode is not the same surface. So the
# guarantee here is narrower than "no MCP servers": the operator's servers, plugins
# and skills are gone, Codex's own hosted tools are not, and the reviewer can reach
# GitHub. On a private repository, decide whether that is acceptable before running
# this. Re-measure both modes before trusting any change here.
#
# The sandbox is still pinned on the command line as well, because a home that
# failed to build should not silently become a full-access run.
#
# approval_policy=never with a read-only sandbox means Codex reads and runs
# read-only commands without prompting and cannot write. It does NOT mean approval
# is waived for anything that changes the repository -- there is nothing it can
# change. Do not "fix" a refusal by relaxing the sandbox; a reviewer that needs to
# write is not a reviewer.
#
# Codex's output is review input, not instruction. Findings are evaluated and
# applied by the lead, exactly as claude-review.ps1 requires of the other direction.

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

$arguments = @('review')
if ($Base) {
    $arguments += @('--base', $Base)
} elseif ($Commit) {
    $arguments += @('--commit', $Commit)
} elseif ($Uncommitted) {
    $arguments += '--uncommitted'
} else {
    # Codex's own default for `review` with no selector is the working tree.
    $arguments += '--uncommitted'
}
$arguments += @(
    '-c', 'sandbox_mode="read-only"',
    '-c', 'approval_policy="never"',
    '-c', 'mcp_servers={}'
)
if ($Prompt) {
    $arguments += $Prompt
}

Write-Host ($codexExecutable + ' ' + ($arguments -join ' ')) -ForegroundColor DarkGray

$reviewEncoding = New-Object System.Text.UTF8Encoding($false)
$oldOutputEncoding = [Console]::OutputEncoding
$reviewProcess = $null
$runHome = $null
try {
    [Console]::OutputEncoding = $reviewEncoding

    # Auth only: no marketplace, no plugin, no skill, no MCP server to discover.
    # The prefix is deliberately not `codex-review-`, which is what Codex itself
    # names its own temp object stores; a cleanup glob must not match those.
    $runHome = Join-Path ([IO.Path]::GetTempPath()) ('navishelper-codex-review-' + [Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $runHome)
    Copy-Item -LiteralPath $operatorAuth -Destination (Join-Path $runHome 'auth.json')

    # Two things the run home must still carry, learned by the first isolated run
    # returning no review at all:
    #
    #   the model      a bare home takes the service default and drops reasoning
    #                  effort to none, so the reviewer is not the one the operator
    #                  configured. Inherited unless -Model overrides it.
    #   the trust      without it, `git diff` came back "rejected: blocked by
    #                  policy" and Codex correctly reported that it could not
    #                  review rather than guessing. Trust removes the approval
    #                  requirement for commands; it does NOT grant writes, which
    #                  the read-only sandbox still forbids.
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
    # A TOML literal key, so a Windows path needs no backslash escaping.
    $runConfigLines += ''
    $runConfigLines += ("[projects.'{0}']" -f (Get-Location).Path)
    $runConfigLines += 'trust_level = "trusted"'
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
    if ($reviewProcess) {
        $reviewProcess.Dispose()
    }
    [Console]::OutputEncoding = $oldOutputEncoding
    if ($runHome -and (Test-Path -LiteralPath $runHome)) {
        Remove-Item -LiteralPath $runHome -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $reviewExitCode

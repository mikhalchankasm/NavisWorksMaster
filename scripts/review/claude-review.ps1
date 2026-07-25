param(
    [string]$PromptPath,
    [string]$Prompt,
    [string]$Model
)

$ErrorActionPreference = 'Stop'

if ($PromptPath -and $Prompt) {
    throw 'Use either -PromptPath or -Prompt, not both.'
}

if ($PromptPath) {
    $reviewPrompt = Get-Content -LiteralPath $PromptPath -Raw -Encoding UTF8
} elseif ($Prompt) {
    $reviewPrompt = $Prompt
} else {
    $reviewPrompt = [Console]::In.ReadToEnd()
}

if ([string]::IsNullOrWhiteSpace($reviewPrompt)) {
    throw 'Claude review prompt is empty.'
}

$claudeCommand = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claudeCommand) {
    throw 'Claude Code CLI was not found. Install or sign in to Claude Code before requesting external review.'
}

$oldAnthropicApiKey = $env:ANTHROPIC_API_KEY
try {
    Remove-Item Env:ANTHROPIC_API_KEY -ErrorAction SilentlyContinue

    # Tool-less advisory review only. Do not pass --bare: this relies on the user's local Claude Code subscription/session.
    $arguments = @(
        '--print',
        '--input-format', 'text',
        '--output-format', 'text',
        '--tools', '',
        '--disallowedTools', 'Bash,Read,Edit,Write,Glob,Grep,LS,MultiEdit,NotebookEdit,WebFetch,WebSearch,TodoWrite,Task',
        '--permission-mode', 'dontAsk',
        '--no-session-persistence',
        '--disable-slash-commands'
    )
    if ($Model) {
        $arguments += @('--model', $Model)
    }

    $reviewPrompt | & $claudeCommand.Source @arguments
    exit $LASTEXITCODE
} finally {
    if ($null -ne $oldAnthropicApiKey) {
        $env:ANTHROPIC_API_KEY = $oldAnthropicApiKey
    }
}

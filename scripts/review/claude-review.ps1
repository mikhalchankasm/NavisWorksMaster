param(
    [string]$PromptPath,
    [string]$Prompt,
    [string]$Model
)

$ErrorActionPreference = 'Stop'
$reviewEncoding = New-Object System.Text.UTF8Encoding($false)
$oldInputEncoding = [Console]::InputEncoding
$oldOutputEncoding = [Console]::OutputEncoding
$reviewProcess = $null
try {
    [Console]::InputEncoding = $reviewEncoding
    [Console]::OutputEncoding = $reviewEncoding

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
    if (-not $claudeCommand -or [IO.Path]::GetExtension($claudeCommand.Source) -ne '.exe') {
        throw 'The native Claude Code executable was not found. Install or sign in to native Claude Code before requesting external review.'
    }

    # Tool-less advisory review through the local subscription/session.
    # Explicit empty --tools disables built-ins; strict mode with no
    # --mcp-config loads no MCP servers. A deny list is redundant and stale
    # tool names cause CLI warnings. Do not pass --bare.
    $arguments = @(
        '--print',
        '--input-format', 'text',
        '--output-format', 'text',
        '--tools=',
        '--strict-mcp-config',
        '--permission-mode', 'dontAsk',
        '--no-session-persistence',
        '--disable-slash-commands'
    )
    if ($Model) {
        $arguments += @('--model', $Model)
    }
    # Quote Windows argv entries without involving a shell. Doubling the
    # backslashes before quotes and at the closing quote preserves values.
    $quotedArguments = foreach ($argument in $arguments) {
        $escaped = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $claudeCommand.Source
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $reviewEncoding
    $startInfo.StandardErrorEncoding = $reviewEncoding
    # Change only the child's environment; the caller's key is never touched.
    $startInfo.EnvironmentVariables.Remove('ANTHROPIC_API_KEY')
    $reviewProcess = New-Object System.Diagnostics.Process
    $reviewProcess.StartInfo = $startInfo
    [void]$reviewProcess.Start()
    $stdoutTask = $reviewProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $reviewProcess.StandardError.ReadToEndAsync()
    # Write UTF-8 bytes directly. This also works in Windows PowerShell 5.1,
    # whose native pipeline can use ASCII when the wrapper is nested.
    $promptBytes = $reviewEncoding.GetBytes($reviewPrompt + [Environment]::NewLine)
    $reviewProcess.StandardInput.BaseStream.Write($promptBytes, 0, $promptBytes.Length)
    $reviewProcess.StandardInput.Close()
    $reviewProcess.WaitForExit()
    [Console]::Out.Write($stdoutTask.GetAwaiter().GetResult())
    [Console]::Error.Write($stderrTask.GetAwaiter().GetResult())
    $reviewExitCode = $reviewProcess.ExitCode
} finally {
    if ($reviewProcess) {
        $reviewProcess.Dispose()
    }
    [Console]::InputEncoding = $oldInputEncoding
    [Console]::OutputEncoding = $oldOutputEncoding
}
exit $reviewExitCode

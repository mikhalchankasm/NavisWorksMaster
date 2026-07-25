param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [string]$OutputRoot = "",
    [bool]$CreateZip = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "NavisHelper.McpServer\NavisHelper.McpServer.csproj"

function Invoke-NativeCommand([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\mcp-server"
}

$publishName = if ($SelfContained) {
    "NavisHelper.McpServer-$Runtime-self-contained"
} else {
    "NavisHelper.McpServer-$Runtime-framework-dependent"
}

$publishDir = Join-Path $OutputRoot $publishName
$docsDir = Join-Path $publishDir "docs"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $publishDir,
    "--self-contained", ($(if ($SelfContained) { "true" } else { "false" }))
)

Invoke-NativeCommand "dotnet" $publishArgs

New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\MCP_AGENT_SETUP.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\MCP_CLIENT_GUIDE.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\MCP_COMMAND_OWNERSHIP.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\MCP_TOOL_CONTRACTS.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\NAVISWORKS_MCP_COMMAND_CATALOG.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\NAVISWORKS_MCP_QUICKSTART.md") -Destination $docsDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $docsDir "PROJECT_README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "tools\mcp_smoke_test.py") -Destination $publishDir -Force

$config = [ordered]@{
    mcpServers = [ordered]@{
        navishelper = [ordered]@{
            command = "<INSTALL_DIR>\NavisHelper.McpServer.exe"
            args = @()
        }
    }
}
$configPath = Join-Path $publishDir "mcp-client-config.example.json"
$configJson = $config | ConvertTo-Json -Depth 8
$configJson = $configJson.Replace('\u003c', '<').Replace('\u003e', '>')
$configJson | Set-Content -LiteralPath $configPath -Encoding UTF8

$readme = @"
# NavisHelper MCP Server

1. Install the NavisHelper Autodesk ApplicationPlugin bundle for your Navisworks version.
2. Start Navisworks and open a model.
3. Configure your MCP client using `mcp-client-config.example.json`.
4. Optionally run `mcp_smoke_test.py --version 2027` to verify connectivity. Python is only required for this optional smoke test.

Use escaped backslashes in JSON paths, for example `C:\\Tools\\NavisHelper`, not `C:\Tools\NavisHelper`.

This package contains only the standalone MCP server. Navisworks API access still runs inside Navisworks through the installed NavisHelper bundle.

See `docs\MCP_TOOL_CONTRACTS.md` for exact Clash Detective tool input/output fields.
"@
Set-Content -LiteralPath (Join-Path $publishDir "README.md") -Value $readme -Encoding UTF8

if ($CreateZip) {
    $zipPath = Join-Path $OutputRoot ($publishName + ".zip")
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -LiteralPath (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "ZIP: $zipPath"
}

Write-Host "Published: $publishDir"

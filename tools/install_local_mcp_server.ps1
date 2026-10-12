param(
    [string]$Source = "",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-DestinationAllowed([string]$DestinationPath) {
    $destinationFull = (Get-FullPath $DestinationPath).TrimEnd('\') + '\'
    $allowedRoots = @(
        (Join-Path $env:LOCALAPPDATA "NavisHelper")
    )

    foreach ($root in $allowedRoots) {
        $rootFull = (Get-FullPath $root).TrimEnd('\') + '\'
        if ($destinationFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    throw "Refusing to install MCP server outside NavisHelper install roots: $DestinationPath"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $repoRoot "NavisHelper.McpServer\bin\Release\net9.0"
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $env:LOCALAPPDATA "NavisHelper"
}

$sourceFull = Get-FullPath $Source
$destinationFull = Get-FullPath $Destination

Assert-DestinationAllowed $destinationFull
$serverExe = Join-Path $sourceFull "NavisHelper.McpServer.exe"
$configuratorExe = Join-Path $repoRoot "NavisHelper.McpConfigurator\bin\Release\net9.0\NavisHelper.McpConfigurator.exe"
if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) {
    throw "MCP server build was not found: $serverExe. Build NavisHelper.McpServer in Release first."
}
if (-not (Test-Path -LiteralPath $configuratorExe -PathType Leaf)) {
    throw "MCP configurator build was not found: $configuratorExe. Build NavisHelper.McpConfigurator in Release first."
}

# The configurator prints its JSON result and validates the server before activation.
& $configuratorExe --install-server $sourceFull --install-root $destinationFull
if ($LASTEXITCODE -ne 0) {
    throw "MCP server installation failed (exit code $LASTEXITCODE). See the configurator JSON result above."
}

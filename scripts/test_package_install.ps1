param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [string]$TestRoot = ""
)

$ErrorActionPreference = "Stop"

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Assert-DirectoryMissing([string]$Path, [string]$Description) {
    if (Test-Path -LiteralPath $Path) {
        throw "$Description must not exist: $Path"
    }
}

function Get-PackageVersion([string]$PackageDirectory) {
    $manifestPath = Join-Path $PackageDirectory "NavisHelper.bundle\PackageContents.xml"
    Assert-File $manifestPath "Package version manifest"
    $content = Get-Content -LiteralPath $manifestPath -Raw
    $match = [regex]::Match($content, 'AppVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"')
    if (-not $match.Success) {
        throw "Could not read a four-part AppVersion from $manifestPath"
    }

    return $match.Groups[1].Value
}

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Package ZIP was not found: $ZipPath"
}

$navisworksProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -in @("Roamer", "Navisworks")
})
if ($navisworksProcesses.Count -gt 0) {
    $details = ($navisworksProcesses | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
    Write-Warning "Package install smoke test skipped because Navisworks is running: $details. Close Navisworks and rerun this script to exercise installation."
    return
}

if ([string]::IsNullOrWhiteSpace($TestRoot)) {
    $TestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NavisHelper-package-smoke-" + [Guid]::NewGuid().ToString("N"))
}

$TestRoot = [System.IO.Path]::GetFullPath($TestRoot)
$unpackedRoot = Join-Path $TestRoot "unpacked"
$originalEnvironment = @{}
foreach ($name in @("APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles")) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

try {
    New-Item -ItemType Directory -Force -Path $unpackedRoot | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpackedRoot -Force

    $installScript = Join-Path $unpackedRoot "Install-NavisHelperBundle.ps1"
    Assert-File $installScript "Packaged installer script"
    $packageVersion = Get-PackageVersion $unpackedRoot

    $env:APPDATA = Join-Path $TestRoot "appdata\roaming"
    $env:LOCALAPPDATA = Join-Path $TestRoot "appdata\local"
    $env:ProgramData = Join-Path $TestRoot "programdata"
    $env:ProgramFiles = Join-Path $TestRoot "programfiles"

    # Fresh install and same-version reinstall both exercise the generated installer.
    & $installScript
    & $installScript

    $installRoot = Join-Path $env:LOCALAPPDATA "NavisHelper"
    $versionedServer = Join-Path $installRoot ("McpServer-" + $packageVersion)
    $unversionedServer = Join-Path $installRoot "McpServer"
    $bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\NavisHelper.bundle"
    $configurator = Join-Path $installRoot "McpConfigurator\NavisHelper.McpConfigurator.exe"
    $server = Join-Path $versionedServer "NavisHelper.McpServer.exe"

    Assert-File $server "Versioned MCP server executable"
    Assert-File $configurator "MCP configurator executable"
    Assert-File (Join-Path $bundle "PackageContents.xml") "Installed bundle manifest"
    Assert-DirectoryMissing $unversionedServer "Unexpected unversioned MCP server directory"

    # Simulate the stale folder left by v2.6.3.0 and verify that only a managed
    # NavisHelper runtime is removed during an upgrade.
    Copy-Item -LiteralPath (Join-Path $unpackedRoot "McpServer") -Destination $unversionedServer -Recurse -Force
    & $installScript
    Assert-DirectoryMissing $unversionedServer "Managed legacy MCP server directory"

    & $configurator --detect --mcp-server $server
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged MCP configurator detection failed with exit code $LASTEXITCODE."
    }

    Write-Host "Package install smoke test passed: fresh install, same-version reinstall, and v2.6.3.0 legacy upgrade."
}
finally {
    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], "Process")
    }

    if (Test-Path -LiteralPath $TestRoot) {
        Remove-Item -LiteralPath $TestRoot -Recurse -Force
    }
}

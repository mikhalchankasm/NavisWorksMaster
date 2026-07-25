param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$SkipBuild,
    [string]$OutputRoot = "",
    [string]$PackageName = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\distribution"
}

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Remove-DirectorySafely([string]$TargetPath, [string]$AllowedRoot) {
    $targetFull = Get-FullPath $TargetPath
    $rootFull = Get-FullPath $AllowedRoot
    if (-not $targetFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete outside output root: $targetFull"
    }
    if (Test-Path -LiteralPath $targetFull) {
        Remove-Item -LiteralPath $targetFull -Recurse -Force
    }
}

function Resolve-MSBuild {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    $fromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }
    throw "MSBuild.exe not found."
}

function Copy-Directory([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Source directory not found: $Source"
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Remove-DebugSymbols([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) {
        return 0
    }

    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        Remove-Item -LiteralPath $file.FullName -Force
    }

    return $files.Count
}

function Invoke-NativeCommand([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

$modeName = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $PackageName = "NavisHelper-full-$Runtime-$modeName-$timestamp"
}

$packageDir = Join-Path $OutputRoot $PackageName
$mcpOutputRoot = Join-Path $OutputRoot "_mcp-publish"
$bundleSource = Join-Path $repoRoot "NavisHelper.bundle"
$bundleDest = Join-Path $packageDir "NavisHelper.bundle"
$mcpDest = Join-Path $packageDir "McpServer"
$configuratorDest = Join-Path $packageDir "McpConfigurator"
$docsDest = Join-Path $packageDir "docs"
$toolsDest = Join-Path $packageDir "tools"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Remove-DirectorySafely $packageDir $OutputRoot
Remove-DirectorySafely $mcpOutputRoot $OutputRoot
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

if (-not $SkipBuild) {
    $msbuild = Resolve-MSBuild
    $solution = Join-Path $repoRoot "NavisHelper.sln"
    Invoke-NativeCommand $msbuild @($solution, "/t:Restore", "/p:Configuration=Release2024", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/t:Restore", "/p:Configuration=Release2025", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/t:Restore", "/p:Configuration=Release2026", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/t:Restore", "/p:Configuration=Release2027", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/p:Configuration=Release2024", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/p:Configuration=Release2025", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/p:Configuration=Release2026", "/p:Platform=x64", "/m", "/v:m")
    Invoke-NativeCommand $msbuild @($solution, "/p:Configuration=Release2027", "/p:Platform=x64", "/m", "/v:m")
}

$bundle2024 = Join-Path $bundleSource "Contents\2024\NavisHelper.dll"
$bundle2025 = Join-Path $bundleSource "Contents\2025\NavisHelper.dll"
$bundle2026 = Join-Path $bundleSource "Contents\2026\NavisHelper.dll"
$bundle2027 = Join-Path $bundleSource "Contents\2027\NavisHelper.dll"
$bundleContracts2024 = Join-Path $bundleSource "Contents\2024\NavisHelper.Contracts.dll"
$bundleContracts2025 = Join-Path $bundleSource "Contents\2025\NavisHelper.Contracts.dll"
$bundleContracts2026 = Join-Path $bundleSource "Contents\2026\NavisHelper.Contracts.dll"
$bundleContracts2027 = Join-Path $bundleSource "Contents\2027\NavisHelper.Contracts.dll"
$packageContents = Join-Path $bundleSource "PackageContents.xml"
foreach ($required in @($bundle2024, $bundle2025, $bundle2026, $bundle2027, $bundleContracts2024, $bundleContracts2025, $bundleContracts2026, $bundleContracts2027, $packageContents)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required bundle artifact is missing: $required. Run the full NavisHelper build matrix (Release2024, Release2025, Release2026, Release2027 with Platform=x64) before packaging, or rerun this script without -SkipBuild."
    }
}

$packageXml = Get-Content -LiteralPath $packageContents -Raw
if ($packageXml -notmatch "Contents/2024/NavisHelper.dll" -or $packageXml -notmatch "Contents/2025/NavisHelper.dll" -or $packageXml -notmatch "Contents/2026/NavisHelper.dll" -or $packageXml -notmatch "Contents/2027/NavisHelper.dll") {
    throw "PackageContents.xml must reference Contents/2024/NavisHelper.dll, Contents/2025/NavisHelper.dll, Contents/2026/NavisHelper.dll, and Contents/2027/NavisHelper.dll."
}

$publishScript = Join-Path $repoRoot "tools\publish_mcp_server.ps1"
$publishParams = @{
    Runtime = $Runtime
    OutputRoot = $mcpOutputRoot
    CreateZip = $false
}
if ($SelfContained) {
    $publishParams.SelfContained = $true
}
& $publishScript @publishParams

$mcpPublishName = if ($SelfContained) {
    "NavisHelper.McpServer-$Runtime-self-contained"
} else {
    "NavisHelper.McpServer-$Runtime-framework-dependent"
}
$mcpPublishDir = Join-Path $mcpOutputRoot $mcpPublishName
if (-not (Test-Path -LiteralPath (Join-Path $mcpPublishDir "NavisHelper.McpServer.exe"))) {
    throw "Published MCP server executable not found."
}

Copy-Directory $bundleSource $bundleDest
# NavisHelper.Dev is a local script-development assembly and is not referenced
# by PackageContents.xml. Never let a stale ignored build artifact leak into a
# public bundle merely because it happens to exist under Contents/<year>.
Get-ChildItem -LiteralPath $bundleDest -Recurse -File -Filter "NavisHelper.Dev.*" |
    Remove-Item -Force
$leakedDevArtifacts = @(Get-ChildItem -LiteralPath $bundleDest -Recurse -File -Filter "NavisHelper.Dev.*")
if ($leakedDevArtifacts.Count -gt 0) {
    throw "NavisHelper.Dev artifacts must not be included in the public distribution bundle."
}
Copy-Directory $mcpPublishDir $mcpDest

$configuratorProject = Join-Path $repoRoot "NavisHelper.McpConfigurator\NavisHelper.McpConfigurator.csproj"
$configuratorPublishArgs = @(
    "publish",
    $configuratorProject,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $configuratorDest,
    "--self-contained", "true"
)
Invoke-NativeCommand "dotnet" $configuratorPublishArgs
if (-not (Test-Path -LiteralPath (Join-Path $configuratorDest "NavisHelper.McpConfigurator.exe"))) {
    throw "Published MCP configurator executable not found."
}

New-Item -ItemType Directory -Force -Path $docsDest | Out-Null
foreach ($doc in @(
    "CLASH_CLUSTERING_PLAN.md",
    "MCP_AGENT_SETUP.md",
    "MCP_CLIENT_GUIDE.md",
    "MCP_COMMAND_OWNERSHIP.md",
    "MCP_TOOL_CONTRACTS.md",
    "MCP_DISTRIBUTION_PLAN.md",
    "MCP_ARCHITECTURE.md",
    "REDESIGN_PROPOSAL.md",
    "NAVISWORKS_MCP_COMMAND_CATALOG.md",
    "NAVISWORKS_MCP_QUICKSTART.md",
    "NAVISWORKS_MCP_PLAN.md"
)) {
    $source = Join-Path $repoRoot ("docs\" + $doc)
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $docsDest -Force
    }
}

$projectReadme = Join-Path $repoRoot "README.md"
if (Test-Path -LiteralPath $projectReadme) {
    Copy-Item -LiteralPath $projectReadme -Destination (Join-Path $docsDest "PROJECT_README.md") -Force
}

New-Item -ItemType Directory -Force -Path $toolsDest | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "tools\mcp_smoke_test.py") -Destination $toolsDest -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "tools\remove_machinewide_bundle.ps1") -Destination $toolsDest -Force

foreach ($rootDoc in @("SETUP_PROMPT.md", "UPDATE_PROMPT.md")) {
    $source = Join-Path $repoRoot ("docs\prompts\" + $rootDoc)
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $packageDir -Force
    }
}

$installScript = @'
param(
    [switch]$SkipMcp,
    [switch]$ConfigureMcp,
    [string]$Clients = "all"
)

$ErrorActionPreference = "Stop"

function Assert-CopyDestinationSafe([string]$Source, [string]$Destination) {
    $sourceFull = [System.IO.Path]::GetFullPath($Source).TrimEnd('\')
    $destinationFull = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ($sourceFull.Equals($destinationFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourceFull.StartsWith($destinationFull + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $destinationFull.StartsWith($sourceFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Destination '$Destination' must not be the source directory, its parent, or its child."
    }
}

function Copy-DirectoryFresh([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Source directory was not found: $Source"
    }
    Assert-CopyDestinationSafe $Source $Destination
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Assert-InstalledFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found after installation: $Path"
    }
}

function Assert-NavisworksClosed {
    $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in @("Roamer", "Navisworks")
    })

    if ($processes.Count -eq 0) {
        return
    }

    $details = ($processes | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
    throw "Close Autodesk Navisworks before installing the bundle. Running process(es): $details"
}

function Assert-NoLegacyMachineWideInstall {
    $legacyBundle = Join-Path $env:ProgramData "Autodesk\ApplicationPlugins\NavisHelper.bundle"
    $legacyInstallRoot = Join-Path $env:ProgramFiles "NavisHelper"
    if ((Test-Path -LiteralPath $legacyBundle) -or (Test-Path -LiteralPath $legacyInstallRoot)) {
        throw "A legacy machine-wide NavisHelper installation was found in Program Files or ProgramData. NavisHelper supports per-user installation only. Run tools\\remove_machinewide_bundle.ps1 as Administrator from this unpacked package, then run this installer again."
    }
}

function Test-PackageRequiresDotNet9 {
    $manifestPath = Join-Path $PSScriptRoot "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $true
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        return [bool]$manifest.mcp_server.framework_dependent
    } catch {
        return $true
    }
}

function Test-DotNet9RuntimeInstalled {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return $false
    }

    $runtimes = & $dotnet.Source --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    return @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App\s+9\.' }).Count -gt 0
}

function Assert-DotNet9Runtime {
    if (-not (Test-PackageRequiresDotNet9)) {
        return
    }

    if (Test-DotNet9RuntimeInstalled) {
        return
    }

    throw ".NET 9 Runtime is required for this framework-dependent NavisHelper MCP server package. Install it from https://dotnet.microsoft.com/download/dotnet/9.0/runtime, then run this script again; or use a self-contained NavisHelper package."
}

function Get-PackageVersion {
    $manifestPath = Join-Path $PSScriptRoot "NavisHelper.bundle\PackageContents.xml"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Package version manifest was not found: $manifestPath"
    }

    $content = Get-Content -LiteralPath $manifestPath -Raw
    $match = [regex]::Match($content, 'AppVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"')
    if (-not $match.Success) {
        throw "Could not read a four-part AppVersion from $manifestPath"
    }

    return $match.Groups[1].Value
}

function Get-ProcessesFromDirectory([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    $directoryFull = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
                $_.Path.StartsWith($directoryFull, [System.StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    })
}

function Test-ManagedLegacyMcpServer([string]$Directory) {
    $server = Join-Path $Directory "NavisHelper.McpServer.exe"
    $deps = Join-Path $Directory "NavisHelper.McpServer.deps.json"
    if (-not (Test-Path -LiteralPath $server -PathType Leaf) -or
        -not (Test-Path -LiteralPath $deps -PathType Leaf)) {
        return $false
    }

    try {
        return (Get-Content -LiteralPath $deps -Raw) -match '"NavisHelper\.McpServer/'
    } catch {
        return $false
    }
}

function Remove-ManagedLegacyMcpServer([string]$InstallRoot) {
    $legacyServer = Join-Path $InstallRoot "McpServer"
    if (-not (Test-Path -LiteralPath $legacyServer -PathType Container)) {
        return
    }

    if (-not (Test-ManagedLegacyMcpServer $legacyServer)) {
        Write-Warning "Found an unversioned MCP server directory at '$legacyServer'. It was not removed because it could not be verified as a managed NavisHelper installation. Review it manually before deleting it."
        return
    }

    $runningProcesses = Get-ProcessesFromDirectory $legacyServer
    if ($runningProcesses.Count -gt 0) {
        $ids = ($runningProcesses | Select-Object -ExpandProperty Id) -join ", "
        Write-Warning "Found a managed legacy MCP server at '$legacyServer', but it is running (PID: $ids). It was not removed; restart the client and remove the directory manually."
        return
    }

    try {
        Remove-Item -LiteralPath $legacyServer -Recurse -Force
        Write-Host "Removed managed legacy MCP server from $legacyServer"
    } catch {
        Write-Warning "Could not remove managed legacy MCP server '$legacyServer': $($_.Exception.Message)"
    }
}

$InstallRoot = Join-Path $env:LOCALAPPDATA "NavisHelper"

$sourceBundle = Join-Path $PSScriptRoot "NavisHelper.bundle"
$destinationRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$destinationBundle = Join-Path $destinationRoot "NavisHelper.bundle"
$packageVersion = Get-PackageVersion

if (-not $SkipMcp) {
    $sourceMcpServer = Join-Path $PSScriptRoot "McpServer"
    $sourceMcpConfigurator = Join-Path $PSScriptRoot "McpConfigurator"
    $destinationMcpServer = Join-Path $InstallRoot ("McpServer-" + $packageVersion)
    $destinationMcpConfigurator = Join-Path $InstallRoot "McpConfigurator"
    Assert-CopyDestinationSafe $sourceMcpServer $destinationMcpServer
    Assert-CopyDestinationSafe $sourceMcpConfigurator $destinationMcpConfigurator
}

if (-not $SkipMcp) {
    Assert-DotNet9Runtime
}

Assert-NoLegacyMachineWideInstall
Assert-NavisworksClosed
Copy-DirectoryFresh $sourceBundle $destinationBundle
Assert-InstalledFile (Join-Path $destinationBundle "PackageContents.xml") "NavisHelper bundle manifest"
Write-Host "Installed bundle to $destinationBundle"

if (-not $SkipMcp) {
    $runningServerProcesses = Get-ProcessesFromDirectory $destinationMcpServer
    if ($runningServerProcesses.Count -gt 0) {
        $ids = ($runningServerProcesses | Select-Object -ExpandProperty Id) -join ", "
        Write-Warning "NavisHelper MCP server version $packageVersion is already running from '$destinationMcpServer' (PID: $ids). Keeping the active runtime unchanged; restart the MCP client before reinstalling this same version."
    } else {
        Copy-DirectoryFresh $sourceMcpServer $destinationMcpServer
    }
    Copy-DirectoryFresh $sourceMcpConfigurator $destinationMcpConfigurator
    $server = Join-Path $destinationMcpServer "NavisHelper.McpServer.exe"
    $configurator = Join-Path $destinationMcpConfigurator "NavisHelper.McpConfigurator.exe"
    Assert-InstalledFile $server "MCP server executable"
    Assert-InstalledFile $configurator "MCP configurator executable"
    Remove-ManagedLegacyMcpServer $InstallRoot
    Write-Host "Installed MCP server to $destinationMcpServer"
    Write-Host "Installed MCP configurator to $destinationMcpConfigurator"

    if ($ConfigureMcp) {
        & $configurator --configure --clients $Clients --create-missing --mcp-server $server
        if ($LASTEXITCODE -ne 0) {
            throw "MCP configurator failed with exit code $LASTEXITCODE."
        }
        & $configurator --detect --clients $Clients --mcp-server $server
        if ($LASTEXITCODE -ne 0) {
            throw "MCP configurator detection failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Existing MCP stdio sessions are not stopped. Restart or reload each MCP client to use '$destinationMcpServer'."
}
'@
Set-Content -LiteralPath (Join-Path $packageDir "Install-NavisHelperBundle.ps1") -Value $installScript -Encoding UTF8

$mcpConfig = [ordered]@{
    mcpServers = [ordered]@{
        navishelper = [ordered]@{
            command = "<UNPACKED_PACKAGE_DIR>\McpServer\NavisHelper.McpServer.exe"
            args = @()
        }
    }
}
$mcpConfigJson = $mcpConfig | ConvertTo-Json -Depth 8
$mcpConfigJson = $mcpConfigJson.Replace('\u003c', '<').Replace('\u003e', '>')
$mcpConfigJson | Set-Content -LiteralPath (Join-Path $packageDir "mcp-client-config.example.json") -Encoding UTF8

$readme = @'
# NavisHelper Full Distribution

This package contains:

- `NavisHelper.bundle` for Autodesk Navisworks Manage 2024, 2025, 2026, and 2027.
- `McpServer\NavisHelper.McpServer.exe` for MCP-capable agents.
- `McpConfigurator\NavisHelper.McpConfigurator.exe` for automatic MCP client configuration.
- `mcp-client-config.example.json`.
- `SETUP_PROMPT.md` and `UPDATE_PROMPT.md`.
- `tools\mcp_smoke_test.py` and `tools\remove_machinewide_bundle.ps1`.
- documentation in `docs`, including `MCP_TOOL_CONTRACTS.md`, `MCP_CLIENT_GUIDE.md`, `NAVISWORKS_MCP_COMMAND_CATALOG.md`, `NAVISWORKS_MCP_QUICKSTART.md`, and `PROJECT_README.md`.

Framework-dependent packages require .NET 9 runtime for `McpServer\NavisHelper.McpServer.exe`. Self-contained packages include the runtime.

## Update from a machine-wide installation

NavisHelper supports per-user installation only. If a legacy installation remains in `Program Files` or `ProgramData`, run `tools\remove_machinewide_bundle.ps1 -Force` from an elevated PowerShell, then run the per-user installer again.

## Install for the current user

Close Navisworks first, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
```

This installs:

- `NavisHelper.bundle` to `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`.
- `McpServer` to `%LOCALAPPDATA%\NavisHelper\McpServer-<package-version>`.
- `McpConfigurator` to `%LOCALAPPDATA%\NavisHelper\McpConfigurator`.
- MCP client config for supported clients when `-ConfigureMcp` is passed.

## Agent install from this ZIP

If an AI agent receives this package as a ZIP file, it should use the ZIP directly:

1. Unpack the ZIP to a normal folder.
2. Close Autodesk Navisworks if it is running.
3. Run PowerShell from the unpacked package folder. Administrator rights are not required.
4. Execute:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
```

The agent should not clone the repository or download GitHub Release assets when this ZIP is already available.

## Configure MCP client

Copy `mcp-client-config.example.json` into your MCP client configuration and replace `<UNPACKED_PACKAGE_DIR>` with this package folder.
Use escaped backslashes in JSON paths, for example `C:\\Tools\\NavisHelper`, not `C:\Tools\NavisHelper`.

Or run the configurator:

```powershell
& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --configure --clients all
```

Supported client ids: `claude-desktop`, `claude-code`, `codex`, `cursor`, `opencode`, `kimi`.

## Optional smoke test

Python is not required for normal installation or MCP usage. It is only needed if you want to run this optional local validation script.

Start Navisworks, open a model, then run:

```powershell
python .\tools\mcp_smoke_test.py --version 2027
```

Or launch with the latest `.nwd` from a folder:

```powershell
python .\tools\mcp_smoke_test.py --version 2027 --launch --nwd-dir "D:\Path\To\NWD"
```

## MCP tool contracts

For exact Clash Detective MCP input/output fields, read:

```text
docs\MCP_TOOL_CONTRACTS.md
```
'@
Set-Content -LiteralPath (Join-Path $packageDir "README.md") -Value $readme -Encoding UTF8

$manifest = [ordered]@{
    package_name = $PackageName
    created_utc = (Get-Date).ToUniversalTime().ToString("o")
    runtime = $Runtime
    self_contained = [bool]$SelfContained
    bundle = [ordered]@{
        dll_2024 = [ordered]@{
            path = "NavisHelper.bundle\Contents\2024\NavisHelper.dll"
            size = (Get-Item -LiteralPath $bundle2024).Length
            last_write_time = (Get-Item -LiteralPath $bundle2024).LastWriteTimeUtc.ToString("o")
        }
        dll_2025 = [ordered]@{
            path = "NavisHelper.bundle\Contents\2025\NavisHelper.dll"
            size = (Get-Item -LiteralPath $bundle2025).Length
            last_write_time = (Get-Item -LiteralPath $bundle2025).LastWriteTimeUtc.ToString("o")
        }
        dll_2026 = [ordered]@{
            path = "NavisHelper.bundle\Contents\2026\NavisHelper.dll"
            size = (Get-Item -LiteralPath $bundle2026).Length
            last_write_time = (Get-Item -LiteralPath $bundle2026).LastWriteTimeUtc.ToString("o")
        }
        dll_2027 = [ordered]@{
            path = "NavisHelper.bundle\Contents\2027\NavisHelper.dll"
            size = (Get-Item -LiteralPath $bundle2027).Length
            last_write_time = (Get-Item -LiteralPath $bundle2027).LastWriteTimeUtc.ToString("o")
        }
    }
    mcp_server = [ordered]@{
        path = "McpServer\NavisHelper.McpServer.exe"
        framework_dependent = -not [bool]$SelfContained
    }
    mcp_configurator = [ordered]@{
        path = "McpConfigurator\NavisHelper.McpConfigurator.exe"
        self_contained = $true
        supported_clients = @("claude-desktop", "claude-code", "codex", "cursor", "opencode", "kimi")
    }
}

$removedDebugSymbolCount = Remove-DebugSymbols $packageDir
$manifest["debug_symbols_excluded"] = $true
$manifest["debug_symbol_file_count_removed"] = $removedDebugSymbolCount

($manifest | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath (Join-Path $packageDir "manifest.json") -Encoding UTF8

$checksumPath = Join-Path $packageDir "checksums.sha256"
$packageRootWithSeparator = (Get-FullPath $packageDir).TrimEnd('\') + '\'
$checksums = Get-ChildItem -LiteralPath $packageDir -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($packageRootWithSeparator.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        "$hash *$relativePath"
    } |
    Sort-Object
$checksums | Set-Content -LiteralPath $checksumPath -Encoding ASCII

$zipPath = Join-Path $OutputRoot ($PackageName + ".zip")
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

$validationScript = Join-Path $repoRoot "tools\validate_distribution.ps1"
& $validationScript -PackagePath $packageDir -ZipPath $zipPath

$packageSmokeTest = Join-Path $repoRoot "scripts\test_package_install.ps1"
if (-not (Test-Path -LiteralPath $packageSmokeTest -PathType Leaf)) {
    throw "Package installation smoke test was not found: $packageSmokeTest"
}
& $packageSmokeTest -ZipPath $zipPath

Write-Host "Package: $packageDir"
Write-Host "ZIP: $zipPath"

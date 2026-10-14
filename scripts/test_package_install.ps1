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

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
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

function Get-NormalizedPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

# Returns the junction target of $Path, or $null when $Path is missing or a plain directory.
function Get-JunctionTarget([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (-not ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        return $null
    }
    Assert-True ($item.LinkType -eq "Junction") "$Path is a reparse point but not a junction: $($item.LinkType)"
    $target = [string](@($item.Target)[0])
    if ($target.StartsWith('\??\')) {
        $target = $target.Substring(4)
    }
    return Get-NormalizedPath $target
}

function Get-VersionDirectories([string]$InstallRoot) {
    $versions = Join-Path $InstallRoot "versions"
    if (-not (Test-Path -LiteralPath $versions -PathType Container)) {
        return @()
    }
    return @(Get-ChildItem -LiteralPath $versions -Directory -Force)
}

# Every case gets its own profile roots, so cases cannot see each other's state.
function Enter-Case([string]$Name) {
    $caseRoot = Join-Path $TestRoot $Name
    $env:APPDATA = Join-Path $caseRoot "appdata\roaming"
    $env:LOCALAPPDATA = Join-Path $caseRoot "appdata\local"
    $env:ProgramData = Join-Path $caseRoot "programdata"
    $env:ProgramFiles = Join-Path $caseRoot "programfiles"
    New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    return Join-Path $env:LOCALAPPDATA "NavisHelper"
}

# Runs the generated installer in a child PowerShell that inherits the case's profile roots.
# -ConfigureMcp is never passed: the configurator resolves client configs through the
# Windows known-folder API, which ignores these environment overrides, so configuring
# clients here would rewrite the real profile's client configs.
function Invoke-PackageInstaller {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    Write-Host $output
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Assert-InstallSucceeded($Result, [string]$Case) {
    Assert-True ($Result.ExitCode -eq 0) "${Case}: installer exited with $($Result.ExitCode)."
    Assert-True ($Result.Output -match '"Activated"\s*:\s*true') "${Case}: installer did not print the configurator's activation JSON."
    Assert-True ($Result.Output -match 'Restart or reload') "${Case}: installer did not tell the user to restart MCP clients."
}

# Asserts the stable layout and returns the junction target directory.
function Assert-StoreLayout([string]$InstallRoot, $Result, [string]$Case) {
    $junction = Join-Path $InstallRoot "McpServer"
    $target = Get-JunctionTarget $junction
    Assert-True ($null -ne $target) "${Case}: $junction is not a junction."
    $versions = Get-NormalizedPath (Join-Path $InstallRoot "versions")
    Assert-True ((Get-NormalizedPath (Split-Path $target -Parent)) -ieq $versions) "${Case}: junction target $target is not under $versions."
    $pattern = '^' + [regex]::Escape($packageVersion) + '-[0-9a-f]{8}$'
    Assert-True ((Split-Path $target -Leaf) -match $pattern) "${Case}: junction target $target is not named $packageVersion-<hash8>."
    Assert-File (Join-Path $target "NavisHelper.McpServer.exe") "${Case}: activated MCP server executable"

    $clientServer = Join-Path $junction "NavisHelper.McpServer.exe"
    $expectedLine = "MCP client path: $clientServer"
    $lines = @($Result.Output -split "\r?\n" | ForEach-Object { $_.Trim() })
    Assert-True ($lines -contains $expectedLine) "${Case}: installer did not report '$expectedLine'."
    Assert-True (-not ($Result.Output -match [regex]::Escape("McpServer-$packageVersion"))) "${Case}: installer still names McpServer-$packageVersion."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $InstallRoot "McpServer-$packageVersion"))) "${Case}: installer created McpServer-$packageVersion."
    return $target
}

function New-LegacyServer([string]$Destination) {
    Copy-Item -LiteralPath (Join-Path $unpackedRoot "McpServer") -Destination $Destination -Recurse -Force
    # The marker changes the tree hash and proves the legacy files were not discarded.
    Set-Content -LiteralPath (Join-Path $Destination "legacy-marker.txt") -Value $Destination -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Package ZIP was not found: $ZipPath"
}

if ([string]::IsNullOrWhiteSpace($TestRoot)) {
    $TestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NavisHelper-package-smoke-" + [Guid]::NewGuid().ToString("N"))
}

$TestRoot = [System.IO.Path]::GetFullPath($TestRoot)
$tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $TestRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not ([System.IO.Path]::GetFileName($TestRoot)).StartsWith('NavisHelper-package-smoke-', [System.StringComparison]::Ordinal) -or
    (Test-Path -LiteralPath $TestRoot)) {
    throw "TestRoot must be a new NavisHelper-package-smoke-* directory under TEMP: $TestRoot"
}
$navisworksProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -in @("Roamer", "Navisworks")
})
if ($navisworksProcesses.Count -gt 0) {
    $details = ($navisworksProcesses | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
    Write-Warning "Package install smoke test skipped because Navisworks is running: $details. Close Navisworks and rerun this script to exercise installation."
    return
}

$unpackedRoot = Join-Path $TestRoot "unpacked"
$legacyProcess = $null
$originalEnvironment = @{}
foreach ($name in @("APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles")) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

try {
    New-Item -ItemType Directory -Force -Path $unpackedRoot | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpackedRoot -Force

    Assert-File (Join-Path $unpackedRoot "LICENSE") "Packaged project license"
    Assert-File (Join-Path $unpackedRoot "THIRD-PARTY-NOTICES.md") "Packaged third-party notices"
    Assert-File (Join-Path $unpackedRoot "licenses\dotnet-runtime-9.0.18-THIRD-PARTY-NOTICES.TXT") "Packaged .NET runtime notices"
    Assert-File (Join-Path $unpackedRoot "NavisHelper.bundle\Contents\AiWorker\NavisHelper.AiWorker.exe") "Packaged OpenRouter AI worker executable"
    Assert-File (Join-Path $unpackedRoot "NavisHelper.bundle\Contents\AiWorker\NavisHelper.AiWorker.runtimeconfig.json") "Packaged OpenRouter AI worker runtime configuration"
    Assert-File (Join-Path $unpackedRoot "NavisHelper.bundle\Contents\AiWorker\Newtonsoft.Json.dll") "Packaged OpenRouter AI worker JSON dependency"
    $autodeskInterop = @(Get-ChildItem -LiteralPath (Join-Path $unpackedRoot "NavisHelper.bundle\Contents") -Recurse -File -Filter "Autodesk.Navisworks.Interop.ComApi.dll")
    if ($autodeskInterop.Count -gt 0) {
        throw "Packaged bundle contains Autodesk.Navisworks.Interop.ComApi.dll: $($autodeskInterop.FullName -join ', ')"
    }

    $installScript = Join-Path $unpackedRoot "Install-NavisHelperBundle.ps1"
    Assert-File $installScript "Packaged installer script"
    $packageVersion = Get-PackageVersion $unpackedRoot

    # 1. Fresh install: McpServer is a junction into the version store; clients get the junction path.
    $installRoot = Enter-Case "fresh"
    $result = Invoke-PackageInstaller
    Assert-InstallSucceeded $result "fresh install"
    $freshTarget = Assert-StoreLayout $installRoot $result "fresh install"
    $bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\NavisHelper.bundle"
    $configurator = Join-Path $installRoot "McpConfigurator\NavisHelper.McpConfigurator.exe"
    Assert-File $configurator "MCP configurator executable"
    Assert-File (Join-Path $bundle "PackageContents.xml") "Installed bundle manifest"
    Assert-File (Join-Path $bundle "Contents\AiWorker\NavisHelper.AiWorker.exe") "Installed OpenRouter AI worker executable"
    Assert-File (Join-Path $bundle "Contents\AiWorker\NavisHelper.AiWorker.runtimeconfig.json") "Installed OpenRouter AI worker runtime configuration"
    Assert-File (Join-Path $bundle "Contents\AiWorker\Newtonsoft.Json.dll") "Installed OpenRouter AI worker JSON dependency"
    foreach ($year in @("2024", "2025", "2026", "2027")) {
        Assert-File (
            Join-Path $bundle "Contents\$year\ru\NavisHelper.resources.dll"
        ) "Installed NavisHelper Russian $year satellite assembly"
    }

    # 2. Same-version reinstall reuses the stored version; the junction target is unchanged.
    $result = Invoke-PackageInstaller
    Assert-InstallSucceeded $result "same-version reinstall"
    $reinstallTarget = Assert-StoreLayout $installRoot $result "same-version reinstall"
    Assert-True ($reinstallTarget -ieq $freshTarget) "same-version reinstall: junction moved from $freshTarget to $reinstallTarget."
    $versionCount = @(Get-VersionDirectories $installRoot).Count
    Assert-True ($versionCount -eq 1) "same-version reinstall: expected one stored version, found $versionCount."

    & $configurator --detect --mcp-server (Join-Path $installRoot "McpServer\NavisHelper.McpServer.exe")
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged MCP configurator detection failed with exit code $LASTEXITCODE."
    }

    # 3. Older ZIP layouts (McpServer-<version>) are preserved untouched.
    $installRoot = Enter-Case "older-zip"
    $olderServers = @(
        (Join-Path $installRoot "McpServer-2.9.0.0"),
        (Join-Path $installRoot "McpServer-$packageVersion")
    )
    foreach ($olderServer in $olderServers) {
        New-LegacyServer $olderServer
    }
    $result = Invoke-PackageInstaller
    Assert-InstallSucceeded $result "older ZIP layout"
    foreach ($olderServer in $olderServers) {
        Assert-File (Join-Path $olderServer "legacy-marker.txt") "older ZIP layout: preserved marker"
        Assert-File (Join-Path $olderServer "NavisHelper.McpServer.exe") "older ZIP layout: preserved server"
    }
    $junction = Join-Path $installRoot "McpServer"
    $target = Get-JunctionTarget $junction
    Assert-True ($null -ne $target) "older ZIP layout: $junction is not a junction."
    Assert-True ((Split-Path $target -Leaf) -match ('^' + [regex]::Escape($packageVersion) + '-[0-9a-f]{8}$')) "older ZIP layout: junction target $target is not a stored version."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $target "legacy-marker.txt"))) "older ZIP layout: junction points at an older copy."

    # 4a. A plain legacy McpServer directory migrates into the store without losing files.
    $installRoot = Enter-Case "legacy"
    $legacy = Join-Path $installRoot "McpServer"
    New-LegacyServer $legacy
    $result = Invoke-PackageInstaller
    Assert-InstallSucceeded $result "legacy migration"
    $target = Get-JunctionTarget $legacy
    Assert-True ($null -ne $target) "legacy migration: $legacy is not a junction."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $target "legacy-marker.txt"))) "legacy migration: junction points at the legacy copy instead of the package version."
    $migrated = @(Get-VersionDirectories $installRoot | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName "legacy-marker.txt")
    })
    Assert-True ($migrated.Count -eq 1) "legacy migration: the legacy server was not kept in the version store."
    Assert-File (Join-Path $migrated[0].FullName "NavisHelper.McpServer.exe") "legacy migration: migrated legacy server"

    # 4b. A running legacy server blocks migration: the installer fails and nothing is lost.
    $installRoot = Enter-Case "legacy-running"
    $legacy = Join-Path $installRoot "McpServer"
    New-LegacyServer $legacy
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $legacy "NavisHelper.McpServer.exe"
    $start.WorkingDirectory = $legacy
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $legacyProcess = [System.Diagnostics.Process]::Start($start)
    Start-Sleep -Seconds 2
    Assert-True (-not $legacyProcess.HasExited) "legacy running: the legacy server exited before the install (exit code $($legacyProcess.ExitCode))."
    $result = Invoke-PackageInstaller
    Assert-True ($result.ExitCode -ne 0) "legacy running: installer succeeded while the legacy server was running."
    Assert-True ($null -eq (Get-JunctionTarget $legacy)) "legacy running: $legacy was replaced by a junction."
    Assert-File (Join-Path $legacy "legacy-marker.txt") "legacy running: preserved marker"
    Assert-File (Join-Path $legacy "NavisHelper.McpServer.exe") "legacy running: preserved server"

    Write-Host "Package install smoke test passed: fresh install, same-version reinstall, older ZIP layout, legacy migration, and a blocked migration while the legacy server runs."
}
finally {
    $keepTestRoot = $false
    if ($null -ne $legacyProcess) {
        # Closing stdin ends the stdio server this test started; no process is stopped.
        if (-not $legacyProcess.HasExited) {
            $legacyProcess.StandardInput.Close()
            if (-not $legacyProcess.WaitForExit(30000)) {
                $keepTestRoot = $true
                Write-Warning "Legacy MCP server started by this test (PID $($legacyProcess.Id)) did not exit after stdin closed; $TestRoot is left in place."
            }
        }
    }

    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], "Process")
    }

    if (-not $keepTestRoot -and (Test-Path -LiteralPath $TestRoot)) {
        # Unlink junctions first so recursive deletion never walks through them.
        Get-ChildItem -LiteralPath $TestRoot -Directory -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue |
            ForEach-Object { [System.IO.Directory]::Delete($_.FullName, $false) }
        Remove-Item -LiteralPath $TestRoot -Recurse -Force
    }
}

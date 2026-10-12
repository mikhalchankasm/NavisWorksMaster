param(
    [string]$IsccPath = "",
    [string]$InstallerScript = "",
    [string]$SourceDir = "",
    [string]$AppVersion = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($InstallerScript)) {
    $InstallerScript = Join-Path $repoRoot "installer\NavisHelper.iss"
}
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    # The installer source tools\build_installer.ps1 leaves behind by default.
    $SourceDir = Join-Path $repoRoot "artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-installer-source"
}
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $IsccPath = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $IsccPath) {
        $fromPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if (-not $fromPath) { throw "ISCC.exe not found. Install Inno Setup 6 or pass -IsccPath." }
        $IsccPath = $fromPath.Source
    }
}
if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw "Installer source directory was not found: $SourceDir. Run tools\build_installer.ps1 first or pass -SourceDir."
}
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    [xml]$packageContents = Get-Content -LiteralPath (Join-Path $SourceDir "NavisHelper.bundle\PackageContents.xml") -Raw
    $AppVersion = [string]$packageContents.ApplicationPackage.AppVersion
}

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Assert-PathMissing([string]$Path, [string]$Description) {
    if (Test-Path -LiteralPath $Path) {
        throw "$Description still exists: $Path"
    }
}

foreach ($requiredPath in @($IsccPath, $InstallerScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required installer smoke input was not found: $requiredPath"
    }
}
if ($AppVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Installer smoke AppVersion must contain four numeric parts: $AppVersion"
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ("NavisHelper-installer-upgrade-smoke-" + [Guid]::NewGuid().ToString("N"))
$bundleRoot = Join-Path $testRoot "bundle\NavisHelper.bundle"
$outputRoot = Join-Path $testRoot "output"
$profileRoot = Join-Path $testRoot "profile"
$outsideMarker = Join-Path $testRoot "outside\keep.marker"
$serverName = "NavisHelper.McpServer"
# GetCustomSetupExitCode in installer\NavisHelper.iss returns this when the server was not activated.
$activationFailedExitCode = 100

function Get-OwnerInstallSnapshot {
    $roots = @(
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'NavisHelper'),
        (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\NavisHelper.bundle'),
        (Join-Path $env:APPDATA 'NavisHelper'),
        (Join-Path $env:LOCALAPPDATA 'NavisHelper')
    )
    foreach ($root in $roots) {
        $root + ':' + (Test-Path -LiteralPath $root)
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName | ForEach-Object {
                $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        }
    }
}

function Assert-OwnerInstallUnchanged([string]$Step) {
    if (Compare-Object $ownerInstallBefore @(Get-OwnerInstallSnapshot)) {
        throw "Isolated installer smoke changed the owner installation during: $Step"
    }
    Assert-File $outsideMarker "Marker outside the isolated install root after $Step"
}

# Mirrors ServerVersionStore.VersionName: <deps.json version>-<first 8 hex of the tree hash>, where the
# tree hash is SHA-256 over "<relative/path>\0<file sha256>\n" for every file, ordinal by path.
function Get-ServerVersionName([string]$Directory) {
    $deps = Get-Content -LiteralPath (Join-Path $Directory "$serverName.deps.json") -Raw
    $versions = @([regex]::Matches($deps, '"NavisHelper\.McpServer/([0-9.]+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($versions.Count -ne 1) { throw "Expected one server version in $Directory, found: $($versions -join ', ')" }
    $root = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $relativePaths = [string[]]@(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($root.Length).Replace('\', '/') })
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $manifest = New-Object System.Text.StringBuilder
    foreach ($relative in $relativePaths) {
        $fileHash = (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$manifest.Append($relative + [char]0 + $fileHash + "`n")
    }
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try { $treeHash = [BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($manifest.ToString()))).Replace('-', '') }
    finally { $sha256.Dispose() }
    return $versions[0] + "-" + $treeHash.Substring(0, 8).ToLowerInvariant()
}

function Get-JunctionTarget([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.LinkType -ne 'Junction') { return $null }
    $target = ([string]@($item.Target)[0]) -replace '^\\\?\?\\', ''
    return [System.IO.Path]::GetFullPath($target).TrimEnd('\')
}

function Start-Server([string]$Executable) {
    $start = New-Object System.Diagnostics.ProcessStartInfo $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $process = [System.Diagnostics.Process]::Start($start)
    [void]$process.StandardError.ReadToEndAsync()
    return $process
}

function Stop-Server([System.Diagnostics.Process]$Process) {
    if (-not $Process.HasExited) {
        $Process.Kill()
        [void]$Process.WaitForExit(10000)
    }
    $Process.Dispose()
}

function Read-JsonRpcResponse([System.Diagnostics.Process]$Process, [int]$Id) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ($true) {
        $remaining = $deadline - [DateTime]::UtcNow
        $read = $Process.StandardOutput.ReadLineAsync()
        if ($remaining -le [TimeSpan]::Zero -or -not $read.Wait($remaining)) { throw "MCP server did not answer request $Id in time." }
        if ($null -eq $read.Result) { throw "MCP server closed stdout before answering request $Id." }
        $message = $read.Result | ConvertFrom-Json
        if ($message.id -eq $Id) { return $message }
    }
}

function Assert-ToolsList([string]$Executable) {
    $process = Start-Server $Executable
    try {
        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"installer-smoke","version":"1.0"}}}')
        $process.StandardInput.Flush()
        if ($null -eq (Read-JsonRpcResponse $process 1).result) { throw "MCP initialize failed through $Executable" }
        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
        $process.StandardInput.Flush()
        $tools = @((Read-JsonRpcResponse $process 2).result.tools)
        if ($tools.Count -eq 0 -or -not ($tools | Where-Object { $_.name -eq 'mcp_health_check' })) {
            throw "tools/list through $Executable returned $($tools.Count) tools without mcp_health_check."
        }
        Write-Host "tools/list through $Executable returned $($tools.Count) tools."
    }
    finally {
        Stop-Server $process
    }
}

# A modal dialog in a silent run never closes on a runner; fail instead of hanging.
function Wait-ProcessTree([string]$FilePath, [string[]]$Arguments, [string]$Name) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle  # Windows PowerShell reports ExitCode only if the handle was opened.
    if (-not $process.WaitForExit(300000)) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        throw "$Name did not finish within 5 minutes (a dialog in a silent run?). Log: $testRoot\$Name.log"
    }
    return $process.ExitCode
}

function Invoke-Installer([string]$AppRoot, [string]$Name) {
    $installerArgs = @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOICONS",
        "/DIR=`"$AppRoot`"",
        "/LOG=`"$(Join-Path $testRoot "$Name.log")`""
    )
    $exitCode = Wait-ProcessTree $smokeInstaller $installerArgs $Name
    Assert-OwnerInstallUnchanged $Name
    return $exitCode
}

function Invoke-Uninstaller([string]$AppRoot, [string]$Name) {
    $uninstaller = Join-Path $AppRoot "unins000.exe"
    Assert-File $uninstaller "Isolated uninstaller"
    $uninstallArgs = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$(Join-Path $testRoot "$Name.log")`"")
    [void](Wait-ProcessTree $uninstaller $uninstallArgs $Name)
    # The uninstaller hands off to a copy of itself in TEMP, which deletes unins000.* last.
    # File.Exists, not Test-Path: a delete-pending file makes Test-Path throw "Access is denied".
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([System.IO.File]::Exists($uninstaller) -or [System.IO.File]::Exists((Join-Path $AppRoot "unins000.dat"))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Uninstaller did not finish in $AppRoot" }
        Start-Sleep -Milliseconds 500
    }
    # The TEMP copy removes the then-empty install root after unins000.*; give it time.
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([System.IO.Directory]::Exists($AppRoot) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    Assert-OwnerInstallUnchanged $Name
    Assert-PathMissing (Join-Path $AppRoot "McpServer") "MCP server junction after uninstall"
    Assert-PathMissing (Join-Path $AppRoot "versions") "MCP version store after uninstall"
    Assert-PathMissing $bundleRoot "Isolated bundle after uninstall"
    if ([System.IO.Directory]::Exists($AppRoot)) {
        $left = @([System.IO.Directory]::GetFileSystemEntries($AppRoot, '*', [System.IO.SearchOption]::AllDirectories))
        if ($left.Count -gt 0) { throw "Uninstall left files in ${AppRoot}: $($left -join '; ')" }
        Write-Host "Note: the empty install root $AppRoot was still present 30 seconds after uninstall."
    }
}

function Assert-ActiveServer([string]$AppRoot, [string]$ExpectedVersionName) {
    $link = Join-Path $AppRoot "McpServer"
    $expected = Join-Path $AppRoot "versions\$ExpectedVersionName"
    $target = Get-JunctionTarget $link
    if ($target -ne $expected) { throw "McpServer must be a junction to $expected; found target '$target'." }
    Assert-PathMissing (Join-Path $AppRoot "McpServerPayload") "Staged MCP server payload"
    Assert-ToolsList (Join-Path $link "$serverName.exe")
}

function Assert-BundleInstalled([string]$AppRoot) {
    Assert-File (Join-Path $bundleRoot "PackageContents.xml") "Installed bundle manifest"
    Assert-File (Join-Path $bundleRoot "Contents\AiWorker\NavisHelper.AiWorker.exe") "Installed OpenRouter AI worker executable"
    Assert-File (Join-Path $bundleRoot "Contents\AiWorker\NavisHelper.AiWorker.runtimeconfig.json") "Installed OpenRouter AI worker runtime configuration"
    Assert-File (Join-Path $bundleRoot "Contents\AiWorker\Newtonsoft.Json.dll") "Installed OpenRouter AI worker JSON dependency"
    foreach ($year in @("2024", "2025", "2026", "2027")) {
        Assert-File (Join-Path $bundleRoot "Contents\$year\NavisHelper.dll") "Installed NavisHelper $year assembly"
        Assert-File (Join-Path $bundleRoot "Contents\$year\NavisHelper.Contracts.dll") "Installed NavisHelper.Contracts $year assembly"
        Assert-File (Join-Path $bundleRoot "Contents\$year\ru\NavisHelper.resources.dll") "Installed NavisHelper Russian $year satellite assembly"
    }
    Assert-File (Join-Path $AppRoot "LICENSE") "Installed project license"
    Assert-File (Join-Path $AppRoot "THIRD-PARTY-NOTICES.md") "Installed third-party notices"
}

$ownerInstallBefore = @(Get-OwnerInstallSnapshot)
# The server writes logs, instance records and scenarios under the user profile; keep them in the test root.
$profileVariables = @{
    NAVISHELPER_MCP_LOG_DIR = (Join-Path $profileRoot "logs")
    NAVISHELPER_INSTANCES_DIR = (Join-Path $profileRoot "instances")
    NAVISHELPER_SCENARIO_DIR = (Join-Path $profileRoot "scenarios")
}
$savedVariables = @{}
foreach ($name in $profileVariables.Keys) {
    $savedVariables[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $profileVariables[$name], 'Process')
}

$legacyServer = $null
try {
    $staleNestedDirectory = Join-Path $bundleRoot "obsolete\nested"
    $staleInteropDirectory = Join-Path $bundleRoot "Contents\2026"
    New-Item -ItemType Directory -Force -Path $staleNestedDirectory, $staleInteropDirectory, $outputRoot, (Split-Path $outsideMarker -Parent) | Out-Null
    [System.IO.File]::WriteAllText($outsideMarker, "must survive install and uninstall")

    $staleMarkers = @(
        (Join-Path $bundleRoot "stale-root.marker"),
        (Join-Path $staleNestedDirectory "stale-nested.marker"),
        (Join-Path $staleInteropDirectory "Autodesk.Navisworks.Interop.ComApi.dll")
    )
    foreach ($marker in $staleMarkers) {
        [System.IO.File]::WriteAllText($marker, "synthetic stale installer-upgrade marker")
    }

    $isccArgs = @(
        "/DSourceDir=$SourceDir",
        "/DAppVersion=$AppVersion",
        "/DBundleInstallDir=$bundleRoot",
        "/DInstallerSmokeTest=1",
        "/O$outputRoot"
    )
    if ($SelfContained) {
        $isccArgs += "/DSelfContained=1"
    }

    & $IsccPath @isccArgs $InstallerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup smoke compiler failed with exit code $LASTEXITCODE."
    }

    $smokeInstaller = Join-Path $outputRoot "NavisHelperSetup-$AppVersion.exe"
    Assert-File $smokeInstaller "Isolated installer smoke executable"
    $newVersionName = Get-ServerVersionName (Join-Path $SourceDir "McpServer")

    # 1. Fresh install, then uninstall.
    $freshRoot = Join-Path $testRoot "fresh"
    $exitCode = Invoke-Installer $freshRoot "fresh-install"
    if ($exitCode -ne 0) { throw "Fresh install failed with exit code $exitCode. Log: $testRoot\fresh-install.log" }
    foreach ($marker in $staleMarkers) {
        Assert-PathMissing $marker "Synthetic stale bundle marker"
    }
    Assert-BundleInstalled $freshRoot
    Assert-ActiveServer $freshRoot $newVersionName
    Invoke-Uninstaller $freshRoot "fresh-uninstall"
    Write-Host "Fresh install and uninstall passed."

    # 2. Upgrade over a legacy real McpServer directory (the 2.10 layout).
    $legacyRoot = Join-Path $testRoot "legacy"
    $legacyDirectory = Join-Path $legacyRoot "McpServer"
    New-Item -ItemType Directory -Force -Path $legacyRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceDir "McpServer") -Destination $legacyDirectory -Recurse
    $legacyDeps = Join-Path $legacyDirectory "$serverName.deps.json"
    $depsText = [System.IO.File]::ReadAllText($legacyDeps) -replace '"NavisHelper\.McpServer/[0-9.]+"', '"NavisHelper.McpServer/2.9.0.0"'
    [System.IO.File]::WriteAllText($legacyDeps, $depsText)
    [System.IO.File]::WriteAllText((Join-Path $legacyDirectory "legacy.marker"), "legacy server directory")
    $legacyVersionName = Get-ServerVersionName $legacyDirectory

    # 2a. A running MCP client holds the legacy directory: activation fails and nothing is lost.
    $legacyServer = Start-Server (Join-Path $legacyDirectory "$serverName.exe")
    Start-Sleep -Seconds 2
    if ($legacyServer.HasExited) { throw "Legacy MCP server exited before the locked upgrade (exit $($legacyServer.ExitCode))." }
    $exitCode = Invoke-Installer $legacyRoot "legacy-locked-install"
    if ($exitCode -ne $activationFailedExitCode) {
        throw "Locked legacy upgrade must exit $activationFailedExitCode; got $exitCode. Log: $testRoot\legacy-locked-install.log"
    }
    if ($null -ne (Get-JunctionTarget $legacyDirectory)) { throw "Locked legacy McpServer must stay a real directory." }
    Assert-File (Join-Path $legacyDirectory "legacy.marker") "Legacy marker after the locked upgrade"
    Assert-File (Join-Path $legacyRoot "McpServerPayload\$serverName.exe") "Kept MCP server payload after the locked upgrade"
    Assert-PathMissing (Join-Path $legacyRoot "versions\$newVersionName") "New server version after the failed activation"
    Stop-Server $legacyServer
    $legacyServer = $null

    # 2b. With the client closed, the same installer migrates the legacy directory.
    $exitCode = Invoke-Installer $legacyRoot "legacy-install"
    if ($exitCode -ne 0) { throw "Legacy upgrade failed with exit code $exitCode. Log: $testRoot\legacy-install.log" }
    Assert-BundleInstalled $legacyRoot
    Assert-ActiveServer $legacyRoot $newVersionName
    Assert-File (Join-Path $legacyRoot "versions\$legacyVersionName\legacy.marker") "Migrated legacy server version"
    $state = Get-Content -LiteralPath (Join-Path $legacyRoot "versions\state.json") -Raw | ConvertFrom-Json
    if ($state.Previous -ne (Join-Path $legacyRoot "versions\$legacyVersionName")) {
        throw "The migrated legacy server must be the rollback slot; state.json Previous is '$($state.Previous)'."
    }
    Invoke-Uninstaller $legacyRoot "legacy-uninstall"
    Write-Host "Legacy upgrade (locked, then migrated) and uninstall passed."

    Write-Host "Installer smoke passed: stale bundle markers removed, McpServer junction active, payload removed, legacy McpServer migrated, uninstall stayed inside the install root."
}
catch {
    # The test root is deleted below; print the Inno Setup logs while they still exist.
    Get-ChildItem -LiteralPath $testRoot -Filter *.log -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "----- $($_.Name) -----"
        Get-Content -LiteralPath $_.FullName | Write-Host
    }
    throw
}
finally {
    if ($null -ne $legacyServer) { Stop-Server $legacyServer }
    foreach ($name in $savedVariables.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedVariables[$name], 'Process')
    }
    $testRootFull = [System.IO.Path]::GetFullPath($testRoot)
    $allowedPrefix = $tempRoot.TrimEnd('\') + '\'
    if (-not $testRootFull.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not ([System.IO.Path]::GetFileName($testRootFull)).StartsWith("NavisHelper-installer-upgrade-smoke-", [System.StringComparison]::Ordinal)) {
        throw "Refusing to remove unsafe installer smoke path: $testRootFull"
    }
    if (Test-Path -LiteralPath $testRootFull) {
        Remove-Item -LiteralPath $testRootFull -Recurse -Force
    }
}

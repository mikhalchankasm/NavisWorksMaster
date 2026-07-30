param(
    [Parameter(Mandatory = $true)]
    [string]$IsccPath,
    [Parameter(Mandatory = $true)]
    [string]$InstallerScript,
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,
    [Parameter(Mandatory = $true)]
    [string]$AppVersion,
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Assert-PathMissing([string]$Path, [string]$Description) {
    if (Test-Path -LiteralPath $Path) {
        throw "$Description was not removed during installer upgrade: $Path"
    }
}

foreach ($requiredPath in @($IsccPath, $InstallerScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required installer smoke input was not found: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw "Installer source directory was not found: $SourceDir"
}
if ($AppVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Installer smoke AppVersion must contain four numeric parts: $AppVersion"
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ("NavisHelper-installer-upgrade-smoke-" + [Guid]::NewGuid().ToString("N"))
$bundleRoot = Join-Path $testRoot "bundle\NavisHelper.bundle"
$appRoot = Join-Path $testRoot "app"
$outputRoot = Join-Path $testRoot "output"
$logPath = Join-Path $testRoot "installer.log"

try {
    $staleNestedDirectory = Join-Path $bundleRoot "obsolete\nested"
    $staleInteropDirectory = Join-Path $bundleRoot "Contents\2026"
    New-Item -ItemType Directory -Force -Path $staleNestedDirectory, $staleInteropDirectory, $appRoot, $outputRoot | Out-Null

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

    $installerArgs = @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOICONS",
        "/DIR=`"$appRoot`"",
        "/LOG=`"$logPath`""
    )
    $installerProcess = Start-Process `
        -FilePath $smokeInstaller `
        -ArgumentList $installerArgs `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($installerProcess.ExitCode -ne 0) {
        throw "Isolated installer upgrade smoke failed with exit code $($installerProcess.ExitCode). Log: $logPath"
    }

    foreach ($marker in $staleMarkers) {
        Assert-PathMissing $marker "Synthetic stale bundle marker"
    }

    Assert-File (Join-Path $bundleRoot "PackageContents.xml") "Installed bundle manifest"
    foreach ($year in @("2024", "2025", "2026", "2027")) {
        Assert-File (Join-Path $bundleRoot "Contents\$year\NavisHelper.dll") "Installed NavisHelper $year assembly"
        Assert-File (Join-Path $bundleRoot "Contents\$year\NavisHelper.Contracts.dll") "Installed NavisHelper.Contracts $year assembly"
        Assert-File (Join-Path $bundleRoot "Contents\$year\ru\NavisHelper.resources.dll") "Installed NavisHelper Russian $year satellite assembly"
    }
    Assert-File (Join-Path $appRoot "LICENSE") "Installed project license"
    Assert-File (Join-Path $appRoot "THIRD-PARTY-NOTICES.md") "Installed third-party notices"

    Write-Host "Installer upgrade cleanup smoke passed: stale root, nested, and Autodesk Interop markers were removed before the fresh bundle was installed."
}
finally {
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

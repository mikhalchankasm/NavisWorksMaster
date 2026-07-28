param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [string]$AppVersion = "2.8.9.0",
    [string]$PackageName = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\installer"
}

function Resolve-ISCC {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $fromPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

function Get-BundleVersion([string]$BundleDirectory) {
    $packageContents = Join-Path $BundleDirectory "PackageContents.xml"
    if (-not (Test-Path -LiteralPath $packageContents -PathType Leaf)) {
        throw "Bundle manifest was not found: $packageContents"
    }

    [xml]$xml = Get-Content -LiteralPath $packageContents -Raw
    $version = [string]$xml.ApplicationPackage.AppVersion
    if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Distribution package bundle version is invalid: '$version'"
    }

    return $version
}

$sourceBundleVersion = Get-BundleVersion (Join-Path $repoRoot "NavisHelper.bundle")
if ($AppVersion -ne $sourceBundleVersion) {
    throw "Installer AppVersion '$AppVersion' must match source bundle version '$sourceBundleVersion'."
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $modeName = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
    $PackageName = "NavisHelper-full-$Runtime-$modeName-installer-source"
}

$distributionRoot = Join-Path $repoRoot "artifacts\distribution"
$packageScript = Join-Path $repoRoot "tools\package_distribution.ps1"
$packageArgs = @{
    Runtime = $Runtime
    PackageName = $PackageName
    OutputRoot = $distributionRoot
}
if ($SelfContained) {
    $packageArgs.SelfContained = $true
}

& $packageScript @packageArgs

$sourceDir = Join-Path $distributionRoot $PackageName
if (-not (Test-Path -LiteralPath $sourceDir)) {
    throw "Distribution package directory not found: $sourceDir"
}

$bundleVersion = Get-BundleVersion (Join-Path $sourceDir "NavisHelper.bundle")
if ($AppVersion -ne $bundleVersion) {
    throw "Installer AppVersion '$AppVersion' must match distribution bundle version '$bundleVersion'."
}

$validationScript = Join-Path $repoRoot "tools\validate_distribution.ps1"
& $validationScript -PackagePath $sourceDir -ZipPath (Join-Path $distributionRoot ($PackageName + ".zip"))

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$iscc = Resolve-ISCC
$script = Join-Path $repoRoot "installer\NavisHelper.iss"
$isccArgs = @("/DSourceDir=$sourceDir", "/DAppVersion=$AppVersion", "/O$OutputRoot")
if ($SelfContained) {
    $isccArgs += "/DSelfContained=1"
}

& $iscc @isccArgs $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

$installerUpgradeSmoke = Join-Path $repoRoot "scripts\test_installer_upgrade.ps1"
if (-not (Test-Path -LiteralPath $installerUpgradeSmoke -PathType Leaf)) {
    throw "Installer upgrade smoke test was not found: $installerUpgradeSmoke"
}
$installerUpgradeSmokeArgs = @{
    IsccPath = $iscc
    InstallerScript = $script
    SourceDir = $sourceDir
    AppVersion = $AppVersion
}
if ($SelfContained) {
    $installerUpgradeSmokeArgs.SelfContained = $true
}
& $installerUpgradeSmoke @installerUpgradeSmokeArgs

Write-Host "Installer output: $OutputRoot"

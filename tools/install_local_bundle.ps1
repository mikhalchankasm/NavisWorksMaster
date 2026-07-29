param(
    [string]$SourceBundle = "",
    [switch]$SkipHashCheck
)

$ErrorActionPreference = "Stop"

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-DirectoryCopySafe([string]$Source, [string]$Destination) {
    $sourceFull = Get-FullPath $Source
    $destinationFull = Get-FullPath $Destination
    $sourceTrimmed = $sourceFull.TrimEnd('\')
    $destinationTrimmed = $destinationFull.TrimEnd('\')

    if ($sourceTrimmed.Equals($destinationTrimmed, [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourceTrimmed.StartsWith($destinationTrimmed + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $destinationTrimmed.StartsWith($sourceTrimmed + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to copy '$Source' to '$Destination' because one path contains the other."
    }
}

function Assert-UnderAllowedRoot([string]$Destination, [string[]]$AllowedRoots) {
    $destinationFull = Get-FullPath $Destination
    foreach ($root in $AllowedRoots) {
        $rootFull = (Get-FullPath $root).TrimEnd('\') + '\'
        if (($destinationFull.TrimEnd('\') + '\').StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    throw "Refusing to install outside Autodesk ApplicationPlugins roots: $destinationFull"
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

function Copy-DirectoryFresh([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Source bundle was not found: $Source"
    }

    Assert-DirectoryCopySafe $Source $Destination
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination (Split-Path -Parent $Destination) -Recurse -Force
}

function Assert-BundleHashesMatch([string]$Source, [string]$Destination) {
    $sourceFiles = @(Get-ChildItem -LiteralPath $Source -Recurse -File | Where-Object {
        $_.Name -in @(
            "PackageContents.xml",
            "NavisHelper.dll",
            "NavisHelper.Contracts.dll",
            "NavisHelper.resources.dll"
        )
    })

    foreach ($sourceFile in $sourceFiles) {
        $relative = $sourceFile.FullName.Substring((Get-FullPath $Source).TrimEnd('\').Length).TrimStart('\')
        $destinationFile = Join-Path $Destination $relative
        if (-not (Test-Path -LiteralPath $destinationFile -PathType Leaf)) {
            throw "Installed bundle is missing expected file: $destinationFile"
        }

        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($destinationHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Installed bundle hash mismatch: $relative"
        }
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($SourceBundle)) {
    $SourceBundle = Join-Path $repoRoot "NavisHelper.bundle"
}

$userRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$destinationRoot = $userRoot
$destinationBundle = Join-Path $destinationRoot "NavisHelper.bundle"

Assert-UnderAllowedRoot $destinationBundle @($userRoot)
Assert-NavisworksClosed
foreach ($year in @("2024", "2025", "2026", "2027")) {
    $satellitePath = Join-Path $SourceBundle "Contents\$year\ru\NavisHelper.resources.dll"
    if (-not (Test-Path -LiteralPath $satellitePath -PathType Leaf)) {
        throw "Source bundle is missing the Russian $year satellite assembly: $satellitePath"
    }
}
Copy-DirectoryFresh $SourceBundle $destinationBundle

if (-not $SkipHashCheck) {
    Assert-BundleHashesMatch $SourceBundle $destinationBundle
}

Write-Host "Installed NavisHelper bundle to $destinationBundle"
Write-Host "Per-user dev install complete."

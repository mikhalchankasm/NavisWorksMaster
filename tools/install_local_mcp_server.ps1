param(
    [string]$Source = "",
    [string]$Destination = "",
    [switch]$SkipVersionCheck
)

$ErrorActionPreference = "Stop"

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-DirectoryCopySafe([string]$SourcePath, [string]$DestinationPath) {
    $sourceFull = (Get-FullPath $SourcePath).TrimEnd('\')
    $destinationFull = (Get-FullPath $DestinationPath).TrimEnd('\')

    if ($sourceFull.Equals($destinationFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourceFull.StartsWith($destinationFull + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $destinationFull.StartsWith($sourceFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to copy '$SourcePath' to '$DestinationPath' because one path contains the other."
    }
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

function Get-ProcessesFromDirectory([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    $directoryFull = (Get-FullPath $Directory).TrimEnd('\') + '\'
    $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
                $_.Path.StartsWith($directoryFull, [System.StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    })

    return $processes
}

function Copy-DirectoryContentsFresh([string]$SourcePath, [string]$DestinationPath) {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
        throw "MCP server source directory was not found: $SourcePath"
    }

    Assert-DirectoryCopySafe $SourcePath $DestinationPath
    Assert-DestinationAllowed $DestinationPath

    $destinationParent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null

    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $DestinationPath -Recurse -Force
}

function Assert-VersionMatches([string]$SourcePath, [string]$DestinationPath) {
    $sourceDll = Join-Path $SourcePath "NavisHelper.McpServer.dll"
    $destinationDll = Join-Path $DestinationPath "NavisHelper.McpServer.dll"

    if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
        throw "Source MCP server assembly was not found: $sourceDll"
    }

    if (-not (Test-Path -LiteralPath $destinationDll -PathType Leaf)) {
        throw "Installed MCP server assembly was not found: $destinationDll"
    }

    $sourceVersion = [System.Reflection.AssemblyName]::GetAssemblyName($sourceDll).Version.ToString()
    $destinationVersion = [System.Reflection.AssemblyName]::GetAssemblyName($destinationDll).Version.ToString()
    if (-not $sourceVersion.Equals($destinationVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed MCP server version mismatch. Source=$sourceVersion Destination=$destinationVersion"
    }
}

function Get-ServerVersion([string]$SourcePath) {
    $sourceDll = Join-Path $SourcePath "NavisHelper.McpServer.dll"
    if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
        throw "Source MCP server assembly was not found: $sourceDll"
    }

    return [System.Reflection.AssemblyName]::GetAssemblyName($sourceDll).Version.ToString()
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $repoRoot "NavisHelper.McpServer\bin\Release\net9.0"
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $version = Get-ServerVersion $Source
    $Destination = Join-Path $env:LOCALAPPDATA ("NavisHelper\McpServer-" + $version)
}

$sourceFull = Get-FullPath $Source
$destinationFull = Get-FullPath $Destination

$runningProcesses = Get-ProcessesFromDirectory $destinationFull
if ($runningProcesses.Count -gt 0) {
    $ids = ($runningProcesses | Select-Object -ExpandProperty Id) -join ", "
    throw "MCP server process(es) already run from '$destinationFull' (PID: $ids). Restart the MCP client before reinstalling this same version; a different version installs to its own directory without stopping the active session."
}

Copy-DirectoryContentsFresh $sourceFull $destinationFull

if (-not $SkipVersionCheck) {
    Assert-VersionMatches $sourceFull $destinationFull
}

Write-Host "Installed NavisHelper MCP server to $destinationFull"

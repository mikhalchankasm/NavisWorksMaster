param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$ZipPath = ""
)

$ErrorActionPreference = "Stop"

function Get-NormalizedRelativePath([string]$RootPath, [string]$FilePath) {
    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $file = [System.IO.Path]::GetFullPath($FilePath)
    if (-not $file.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "File is outside distribution package: $file"
    }

    return $file.Substring($root.Length).Replace('\', '/')
}

function Get-Sha256FromStream([System.IO.Stream]$Stream) {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($Stream)
        return ([System.BitConverter]::ToString($hash)).Replace('-', '')
    } finally {
        $sha256.Dispose()
    }
}

function Get-ChecksumEntries([string]$ChecksumPath) {
    if (-not (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)) {
        throw "Distribution checksum file is missing: $ChecksumPath"
    }

    $entries = @{}
    foreach ($line in Get-Content -LiteralPath $ChecksumPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $match = [regex]::Match($line, '^([A-Fa-f0-9]{64}) \*(.+)$')
        if (-not $match.Success) {
            throw "Invalid checksum entry: $line"
        }

        $relativePath = $match.Groups[2].Value.Replace('\', '/')
        if ($entries.ContainsKey($relativePath)) {
            throw "Duplicate checksum entry: $relativePath"
        }
        $entries[$relativePath] = $match.Groups[1].Value.ToUpperInvariant()
    }

    if ($entries.Count -eq 0) {
        throw "Distribution checksum file contains no entries: $ChecksumPath"
    }

    return $entries
}

$packageRoot = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "Distribution package directory was not found: $packageRoot"
}

$requiredFiles = @(
    "Install-NavisHelperBundle.ps1",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md",
    "licenses/WindowsCommunityToolkit-7.1.3-LICENSE.md",
    "licenses/Newtonsoft.Json-13.0.3-LICENSE.md",
    "licenses/System.ValueTuple-4.5.0-LICENSE.TXT",
    "licenses/System.ValueTuple-4.5.0-THIRD-PARTY-NOTICES.TXT",
    "licenses/ModelContextProtocol-1.2.0-LICENSE",
    "licenses/ModelContextProtocol-1.2.0-THIRD-PARTY-NOTICES.txt",
    "licenses/dotnet-extensions-10.4.1-LICENSE",
    "licenses/dotnet-extensions-10.4.1-THIRD-PARTY-NOTICES.TXT",
    "licenses/dotnet-runtime-8.0.x-LICENSE.TXT",
    "licenses/dotnet-runtime-8.0.0-THIRD-PARTY-NOTICES.TXT",
    "licenses/dotnet-runtime-8.0.1-8.0.2-THIRD-PARTY-NOTICES.TXT",
    "licenses/dotnet-dotnet-10.0.4-10.0.5-LICENSE.TXT",
    "licenses/dotnet-dotnet-10.0.4-10.0.5-THIRD-PARTY-NOTICES.TXT",
    "licenses/dotnet-runtime-9.0.18-LICENSE.TXT",
    "licenses/dotnet-runtime-9.0.18-THIRD-PARTY-NOTICES.TXT",
    "licenses/Microsoft-DotNet-Library-License.html",
    "manifest.json",
    "mcp-client-config.example.json",
    "NavisHelper.bundle/PackageContents.xml",
    "NavisHelper.bundle/Contents/2024/NavisHelper.dll",
    "NavisHelper.bundle/Contents/2024/NavisHelper.Contracts.dll",
    "NavisHelper.bundle/Contents/2024/ru/NavisHelper.resources.dll",
    "NavisHelper.bundle/Contents/2025/NavisHelper.dll",
    "NavisHelper.bundle/Contents/2025/NavisHelper.Contracts.dll",
    "NavisHelper.bundle/Contents/2025/ru/NavisHelper.resources.dll",
    "NavisHelper.bundle/Contents/2026/NavisHelper.dll",
    "NavisHelper.bundle/Contents/2026/NavisHelper.Contracts.dll",
    "NavisHelper.bundle/Contents/2026/ru/NavisHelper.resources.dll",
    "NavisHelper.bundle/Contents/2027/NavisHelper.dll",
    "NavisHelper.bundle/Contents/2027/NavisHelper.Contracts.dll",
    "NavisHelper.bundle/Contents/2027/ru/NavisHelper.resources.dll",
    "NavisHelper.bundle/Contents/AiWorker/NavisHelper.AiWorker.exe",
    "NavisHelper.bundle/Contents/AiWorker/NavisHelper.AiWorker.dll",
    "NavisHelper.bundle/Contents/AiWorker/NavisHelper.AiWorker.deps.json",
    "NavisHelper.bundle/Contents/AiWorker/NavisHelper.AiWorker.runtimeconfig.json",
    "NavisHelper.bundle/Contents/AiWorker/Newtonsoft.Json.dll",
    "McpServer/NavisHelper.McpServer.exe",
    "McpServer/NavisHelper.McpServer.dll",
    "McpConfigurator/NavisHelper.McpConfigurator.exe",
    "McpConfigurator/NavisHelper.McpConfigurator.dll"
)
foreach ($requiredFile in $requiredFiles) {
    $fullPath = Join-Path $packageRoot $requiredFile
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Distribution package is missing required file: $requiredFile"
    }
}

$unexpectedSymbols = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter "*.pdb")
if ($unexpectedSymbols.Count -gt 0) {
    throw "Distribution package contains debug symbols: $($unexpectedSymbols.FullName -join ', ')"
}

$leakedDevArtifacts = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter "NavisHelper.Dev.*")
if ($leakedDevArtifacts.Count -gt 0) {
    throw "Distribution package contains NavisHelper.Dev artifacts: $($leakedDevArtifacts.FullName -join ', ')"
}

$leakedAutodeskInterop = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot "NavisHelper.bundle\Contents") -Recurse -File -Filter "Autodesk.Navisworks.Interop.ComApi.dll")
if ($leakedAutodeskInterop.Count -gt 0) {
    throw "Distribution package contains a non-redistributable Autodesk Interop assembly: $($leakedAutodeskInterop.FullName -join ', ')"
}

[xml]$packageXml = Get-Content -LiteralPath (Join-Path $packageRoot "NavisHelper.bundle\PackageContents.xml") -Raw
$bundleVersion = [string]$packageXml.ApplicationPackage.AppVersion
if ($bundleVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Bundle AppVersion must be a four-part version: '$bundleVersion'"
}

$manifest = Get-Content -LiteralPath (Join-Path $packageRoot "manifest.json") -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$manifest.package_name) -or [string]::IsNullOrWhiteSpace([string]$manifest.runtime)) {
    throw "Distribution manifest must include package_name and runtime."
}
if ($null -eq $manifest.mcp_server -or $null -eq $manifest.mcp_server.framework_dependent) {
    throw "Distribution manifest must declare mcp_server.framework_dependent."
}
if ($null -eq $manifest.ai_worker -or
    $null -eq $manifest.ai_worker.framework_dependent -or
    [int]$manifest.ai_worker.protocol_version -ne 3) {
    throw "Distribution manifest must declare ai_worker framework mode and protocol version 3."
}

$serverVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $packageRoot "McpServer\NavisHelper.McpServer.dll")).Version.ToString()
$configuratorVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $packageRoot "McpConfigurator\NavisHelper.McpConfigurator.dll")).Version.ToString()
$workerVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $packageRoot "NavisHelper.bundle\Contents\AiWorker\NavisHelper.AiWorker.dll")).Version.ToString()
if ($serverVersion -ne $bundleVersion -or $configuratorVersion -ne $bundleVersion -or $workerVersion -ne $bundleVersion) {
    throw "Distribution version mismatch. Bundle=$bundleVersion McpServer=$serverVersion McpConfigurator=$configuratorVersion AiWorker=$workerVersion"
}

$checksumEntries = Get-ChecksumEntries (Join-Path $packageRoot "checksums.sha256")
$packageFiles = @{}
Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Name -ne "checksums.sha256" } | ForEach-Object {
    $relativePath = Get-NormalizedRelativePath $packageRoot $_.FullName
    $packageFiles[$relativePath] = $_.FullName
}
if ($checksumEntries.Count -ne $packageFiles.Count) {
    throw "Checksum entry count does not match package files. Checksums=$($checksumEntries.Count) Files=$($packageFiles.Count)"
}
foreach ($relativePath in $packageFiles.Keys) {
    if (-not $checksumEntries.ContainsKey($relativePath)) {
        throw "Checksum is missing package file: $relativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $packageFiles[$relativePath] -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne $checksumEntries[$relativePath]) {
        throw "Checksum mismatch for package file: $relativePath"
    }
}

if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
    $zipFullPath = [System.IO.Path]::GetFullPath($ZipPath)
    if (-not (Test-Path -LiteralPath $zipFullPath -PathType Leaf)) {
        throw "Distribution ZIP was not found: $zipFullPath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipFullPath)
    try {
        $zipEntries = @{}
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrWhiteSpace($entry.Name)) {
                continue
            }

            $entryPath = $entry.FullName.Replace('\', '/')
            if ($entryPath -eq "checksums.sha256") {
                continue
            }
            if ($zipEntries.ContainsKey($entryPath)) {
                throw "ZIP contains duplicate entry: $entryPath"
            }
            $zipEntries[$entryPath] = $entry
        }

        if ($zipEntries.Count -ne $checksumEntries.Count) {
            throw "ZIP entry count does not match package checksums. ZIP=$($zipEntries.Count) Checksums=$($checksumEntries.Count)"
        }
        foreach ($relativePath in $checksumEntries.Keys) {
            if (-not $zipEntries.ContainsKey($relativePath)) {
                throw "ZIP is missing package file: $relativePath"
            }

            $stream = $zipEntries[$relativePath].Open()
            try {
                $actualHash = Get-Sha256FromStream $stream
            } finally {
                $stream.Dispose()
            }
            if ($actualHash -ne $checksumEntries[$relativePath]) {
                throw "ZIP checksum mismatch for package file: $relativePath"
            }
        }
    } finally {
        $archive.Dispose()
    }
}

Write-Host "Validated NavisHelper distribution ${bundleVersion}: $packageRoot"

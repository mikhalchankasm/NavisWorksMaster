param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$PackageRoot,
    [string]$InstalledRoot,
    [string]$BundleRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'archive.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($catalog.entries.Count -ne 319) { throw 'Unexpected archive entry count' }
function Safe-Path([string]$root, [string]$relative) {
    $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive path escapes its root' }
    return $full
}
if (-not $PackageRoot) {
    if (-not $InstalledRoot -or -not $BundleRoot) { throw 'Supply the installed payload roots' }
    $PackageRoot = Join-Path (Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent) 'recovered-package'
    if (Test-Path -LiteralPath $PackageRoot) { throw 'Recovery staging directory already exists' }
    New-Item -ItemType Directory -Path $PackageRoot | Out-Null
    $supplement = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'supplement.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($supplement).Count -ne 2) { throw 'Expected exactly two supplemental files' }
    foreach ($file in $supplement) {
        if ($file.name -notin @('Install-NavisHelperBundle.ps1','checksums.sha256')) { throw 'Unexpected supplemental file' }
        [IO.File]::WriteAllBytes((Safe-Path $PackageRoot $file.name), [Convert]::FromBase64String($file.base64))
    }
    foreach ($record in $catalog.entries) {
        $target = Safe-Path $PackageRoot $record.name
        if ($record.name.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
        if (Test-Path -LiteralPath $target) { continue }
        $source = if ($record.name.StartsWith('NavisHelper.bundle/')) {
            Safe-Path $BundleRoot $record.name.Substring('NavisHelper.bundle/'.Length)
        } else { Safe-Path $InstalledRoot $record.name }
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
}
foreach ($record in $catalog.entries) {
    if ($record.name.EndsWith('/')) { continue }
    $file = Get-Item -LiteralPath (Safe-Path $PackageRoot $record.name)
    if ($file.Length -ne $record.size -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ine $record.sha256) {
        throw ('Payload mismatch: ' + $record.name)
    }
}
if (Test-Path -LiteralPath $OutputPath) { throw 'Refusing to overwrite an existing archive' }
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($record in $catalog.entries) {
        # Windows PowerShell Compress-Archive stores backslashes in the ZIP headers.
        $entry = $archive.CreateEntry($record.name.Replace('/', '\'), [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::ParseExact($record.time, 'yyyy-MM-ddTHH:mm:ss', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
        if ($record.attributes -ne 0) { throw 'Unsupported nonzero archive attributes' }
        if ($record.name.EndsWith('/')) { continue }
        $reader = [IO.File]::OpenRead((Safe-Path $PackageRoot $record.name))
        $output = $entry.Open()
        try { $reader.CopyTo($output) } finally { $output.Dispose(); $reader.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
Write-Output "reconstructed_sha256=$hash"
if ($hash -ine $catalog.sha256) { throw 'Reconstructed ZIP is not byte-identical to the validated release' }
Write-Output 'all_319_entries_verified_and_original_zip_reproduced=true'

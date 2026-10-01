$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot 'tools/package_distribution.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) { throw $parseErrors[0] }
foreach ($name in @('Get-FullPath', 'Remove-DirectorySafely')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    Invoke-Expression $function.Extent.Text
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('NavisHelper-safety-' + [Guid]::NewGuid().ToString('N'))
$allowed = Join-Path $fixture 'output'
$sibling = Join-Path $fixture 'output-old'
try {
    New-Item -ItemType Directory -Path $allowed, $sibling | Out-Null
    foreach ($unsafe in @($allowed, ($allowed + '\'), $sibling, $fixture)) {
        $rejected = $false
        try { Remove-DirectorySafely $unsafe $allowed } catch { $rejected = $_.Exception.Message -like 'Refusing to delete outside output root:*' }
        if (-not $rejected -or -not (Test-Path -LiteralPath $unsafe)) { throw "Unsafe deletion accepted: $unsafe" }
    }
    $child = Join-Path $allowed 'child'
    New-Item -ItemType Directory -Path $child | Out-Null
    Remove-DirectorySafely $child $allowed
    if (Test-Path -LiteralPath $child) { throw 'Safe child was not removed.' }
    $zip = Join-Path $fixture 'dummy.zip'
    New-Item -ItemType File -Path $zip | Out-Null
    foreach ($unsafe in @($fixture, (Join-Path $repoRoot 'NavisHelper-package-smoke-unsafe'))) {
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'test_package_install.ps1') -ZipPath $zip -TestRoot $unsafe }
        catch { $rejected = $_.Exception.Message -like 'TestRoot must be a new*' }
        if (-not $rejected) { throw "Unsafe smoke root accepted: $unsafe" }
    }
    Write-Host 'Distribution safety checks passed: root/sibling deletion refused; safe child removed; unsafe smoke roots refused.'
} finally {
    $full = [IO.Path]::GetFullPath($fixture)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notlike 'NavisHelper-safety-*') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

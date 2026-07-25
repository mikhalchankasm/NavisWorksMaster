param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [ValidateSet("2024", "2025", "2026", "2027")]
    [string]$Version = "2027",

    [switch]$PassThru,

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$roamerPath = "C:\Program Files\Autodesk\Navisworks Manage $Version\Roamer.exe"

if (-not (Test-Path -LiteralPath $roamerPath)) {
    throw "Navisworks Manage $Version was not found at '$roamerPath'."
}

$resolvedFile = Resolve-Path -LiteralPath $FilePath -ErrorAction Stop
$quotedFile = '"' + $resolvedFile.Path + '"'

if ($DryRun) {
    [pscustomobject]@{
        RoamerPath = $roamerPath
        ArgumentList = $quotedFile
    }
    return
}

$process = Start-Process -FilePath $roamerPath -ArgumentList $quotedFile -PassThru

if ($PassThru) {
    $process
}

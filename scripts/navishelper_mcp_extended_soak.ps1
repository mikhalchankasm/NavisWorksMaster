param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [ValidateSet("2024", "2025", "2026", "2027")]
    [string]$Version = "2027",

    [int]$Cycles = 5,

    [int]$StressCount = 100,

    [int]$MixedStressCount = 100,

    [int]$MemoryCheckpointInterval = 25,

    [double]$MaxMemoryDeltaMb = 512,

    [string]$ReportDirectory = "",

    [int]$StressDelayMs = 10,

    [string]$SummaryJsonFile = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$soakScript = Join-Path $scriptDir "navishelper_mcp_soak.ps1"

if ([string]::IsNullOrWhiteSpace($ReportDirectory)) {
    $ReportDirectory = Join-Path (Join-Path $scriptDir "..\artifacts\mcp-soak") ("extended-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonFile)) {
    $SummaryJsonFile = Join-Path $ReportDirectory "summary.json"
}

& $soakScript `
    -FilePath $FilePath `
    -Version $Version `
    -Cycles $Cycles `
    -StressCount $StressCount `
    -MixedStressCount $MixedStressCount `
    -MemoryCheckpointInterval $MemoryCheckpointInterval `
    -MaxMemoryDeltaMb $MaxMemoryDeltaMb `
    -ReportDirectory $ReportDirectory `
    -StressDelayMs $StressDelayMs `
    -SummaryJsonFile $SummaryJsonFile

exit $LASTEXITCODE

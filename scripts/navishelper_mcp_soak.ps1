param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [ValidateSet("2024", "2025", "2026", "2027")]
    [string]$Version = "2027",

    [int]$Cycles = 3,

    [int]$StressCount = 20,

    [int]$MixedStressCount = 0,

    [int]$MemoryCheckpointInterval = 25,

    [double]$MaxMemoryDeltaMb = 0,

    [string]$ReportDirectory = "",

    [int]$StressDelayMs = 25,

    [string]$SummaryJsonFile = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$regressionScript = Join-Path $scriptDir "navishelper_mcp_regression.ps1"

if ($Cycles -lt 1) {
    throw "Cycles must be at least 1."
}

$results = @()
$startedAt = Get-Date

for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    Write-Host ""
    Write-Host "== Soak cycle $cycle/$Cycles =="

    $cycleStartedAt = Get-Date
    try {
        $regressionArgs = @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $regressionScript,
            "-FilePath",
            $FilePath,
            "-Version",
            $Version,
            "-StressCount",
            $StressCount,
            "-MixedStressCount",
            $MixedStressCount,
            "-MemoryCheckpointInterval",
            $MemoryCheckpointInterval,
            "-MaxMemoryDeltaMb",
            $MaxMemoryDeltaMb,
            "-StressDelayMs",
            $StressDelayMs
        )
        if (-not [string]::IsNullOrWhiteSpace($ReportDirectory)) {
            $regressionArgs += "-ReportDirectory"
            $regressionArgs += $ReportDirectory
        }

        powershell @regressionArgs

        if ($LASTEXITCODE -ne 0) {
            throw "navishelper_mcp_regression.ps1 failed with exit code $LASTEXITCODE."
        }

        $results += [pscustomobject]@{
            Cycle = $cycle
            Ok = $true
            ElapsedSeconds = [Math]::Round(((Get-Date) - $cycleStartedAt).TotalSeconds, 1)
            Error = $null
        }
    }
    catch {
        $results += [pscustomobject]@{
            Cycle = $cycle
            Ok = $false
            ElapsedSeconds = [Math]::Round(((Get-Date) - $cycleStartedAt).TotalSeconds, 1)
            Error = $_.Exception.Message
        }

        break
    }
}

$okCount = @($results | Where-Object { $_.Ok }).Count
$summary = [pscustomobject]@{
    Ok = ($okCount -eq $Cycles)
    Version = $Version
    CyclesRequested = $Cycles
    CyclesCompleted = $results.Count
    OkCount = $okCount
    ErrorCount = $results.Count - $okCount
    StressCount = $StressCount
    MixedStressCount = $MixedStressCount
    MemoryCheckpointInterval = $MemoryCheckpointInterval
    MaxMemoryDeltaMb = $MaxMemoryDeltaMb
    TotalElapsedSeconds = [Math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
    Results = $results
}

Write-Host ""
Write-Host "== Soak Summary =="
$summary | ConvertTo-Json -Depth 5

if (-not [string]::IsNullOrWhiteSpace($SummaryJsonFile)) {
    $summaryPath = [System.IO.Path]::GetFullPath($SummaryJsonFile)
    $summaryDirectory = Split-Path -Parent $summaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDirectory) -and -not (Test-Path -LiteralPath $summaryDirectory)) {
        New-Item -ItemType Directory -Path $summaryDirectory -Force | Out-Null
    }

    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host "Soak summary: $summaryPath"
}

if (-not $summary.Ok) {
    exit 1
}

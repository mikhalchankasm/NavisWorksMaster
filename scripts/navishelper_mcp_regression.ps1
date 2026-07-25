param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [ValidateSet("2024", "2025", "2026", "2027")]
    [string]$Version = "2027",

    [int]$WaitTimeoutSec = 360,

    [int]$StressCount = 20,

    [int]$StressDelayMs = 25,

    [int]$MixedStressCount = 0,

    [int]$MemoryCheckpointInterval = 25,

    [double]$MaxMemoryDeltaMb = 0,

    [string]$ReportDirectory = "",

    [switch]$SkipStress,

    [switch]$SkipMixedStress,

    [switch]$KeepNavisworksOpen,

    [switch]$UseExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

function Get-InstancesDirectory {
    $configured = [Environment]::GetEnvironmentVariable("NAVISHELPER_INSTANCES_DIR")
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        return $configured
    }

    return Join-Path $env:LOCALAPPDATA "NavisHelper\Mcp\instances"
}

function Test-ProcessAlive {
    param([int]$ProcessId)

    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        return -not $process.HasExited
    }
    catch {
        return $false
    }
}

function Read-DiscoveryRecords {
    param([string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    $records = @()
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter *.json -File) {
        try {
            $record = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($null -eq $record) {
                continue
            }

            if (-not (Test-ProcessAlive -ProcessId ([int]$record.pid))) {
                continue
            }

            $record | Add-Member -NotePropertyName "__file_path" -NotePropertyValue $file.FullName -Force
            $record | Add-Member -NotePropertyName "__last_write_time" -NotePropertyValue $file.LastWriteTimeUtc -Force
            $records += $record
        }
        catch {
            continue
        }
    }

    return $records
}

function Wait-NavisHelperInstance {
    param(
        [int]$ProcessId,
        [string]$TargetVersion,
        [int]$TimeoutSec
    )

    $instancesDirectory = Get-InstancesDirectory
    $deadline = (Get-Date).AddSeconds($TimeoutSec)

    while ((Get-Date) -lt $deadline) {
        $records = @(Read-DiscoveryRecords -Directory $instancesDirectory)

        if ($ProcessId -gt 0) {
            $records = @($records | Where-Object { [int]$_.pid -eq $ProcessId })
        }
        elseif (-not [string]::IsNullOrWhiteSpace($TargetVersion)) {
            $records = @($records | Where-Object { $_.navisworks_version -eq $TargetVersion })
        }

        $records = @($records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.document_title) } | Sort-Object __last_write_time -Descending)
        if ($records.Count -gt 0) {
            return $records[0]
        }

        if ($ProcessId -gt 0 -and -not (Test-ProcessAlive -ProcessId $ProcessId)) {
            throw "Navisworks process $ProcessId exited before NavisHelper MCP host became ready."
        }

        Start-Sleep -Seconds 5
    }

    throw "Timed out after $TimeoutSec seconds waiting for NavisHelper MCP host."
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Body
    )

    Write-Host ""
    Write-Host "== $Name =="
    & $Body
}

function Close-NavisworksProcess {
    param([int]$ProcessId)

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    $sent = $process.CloseMainWindow()
    Start-Sleep -Seconds 12
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    throw "Navisworks process $ProcessId is still running after CloseMainWindow. CloseSignalSent=$sent. Window='$($process.MainWindowTitle)'."
}

$startedProcess = $null
$targetRecord = $null
$previousInstanceId = $env:NAVISHELPER_INSTANCE_ID

try {
    if ($UseExisting) {
        Invoke-Step "Use Existing Navisworks" {
            $script:targetRecord = Wait-NavisHelperInstance -ProcessId 0 -TargetVersion $Version -TimeoutSec $WaitTimeoutSec
            Write-Host "Using instance $($script:targetRecord.instance_id) pid=$($script:targetRecord.pid) document=$($script:targetRecord.document_title)"
        }
    }
    else {
        Invoke-Step "Start Navisworks $Version" {
            $script:startedProcess = & (Join-Path $scriptDir "start_navisworks.ps1") -FilePath $FilePath -Version $Version -PassThru
            if ($null -eq $script:startedProcess) {
                throw "start_navisworks.ps1 did not return a process."
            }

            Write-Host "Started pid=$($script:startedProcess.Id)"
        }

        Invoke-Step "Wait For MCP Host" {
            $script:targetRecord = Wait-NavisHelperInstance -ProcessId ([int]$script:startedProcess.Id) -TargetVersion $Version -TimeoutSec $WaitTimeoutSec
            Write-Host "Ready instance=$($script:targetRecord.instance_id) document=$($script:targetRecord.document_title)"
        }
    }

    $env:NAVISHELPER_INSTANCE_ID = $targetRecord.instance_id

    Invoke-Step "MCP Smoke" {
        $smokeArgs = @((Join-Path $scriptDir "navishelper_mcp_smoke.py"))
        if (-not $UseExisting) {
            $smokeArgs += "--apply-saved-viewpoint", "--selection-sets-lifecycle", "--selection-properties-smoke", "--subtree-dump-failure-smoke", "--clash-report-dry-run"
        }

        python -X utf8 @smokeArgs
        if ($LASTEXITCODE -ne 0) {
            throw "navishelper_mcp_smoke.py failed with exit code $LASTEXITCODE."
        }
    }

    if (-not $SkipStress) {
        Invoke-Step "Host Stress find_root_items_by_name" {
            $names = @(
                "240000-АЛТ1.rvm",
                "240000-АПТ1.rvm",
                "240000-АС1.rvm",
                "240000-АС3.rvm",
                "240000-АС4.rvm",
                "240000-АС5.rvm",
                "240000-АС6.rvm",
                "240000-АС7.rvm",
                "240000-АС8.rvm",
                "240000-АС9.rvm",
                "240000-АС10.rvm",
                "240000-АС11.rvm",
                "240000-АС12.rvm",
                "240000-АС13.rvm",
                "240000-АС14.rvm",
                "240000-АС15.rvm",
                "240000-АС16.rvm",
                "240000-АС17.rvm",
                "240000-АС18.rvm",
                "240000-АС19.rvm"
            )

            $payload = @{
                names = $names
                comparison = "equals"
                preview_limit = 1
            } | ConvertTo-Json -Depth 10 -Compress

            $payloadFile = Join-Path ([System.IO.Path]::GetTempPath()) ("navishelper_stress_payload_" + [Guid]::NewGuid().ToString("N") + ".json")
            try {
                Set-Content -LiteralPath $payloadFile -Value $payload -Encoding UTF8
                powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir "navishelper_host_stress.ps1") `
                    -Command "find_root_items_by_name" `
                    -PayloadJsonFile $payloadFile `
                    -Count $StressCount `
                    -DelayMs $StressDelayMs `
                    -TimeoutMs 60000 `
                    -InstanceId $targetRecord.instance_id `
                    -StopOnError

                if ($LASTEXITCODE -ne 0) {
                    throw "navishelper_host_stress.ps1 failed with exit code $LASTEXITCODE."
                }
            }
            finally {
                Remove-Item -LiteralPath $payloadFile -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (-not $SkipMixedStress -and $MixedStressCount -gt 0) {
        Invoke-Step "Mixed MCP Stress" {
            $effectiveReportDirectory = $ReportDirectory
            if ([string]::IsNullOrWhiteSpace($effectiveReportDirectory)) {
                $effectiveReportDirectory = Join-Path $repoRoot "artifacts\mcp-stress"
            }

            New-Item -ItemType Directory -Path $effectiveReportDirectory -Force | Out-Null
            $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $reportJsonFile = Join-Path $effectiveReportDirectory ("mixed-stress-$Version-$stamp.json")

            $mixedArgs = @(
                (Join-Path $scriptDir "navishelper_mcp_mixed_stress.py"),
                "--count",
                $MixedStressCount,
                "--delay-ms",
                $StressDelayMs,
                "--memory-checkpoint-interval",
                $MemoryCheckpointInterval,
                "--max-memory-delta-mb",
                $MaxMemoryDeltaMb,
                "--report-json-file",
                $reportJsonFile
            )
            if (-not $UseExisting) {
                $mixedArgs += "--include-selection"
            }

            python -X utf8 @mixedArgs
            if ($LASTEXITCODE -ne 0) {
                throw "navishelper_mcp_mixed_stress.py failed with exit code $LASTEXITCODE."
            }

            Write-Host "Mixed stress report: $reportJsonFile"
        }
    }

    Invoke-Step "Post-Stress MCP Smoke" {
        python -X utf8 (Join-Path $scriptDir "navishelper_mcp_smoke.py") --skip-view
        if ($LASTEXITCODE -ne 0) {
            throw "navishelper_mcp_smoke.py --skip-view failed with exit code $LASTEXITCODE."
        }
    }

    Invoke-Step "Regression Summary" {
        [pscustomobject]@{
            Ok = $true
            InstanceId = $targetRecord.instance_id
            Pid = $targetRecord.pid
            Document = $targetRecord.document_title
            StressCount = if ($SkipStress) { 0 } else { $StressCount }
            MixedStressCount = if ($SkipMixedStress) { 0 } else { $MixedStressCount }
            KeptOpen = [bool]$KeepNavisworksOpen
        } | ConvertTo-Json -Depth 5
    }
}
finally {
    $env:NAVISHELPER_INSTANCE_ID = $previousInstanceId

    if (-not $KeepNavisworksOpen -and -not $UseExisting -and $null -ne $startedProcess) {
        Invoke-Step "Close Navisworks" {
            Close-NavisworksProcess -ProcessId ([int]$startedProcess.Id)
            Write-Host "Closed pid=$($startedProcess.Id)"
        }
    }
}

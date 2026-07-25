param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [ValidateSet("2024", "2025", "2026", "2027")]
    [string]$Version = "2027",

    [int]$WaitTimeoutSec = 360,

    [switch]$SkipMultipleHosts,

    [switch]$SkipNoDocument,

    [switch]$SkipHostClosed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$roamerPath = "C:\Program Files\Autodesk\Navisworks Manage $Version\Roamer.exe"

if (-not (Test-Path -LiteralPath $roamerPath)) {
    throw "Navisworks Manage $Version was not found at '$roamerPath'."
}

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
    $directory = Get-InstancesDirectory
    if (-not (Test-Path -LiteralPath $directory)) {
        return @()
    }

    $records = @()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter *.json -File) {
        try {
            $record = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($null -eq $record) {
                continue
            }
            if (-not (Test-ProcessAlive -ProcessId ([int]$record.pid))) {
                continue
            }

            $record | Add-Member -NotePropertyName "__last_write_time" -NotePropertyValue $file.LastWriteTimeUtc -Force
            $records += $record
        }
        catch {
            continue
        }
    }

    return $records
}

function Wait-InstanceByPid {
    param(
        [int]$ProcessId,
        [bool]$RequireDocument
    )

    $deadline = (Get-Date).AddSeconds($WaitTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $records = @(Read-DiscoveryRecords | Where-Object { [int]$_.pid -eq $ProcessId } | Sort-Object __last_write_time -Descending)
        if ($RequireDocument) {
            $records = @($records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.document_title) })
        }

        if ($records.Count -gt 0) {
            return $records[0]
        }

        if (-not (Test-ProcessAlive -ProcessId $ProcessId)) {
            throw "Navisworks process $ProcessId exited before NavisHelper MCP host became ready."
        }

        Start-Sleep -Seconds 5
    }

    throw "Timed out after $WaitTimeoutSec seconds waiting for NavisHelper MCP host pid=$ProcessId."
}

function Start-Navisworks {
    param([string]$ModelPath)

    if ([string]::IsNullOrWhiteSpace($ModelPath)) {
        return Start-Process -FilePath $roamerPath -PassThru
    }

    $resolvedFile = Resolve-Path -LiteralPath $ModelPath -ErrorAction Stop
    $quotedFile = '"' + $resolvedFile.Path + '"'
    return Start-Process -FilePath $roamerPath -ArgumentList $quotedFile -PassThru
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

    Write-Warning "Navisworks process $ProcessId is still running after CloseMainWindow. CloseSignalSent=$sent. Window='$($process.MainWindowTitle)'. Stopping the test-owned process."
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
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

function Invoke-FailureMode {
    param(
        [string]$Scenario,
        [string]$InstanceId = ""
    )

    $args = @(
        "-X",
        "utf8",
        (Join-Path $scriptDir "navishelper_mcp_failure_modes.py"),
        "--scenario",
        $Scenario
    )
    if (-not [string]::IsNullOrWhiteSpace($InstanceId)) {
        $args += "--instance-id"
        $args += $InstanceId
    }

    python @args
    if ($LASTEXITCODE -ne 0) {
        throw "navishelper_mcp_failure_modes.py scenario=$Scenario failed with exit code $LASTEXITCODE."
    }
}

$started = @()
$results = @()

try {
    if (-not $SkipMultipleHosts) {
        Invoke-Step "Failure Mode: Multiple Hosts" {
            $p1 = Start-Navisworks -ModelPath $FilePath
            $p2 = Start-Navisworks -ModelPath $FilePath
            $script:started += $p1
            $script:started += $p2
            $r1 = Wait-InstanceByPid -ProcessId ([int]$p1.Id) -RequireDocument $true
            $r2 = Wait-InstanceByPid -ProcessId ([int]$p2.Id) -RequireDocument $true
            Write-Host "Ready instances: $($r1.instance_id), $($r2.instance_id)"
            Invoke-FailureMode -Scenario "multiple-hosts"
            $script:results += [pscustomobject]@{ Scenario = "multiple-hosts"; Ok = $true }
        }

        foreach ($process in @($started)) {
            Close-NavisworksProcess -ProcessId ([int]$process.Id)
        }
        $started = @()
    }

    if (-not $SkipNoDocument) {
        Invoke-Step "Failure Mode: No Document" {
            $p = Start-Navisworks -ModelPath ""
            $script:started += $p
            $record = Wait-InstanceByPid -ProcessId ([int]$p.Id) -RequireDocument $false
            Write-Host "Ready no-document instance: $($record.instance_id)"
            Invoke-FailureMode -Scenario "no-document" -InstanceId $record.instance_id
            $script:results += [pscustomobject]@{ Scenario = "no-document"; Ok = $true }
        }

        foreach ($process in @($started)) {
            Close-NavisworksProcess -ProcessId ([int]$process.Id)
        }
        $started = @()
    }

    if (-not $SkipHostClosed) {
        Invoke-Step "Failure Mode: Host Closed" {
            $p = Start-Navisworks -ModelPath $FilePath
            $script:started += $p
            $record = Wait-InstanceByPid -ProcessId ([int]$p.Id) -RequireDocument $true
            Write-Host "Ready instance before close: $($record.instance_id)"
            Close-NavisworksProcess -ProcessId ([int]$p.Id)
            $script:started = @()
            Start-Sleep -Seconds 2
            Invoke-FailureMode -Scenario "host-closed" -InstanceId $record.instance_id
            $script:results += [pscustomobject]@{ Scenario = "host-closed"; Ok = $true }
        }
    }

    Write-Host ""
    Write-Host "== Failure Mode Summary =="
    [pscustomobject]@{
        Ok = $true
        Version = $Version
        Results = $results
    } | ConvertTo-Json -Depth 5
}
finally {
    foreach ($process in @($started)) {
        try {
            Close-NavisworksProcess -ProcessId ([int]$process.Id)
        }
        catch {
            Write-Warning $_.Exception.Message
        }
    }
}

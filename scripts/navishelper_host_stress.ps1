param(
    [string]$Command = "fit_all",

    [string]$PayloadJson = "{}",

    [string]$PayloadJsonFile,

    [int]$Count = 50,

    [int]$DelayMs = 250,

    [int]$TimeoutMs = 60000,

    [string]$InstanceId,

    [string]$NavisworksVersion,

    [switch]$Latest,

    [switch]$StopOnError
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
            $record = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
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

function Resolve-TargetRecord {
    param(
        [string]$Directory,
        [string]$TargetInstanceId,
        [string]$TargetVersion,
        [bool]$UseLatest
    )

    $records = @(Read-DiscoveryRecords -Directory $Directory)

    if (-not [string]::IsNullOrWhiteSpace($TargetInstanceId)) {
        $matched = @($records | Where-Object { $_.instance_id -eq $TargetInstanceId })
        if ($matched.Count -eq 0) {
            throw "Instance '$TargetInstanceId' was not found in '$Directory'."
        }

        return $matched[0]
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetVersion)) {
        $records = @($records | Where-Object { $_.navisworks_version -eq $TargetVersion })
    }

    if ($records.Count -eq 0) {
        throw "No NavisHelper MCP discovery records were found."
    }

    $records = @($records | Sort-Object __last_write_time -Descending)

    if ($records.Count -gt 1 -and -not $UseLatest) {
        $summary = $records | ForEach-Object {
            "$($_.instance_id) [version=$($_.navisworks_version)] [pid=$($_.pid)] [document=$($_.document_title)]"
        }

        throw "Multiple NavisHelper MCP instances found. Re-run with -Latest, -InstanceId, or -NavisworksVersion.`n$($summary -join [Environment]::NewLine)"
    }

    return $records[0]
}

function Write-Frame {
    param(
        [System.IO.Stream]$Stream,
        [string]$Json
    )

    $payload = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $length = [BitConverter]::GetBytes($payload.Length)
    $Stream.Write($length, 0, $length.Length)
    $Stream.Write($payload, 0, $payload.Length)
    $Stream.Flush()
}

function Read-Exactly {
    param(
        [System.IO.Stream]$Stream,
        [int]$Count
    )

    $buffer = New-Object byte[] $Count
    $offset = 0

    while ($offset -lt $Count) {
        $read = $Stream.Read($buffer, $offset, $Count - $offset)
        if ($read -le 0) {
            throw "Pipe closed while reading response."
        }

        $offset += $read
    }

    return $buffer
}

function Read-Frame {
    param([System.IO.Stream]$Stream)

    $lengthBuffer = Read-Exactly -Stream $Stream -Count 4
    $length = [BitConverter]::ToInt32($lengthBuffer, 0)
    if ($length -le 0) {
        throw "Invalid frame length: $length"
    }

    $payload = Read-Exactly -Stream $Stream -Count $length
    return [System.Text.Encoding]::UTF8.GetString($payload)
}

function Invoke-HostCommand {
    param(
        [object]$Record,
        [object]$PayloadObject,
        [int]$Timeout
    )

    $request = @{
        request_id = "req-" + [Guid]::NewGuid().ToString("N")
        instance_id = $Record.instance_id
        command = $Command
        timeout_ms = $Timeout
        payload = $PayloadObject
    }

    $json = $request | ConvertTo-Json -Depth 30 -Compress
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $Record.pipe_name, [System.IO.Pipes.PipeDirection]::InOut)

    try {
        $pipe.Connect($Timeout)
        Write-Frame -Stream $pipe -Json $json
        $responseJson = Read-Frame -Stream $pipe
        return $responseJson | ConvertFrom-Json
    }
    finally {
        $pipe.Dispose()
    }
}

if ($Count -lt 1) {
    throw "Count must be 1 or greater."
}

$instancesDirectory = Get-InstancesDirectory
$record = Resolve-TargetRecord -Directory $instancesDirectory -TargetInstanceId $InstanceId -TargetVersion $NavisworksVersion -UseLatest:$Latest
if (-not [string]::IsNullOrWhiteSpace($PayloadJsonFile)) {
    $PayloadJson = Get-Content -LiteralPath $PayloadJsonFile -Raw -Encoding UTF8
}

$payloadObject = ConvertFrom-Json -InputObject $PayloadJson

Write-Host "Target: $($record.instance_id) pipe=$($record.pipe_name) pid=$($record.pid) document=$($record.document_title)"
Write-Host "Command: $Command count=$Count timeout_ms=$TimeoutMs delay_ms=$DelayMs"

$results = @()
$overall = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 1; $i -le $Count; $i++) {
    $started = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "ok"
    $errorCode = ""
    $errorMessage = ""
    $elapsedFromHost = $null

    try {
        $response = Invoke-HostCommand -Record $record -PayloadObject $payloadObject -Timeout $TimeoutMs
        $elapsedFromHost = $response.elapsed_ms

        if (-not $response.ok) {
            $status = "error"
            $errorCode = if ($response.error_code) { $response.error_code } else { "unknown_error" }
            $errorMessage = if ($response.error_message) { $response.error_message } else { "Unknown host error." }
        }
    }
    catch {
        $status = "exception"
        $errorCode = $_.Exception.GetType().Name
        $errorMessage = $_.Exception.Message
    }
    finally {
        $started.Stop()
    }

    $process = Get-Process -Id ([int]$record.pid) -ErrorAction SilentlyContinue
    $workingSetMb = if ($process) { [Math]::Round($process.WorkingSet64 / 1MB, 1) } else { $null }

    $row = [pscustomobject]@{
        Iteration = $i
        Status = $status
        ErrorCode = $errorCode
        ElapsedMs = $started.ElapsedMilliseconds
        HostElapsedMs = $elapsedFromHost
        WorkingSetMb = $workingSetMb
        ErrorMessage = $errorMessage
    }

    $results += $row
    $row | Format-Table -AutoSize | Out-String -Width 240 | Write-Host

    if ($StopOnError -and $status -ne "ok") {
        break
    }

    if ($DelayMs -gt 0 -and $i -lt $Count) {
        Start-Sleep -Milliseconds $DelayMs
    }
}

$overall.Stop()
$okCount = @($results | Where-Object { $_.Status -eq "ok" }).Count
$errorCount = $results.Count - $okCount
$avgElapsed = if ($results.Count -gt 0) { [Math]::Round(($results | Measure-Object -Property ElapsedMs -Average).Average, 1) } else { 0 }
$maxElapsed = if ($results.Count -gt 0) { ($results | Measure-Object -Property ElapsedMs -Maximum).Maximum } else { 0 }

[pscustomobject]@{
    TargetInstanceId = $record.instance_id
    Command = $Command
    RequestedCount = $Count
    CompletedCount = $results.Count
    OkCount = $okCount
    ErrorCount = $errorCount
    AverageElapsedMs = $avgElapsed
    MaxElapsedMs = $maxElapsed
    TotalElapsedMs = $overall.ElapsedMilliseconds
} | ConvertTo-Json -Depth 5

param(
    [Parameter(Mandatory = $true)]
    [string]$Command,

    [string]$PayloadJson = "{}",

    [string]$InstanceId,

    [string]$NavisworksVersion,

    [switch]$Latest,

    [int]$TimeoutMs = 60000
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

function Read-DiscoveryRecords {
    param(
        [string]$Directory
    )

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
    param(
        [System.IO.Stream]$Stream
    )

    $lengthBuffer = Read-Exactly -Stream $Stream -Count 4
    $length = [BitConverter]::ToInt32($lengthBuffer, 0)
    if ($length -le 0) {
        throw "Invalid frame length: $length"
    }

    $payload = Read-Exactly -Stream $Stream -Count $length
    return [System.Text.Encoding]::UTF8.GetString($payload)
}

$instancesDirectory = Get-InstancesDirectory
$record = Resolve-TargetRecord -Directory $instancesDirectory -TargetInstanceId $InstanceId -TargetVersion $NavisworksVersion -UseLatest:$Latest
$payloadObject = ConvertFrom-Json -InputObject $PayloadJson
$request = @{
    request_id = "req-" + [Guid]::NewGuid().ToString("N")
    instance_id = $record.instance_id
    command = $Command
    timeout_ms = $TimeoutMs
    payload = $payloadObject
}

$json = $request | ConvertTo-Json -Depth 20 -Compress

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $record.pipe_name, [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect($TimeoutMs)
    Write-Frame -Stream $pipe -Json $json
    $responseJson = Read-Frame -Stream $pipe
    $response = $responseJson | ConvertFrom-Json

    if (-not $response.ok) {
        $errorCode = if ($response.error_code) { $response.error_code } else { "unknown_error" }
        $errorMessage = if ($response.error_message) { $response.error_message } else { "Unknown host error." }
        throw "${errorCode}: $errorMessage"
    }

    $response | ConvertTo-Json -Depth 20
}
finally {
    $pipe.Dispose()
}

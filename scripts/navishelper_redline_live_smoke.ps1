param(
    [string]$FolderPath = "NavisHelper Live Smoke",
    [string]$Name = "",
    [switch]$SectionBox,
    [string]$InstanceId,
    [string]$NavisworksVersion,
    [switch]$Latest,
    [int]$TimeoutMs = 120000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Name)) {
    $Name = "Redline smoke " + (Get-Date).ToString("yyyyMMdd-HHmmss")
}

$hostSmoke = Join-Path $PSScriptRoot "navishelper_host_smoke.ps1"
$payload = if ($SectionBox) {
    @{
        Name = $Name
        FolderPath = $FolderPath
        Source = "current_selection"
        BoxOffsetMm = 1000
        MarkStyle = "target"
        ArrowCallout = $true
        ArrowLengthMm = 0
        TargetCrosshair = $false
        Apply = $true
    }
} else {
    @{
        Name = $Name
        FolderPath = $FolderPath
        Source = "current_selection"
        AutoTopView = $true
        FitToSelection = $true
        MarkStyle = "target"
        ArrowCallout = $true
        ArrowLengthMm = 0
        TargetCrosshair = $false
        Apply = $true
    }
}

$arguments = @{
    Command = if ($SectionBox) { "section_box_viewpoint" } else { "markup_selection" }
    PayloadJson = ($payload | ConvertTo-Json -Depth 10 -Compress)
    TimeoutMs = $TimeoutMs
}
if (-not [string]::IsNullOrWhiteSpace($InstanceId)) { $arguments.InstanceId = $InstanceId }
if (-not [string]::IsNullOrWhiteSpace($NavisworksVersion)) { $arguments.NavisworksVersion = $NavisworksVersion }
if ($Latest) { $arguments.Latest = $true }

$raw = & $hostSmoke @arguments
$rawText = $raw -join [Environment]::NewLine
$response = $rawText | ConvertFrom-Json
$payloadProperty = $response.PSObject.Properties["payload"]
$result = if ($null -ne $payloadProperty) { $payloadProperty.Value } else { $response }
$markProperty = $result.PSObject.Properties["mark_count"]
if ($null -eq $markProperty) { $markProperty = $result.PSObject.Properties["MarkCount"] }
$arrowProperty = $result.PSObject.Properties["arrow_count"]
if ($null -eq $arrowProperty) { $arrowProperty = $result.PSObject.Properties["ArrowCount"] }
$markCount = if ($null -eq $markProperty) { 0 } else { [int]$markProperty.Value }
$arrowCount = if ($null -eq $arrowProperty) { 0 } else { [int]$arrowProperty.Value }

if ($markCount -le 0) {
    throw "Live redline smoke created no marks. Response: $rawText"
}
if ($arrowCount -le 0) {
    throw "Live redline smoke created no arrow callouts. Response: $rawText"
}

Write-Host "Live redline smoke passed. command=$($arguments.Command) name='$Name' markCount=$markCount arrowCount=$arrowCount"
$rawText

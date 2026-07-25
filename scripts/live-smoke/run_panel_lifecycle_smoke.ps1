param(
    [Parameter(Mandatory = $true)]
    [string]$NwdPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectRoot = Join-Path $PSScriptRoot "NavisHelper.PanelLifecycleSmoke"
$projectPath = Join-Path $projectRoot "NavisHelper.PanelLifecycleSmoke.csproj"
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$automationAssembly = "C:\Program Files\Autodesk\Navisworks Manage 2027\Autodesk.Navisworks.Automation.dll"
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"))
$smokeBundle = [IO.Path]::GetFullPath((Join-Path $pluginsRoot "NavisHelper.PanelLifecycleSmoke.bundle"))
$expectedParent = [IO.Directory]::GetParent($smokeBundle).FullName
$resultPath = Join-Path $env:TEMP ("navishelper-panel-lifecycle-smoke-" + [Guid]::NewGuid().ToString("N") + ".txt")

function Remove-SmokeBundle {
    if (-not (Test-Path -LiteralPath $smokeBundle)) {
        return
    }

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Remove-Item -LiteralPath $smokeBundle -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

if ([IO.Path]::GetExtension($NwdPath) -ine ".nwd") {
    throw "Panel lifecycle smoke accepts only .nwd files: $NwdPath"
}
if (-not (Test-Path -LiteralPath $NwdPath)) {
    throw "NWD does not exist: $NwdPath"
}
if ($expectedParent -ine $pluginsRoot -or [IO.Path]::GetFileName($smokeBundle) -ine "NavisHelper.PanelLifecycleSmoke.bundle") {
    throw "Unexpected smoke bundle path: $smokeBundle"
}
if (@(Get-Process -Name roamer -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "An existing roamer process prevents an isolated lifecycle smoke"
}

& $msbuild $projectPath /restore /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Smoke plugin build failed with exit code $LASTEXITCODE"
}

$before = @(Get-Process -Name roamer -ErrorAction SilentlyContinue | ForEach-Object Id)
$ownedProcessIds = @()
try {
    if (Test-Path -LiteralPath $smokeBundle) {
        Remove-SmokeBundle
    }
    $contents = Join-Path $smokeBundle "Contents\2027"
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot "PackageContents.xml") -Destination (Join-Path $smokeBundle "PackageContents.xml") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "bin\x64\Release\net481\NavisHelper.PanelLifecycleSmoke.dll") -Destination (Join-Path $contents "NavisHelper.PanelLifecycleSmoke.dll") -Force

    Add-Type -Path $automationAssembly
    $navisworks = New-Object Autodesk.Navisworks.Api.Automation.NavisworksApplication
    try {
        $navisworks.Visible = $false
        $navisworks.OpenFile($NwdPath)
        $result = $navisworks.ExecuteAddInPlugin("NavisHelperPanelLifecycleSmoke.CODX", [string[]]@($resultPath))
        if ($result -ne 0) {
            $details = if (Test-Path -LiteralPath $resultPath) { Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath } else { "no result file" }
            throw "Panel lifecycle smoke plugin returned $result`n$details"
        }
        if (-not (Test-Path -LiteralPath $resultPath)) {
            throw "Panel lifecycle smoke did not create its result file"
        }
        $resultLines = @(Get-Content -Encoding UTF8 -LiteralPath $resultPath)
        $details = $resultLines -join [Environment]::NewLine
        if ($resultLines -notcontains "status=passed") {
            throw "Panel lifecycle smoke did not report success`n$details"
        }
        Write-Output $details.TrimEnd()
    }
    finally {
        if ($null -ne $navisworks) {
            $navisworks.Dispose()
        }
    }

    Start-Sleep -Seconds 5
    $ownedProcessIds = @(Get-Process -Name roamer -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | ForEach-Object Id)
}
finally {
    $ownedProcessIds = @(
        $ownedProcessIds
        Get-Process -Name roamer -ErrorAction SilentlyContinue |
            Where-Object { $before -notcontains $_.Id } |
            ForEach-Object Id
    ) | Select-Object -Unique
    foreach ($processId in $ownedProcessIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $resultPath) {
        Remove-Item -LiteralPath $resultPath -Force
    }
    if (Test-Path -LiteralPath $smokeBundle) {
        Remove-SmokeBundle
    }
}

if (@(Get-Process -Name roamer -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "A roamer process remains after lifecycle smoke cleanup"
}
Write-Output "roamer_remaining=0"

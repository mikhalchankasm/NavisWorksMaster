param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-NavisworksClosed {
    $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in @("Roamer", "Navisworks")
    })
    if ($processes.Count -gt 0) {
        $details = ($processes | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
        throw "Close Autodesk Navisworks before removing the machine-wide bundle. Running process(es): $details"
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run PowerShell as Administrator to remove the machine-wide NavisHelper bundle."
}

Assert-NavisworksClosed

$targets = @(
    [System.IO.Path]::GetFullPath((Join-Path $env:ProgramData "Autodesk\ApplicationPlugins\NavisHelper.bundle")),
    [System.IO.Path]::GetFullPath((Join-Path $env:ProgramFiles "NavisHelper"))
) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }

if ($targets.Count -eq 0) {
    Write-Host "No legacy machine-wide NavisHelper installation was found."
    return
}

if (-not $Force) {
    $answer = Read-Host "Remove legacy machine-wide NavisHelper path(s): $($targets -join '; ')? Type YES"
    if ($answer -ne "YES") {
        Write-Host "Cancelled."
        return
    }
}

foreach ($target in $targets) {
    Remove-Item -LiteralPath $target -Recurse -Force
    Write-Host "Removed legacy machine-wide NavisHelper path: $target"
}

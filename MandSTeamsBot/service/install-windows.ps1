<#
Installs (or updates) the check-in bot as a background service on this Windows PC.

  - Copies the built bot into its own folder, away from the source code.
  - On first install, copies .env and the data folder (database + sign-in token) there.
    On later runs those are left alone: the service folder owns them from then on.
  - Registers a scheduled task that starts the bot when you sign in to Windows,
    keeps it running, and restarts it if it stops.

Run from the MandSTeamsBot folder after "npm run build":

  powershell -ExecutionPolicy Bypass -File service\install-windows.ps1

Run it again after any code change to update the service.
#>
param(
    [string]$Target = "C:\Mergen\MandSTeamsBot-service",
    [string]$TaskName = "MandSTeamsBot"
)
$ErrorActionPreference = "Stop"
$source = Split-Path $PSScriptRoot -Parent

if (-not (Test-Path (Join-Path $source "dist\index.js"))) { throw "Build first: npm run build" }

# Stop whatever is running from the service folder, so files can be replaced
# and so two copies never run at once (that would send every message twice).
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}
Get-CimInstance Win32_Process -Filter "Name = 'node.exe' OR Name = 'powershell.exe'" |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine -like "*$Target*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

New-Item -ItemType Directory -Force $Target | Out-Null

# Code: always replaced.
robocopy (Join-Path $source "dist") (Join-Path $Target "dist") /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the build failed (robocopy $LASTEXITCODE)" }
Copy-Item (Join-Path $source "package.json"), (Join-Path $source "package-lock.json") $Target -Force
Copy-Item (Join-Path $PSScriptRoot "run-service.ps1") $Target -Force

# Settings and data: only on first install.
if (-not (Test-Path (Join-Path $Target ".env"))) {
    Copy-Item (Join-Path $source ".env") $Target
    Write-Host "Copied .env"
}
if (-not (Test-Path (Join-Path $Target "data"))) {
    Copy-Item (Join-Path $source "data") (Join-Path $Target "data") -Recurse
    Write-Host "Copied the data folder (database and sign-in token)"
}

Push-Location $Target
try {
    npm ci --omit=dev --no-audit --no-fund | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
} finally { Pop-Location }

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Target\run-service.ps1`"" `
    -WorkingDirectory $Target
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description "AI daily check-in bot for Teams. Starts at sign-in and keeps itself running." -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Installed in $Target and started. Log: $Target\data\bot.log"

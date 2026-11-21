# Keeps the check-in bot running: starts it, and starts it again if it ever stops.
# Launched by the "MandSTeamsBot" scheduled task; not meant to be run by hand.
Set-Location $PSScriptRoot
$node = (Get-Command node -ErrorAction Stop).Source

while ($true) {
    $log = Join-Path $PSScriptRoot "data\bot.log"
    if ((Test-Path $log) -and ((Get-Item $log).Length -gt 5MB)) {
        Move-Item $log (Join-Path $PSScriptRoot "data\bot.previous.log") -Force
    }
    # cmd does the redirection so the log stays plain UTF-8 text.
    cmd /c "`"$node`" --env-file=.env dist\index.js >> data\bot.log 2>&1"
    Start-Sleep -Seconds 15
}

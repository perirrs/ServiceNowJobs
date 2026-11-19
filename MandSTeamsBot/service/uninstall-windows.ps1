<#
Stops the check-in bot and removes its scheduled task. The service folder,
with the database and sign-in token, is left in place: delete it yourself if
you no longer need the notes.

  powershell -ExecutionPolicy Bypass -File service\uninstall-windows.ps1
#>
param(
    [string]$Target = "C:\Mergen\MandSTeamsBot-service",
    [string]$TaskName = "MandSTeamsBot"
)
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Get-CimInstance Win32_Process -Filter "Name = 'node.exe' OR Name = 'powershell.exe'" |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine -like "*$Target*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host "Stopped and removed. Data is still in $Target."

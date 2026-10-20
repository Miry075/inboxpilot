# Registers a Windows scheduled task that runs triage at fixed times every day.
# Runs only while you are logged on: it needs your Claude Code login and token caches.
# Usage: .\scripts\register-task.ps1 [-Times "08:05","12:05","17:05"]
param(
    [string[]]$Times = @("08:05", "10:05", "12:05", "14:05", "16:05", "18:05", "20:05"),
    [string]$TaskName = "InboxPilot Triage"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot "run-triage.ps1"

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    throw "'claude' is not on PATH. Install Claude Code and run 'claude' once to sign in."
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`"" `
    -WorkingDirectory $root

$triggers = $Times | ForEach-Object { New-ScheduledTaskTrigger -Daily -At $_ }

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings `
    -Description "InboxPilot: Claude Code triages Gmail + Outlook and drafts replies (never sends)." -Force | Out-Null

Write-Host "Registered '$TaskName' at: $($Times -join ', ')"
Write-Host "Run now:  Start-ScheduledTask -TaskName '$TaskName'"
Write-Host "Remove:   Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false"

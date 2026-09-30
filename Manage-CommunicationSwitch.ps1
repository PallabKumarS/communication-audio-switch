<#
.SYNOPSIS
    Management and troubleshooting script for CommunicationSwitch.
.DESCRIPTION
    Checks service status, views logs, starts/stops background process, and runs live tests.
#>

param (
    [ValidateSet("Status", "Start", "Stop", "Restart", "Log", "Test", "Gui")]
    [string]$Action = "Status",
    [int]$LogLines = 25
)

$baseDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
$exePath = Join-Path $baseDir "CommunicationSwitch.exe"
$cliPath = Join-Path $baseDir "CommunicationSwitch-CLI.exe"
$logPath = Join-Path $baseDir "CommunicationSwitch.log"

function Show-Status {
    Write-Host "`n=== Communication Switch Service Status ===" -ForegroundColor Cyan
    $proc = Get-Process -Name "CommunicationSwitch" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "Service State:   " -NoNewline
        Write-Host "RUNNING" -ForegroundColor Green -NoNewline
        Write-Host " (PID: $($proc.Id), Memory: $([math]::Round($proc.WorkingSet64 / 1MB, 1)) MB)"
    } else {
        Write-Host "Service State:   " -NoNewline
        Write-Host "STOPPED" -ForegroundColor Red
    }

    $task = Get-ScheduledTask -TaskName "CommunicationSwitch" -ErrorAction SilentlyContinue
    if ($task) {
        Write-Host "Autostart Task:  " -NoNewline
        Write-Host "REGISTERED ($($task.State))" -ForegroundColor Green
    } else {
        Write-Host "Autostart Task:  " -NoNewline
        Write-Host "NOT FOUND" -ForegroundColor Yellow
    }

    $runKey = Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "CommunicationSwitch" -ErrorAction SilentlyContinue
    if ($runKey) {
        Write-Host "HKCU Run Key:    " -NoNewline
        Write-Host "CONFIGURED" -ForegroundColor Green
    }

    if (Test-Path $cliPath) {
        & $cliPath --status
    }
}

function Start-App {
    $proc = Get-Process -Name "CommunicationSwitch" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "CommunicationSwitch is already running (PID: $($proc.Id))." -ForegroundColor Yellow
        return
    }
    Write-Host "Starting CommunicationSwitch..." -ForegroundColor Cyan
    if (Test-Path $exePath) {
        Start-Process $exePath -ArgumentList "--tray"
        Start-Sleep -Seconds 2
        Show-Status
    } else {
        Write-Host "Executable not found at $exePath. Run build.bat first." -ForegroundColor Red
    }
}

function Stop-App {
    Write-Host "Stopping CommunicationSwitch..." -ForegroundColor Cyan
    Stop-Process -Name "CommunicationSwitch" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "CommunicationSwitch stopped." -ForegroundColor Green
}

function Show-Log {
    if (Test-Path $logPath) {
        Write-Host "`n=== Last $LogLines Log Entries ($logPath) ===" -ForegroundColor Cyan
        Get-Content $logPath -Tail $LogLines
        Write-Host "====================================================`n" -ForegroundColor Cyan
    } else {
        Write-Host "Log file not found at $logPath." -ForegroundColor Yellow
    }
}

function Run-LiveTest {
    if (Test-Path $cliPath) {
        & $cliPath --test
    } else {
        Write-Host "CLI binary not found at $cliPath. Run build.bat first." -ForegroundColor Red
    }
}

function Open-Gui {
    if (Test-Path $exePath) {
        Start-Process $exePath
    } else {
        Write-Host "Executable not found at $exePath. Run build.bat first." -ForegroundColor Red
    }
}

switch ($Action) {
    "Status"  { Show-Status }
    "Start"   { Start-App }
    "Stop"    { Stop-App }
    "Restart" { Stop-App; Start-App }
    "Log"     { Show-Log }
    "Test"    { Run-LiveTest }
    "Gui"     { Open-Gui }
}

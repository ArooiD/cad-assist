param(
    [string]$ModelPath = "C:\cad-assist-test\test.m3d",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$logDirectory = "C:\cad-assist-test"
$publishedAppDir = "C:\cad-assist-test\app"
$publishedExe = Join-Path $publishedAppDir "CadAssist.Kompas.Interop.exe"
$logPath = Join-Path $logDirectory "cad-assist-launcher.log"
$stdoutPath = Join-Path $logDirectory "cad-assist-task-window.stdout.log"
$stderrPath = Join-Path $logDirectory "cad-assist-task-window.stderr.log"
$jsonLogPath = Join-Path $logDirectory "cad-assist-task-window.json"

function Write-LauncherLog {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

try {
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Set-Content -Path $logPath -Value "CAD Assist launcher started" -Encoding UTF8

    Write-LauncherLog "ModelPath: $ModelPath"
    Write-LauncherLog "PublishedExe: $publishedExe"

    if (-not (Test-Path $ModelPath)) {
        throw "KOMPAS model was not found: $ModelPath"
    }

    if (-not (Test-Path $publishedExe)) {
        throw "Published CAD Assist executable was not found. Run GitHub workflow first. Expected file: $publishedExe"
    }

    $process = Start-Process -FilePath $publishedExe -ArgumentList @(
        "--model-path", $ModelPath,
        "--show-task-window",
        "--json-log", $jsonLogPath
    ) -WorkingDirectory $publishedAppDir -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru

    Write-LauncherLog "Process started. PID: $($process.Id)"
}
catch {
    Write-LauncherLog "ERROR: $($_.Exception.Message)"
    Write-Host "CAD Assist launcher failed."
    Write-Host $_.Exception.Message
    if (-not $NoPause) {
        Read-Host "Press Enter to close"
    }
    exit 1
}

if (-not $NoPause) {
    Write-Host "CAD Assist launcher started."
    Write-Host "Log: $logPath"
    Read-Host "Press Enter to close"
}

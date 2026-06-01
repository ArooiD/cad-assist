param(
    [string]$ModelPath = "C:\cad-assist-test\test.m3d",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$logDirectory = "C:\cad-assist-test"
$logPath = Join-Path $logDirectory "cad-assist-launcher.log"

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

    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $repoRoot = Split-Path -Parent $scriptDir
    $projectPath = Join-Path $repoRoot "src\CadAssist.Kompas.Interop\CadAssist.Kompas.Interop.csproj"
    $releaseExe = Join-Path $repoRoot "src\CadAssist.Kompas.Interop\bin\Release\net8.0-windows\CadAssist.Kompas.Interop.exe"

    Write-LauncherLog "ScriptDir: $scriptDir"
    Write-LauncherLog "RepoRoot: $repoRoot"
    Write-LauncherLog "ProjectPath: $projectPath"
    Write-LauncherLog "ReleaseExe: $releaseExe"

    if (-not (Test-Path $projectPath)) {
        throw "CAD Assist project was not found: $projectPath"
    }

    if (-not (Test-Path $ModelPath)) {
        throw "KOMPAS model was not found: $ModelPath"
    }

    if (Test-Path $releaseExe) {
        Write-LauncherLog "Starting release executable."
        $process = Start-Process -FilePath $releaseExe -ArgumentList @(
            "--model-path", $ModelPath,
            "--show-task-window"
        ) -WorkingDirectory $repoRoot -PassThru
    }
    else {
        Write-LauncherLog "Release executable not found. Starting through dotnet run."
        $process = Start-Process -FilePath "dotnet" -ArgumentList @(
            "run",
            "--project", $projectPath,
            "-c", "Release",
            "--",
            "--model-path", $ModelPath,
            "--show-task-window"
        ) -WorkingDirectory $repoRoot -PassThru
    }

    Write-LauncherLog "Process started. PID: $($process.Id)"
    Write-LauncherLog "Launcher finished. If no window is visible, check whether the process is still running and whether KOMPAS is blocked by a modal dialog."
}
catch {
    Write-LauncherLog "ERROR: $($_.Exception.Message)"
    Write-LauncherLog "STACK: $($_.ScriptStackTrace)"
    Write-Host ""
    Write-Host "CAD Assist launcher failed. Log file: $logPath"
    Write-Host "Error: $($_.Exception.Message)"
    if (-not $NoPause) {
        Read-Host "Press Enter to close"
    }
    exit 1
}

if (-not $NoPause) {
    Write-Host ""
    Write-Host "CAD Assist launcher completed. Log file: $logPath"
    Write-Host "If the task window did not appear, keep this console open and check the log file."
    Read-Host "Press Enter to close"
}

param(
    [string]$ModelPath = "C:\cad-assist-test\test.m3d",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$logDirectory = "C:\cad-assist-test"
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
    Set-Content -Path $stdoutPath -Value "" -Encoding UTF8
    Set-Content -Path $stderrPath -Value "" -Encoding UTF8
    if (Test-Path $jsonLogPath) { Remove-Item $jsonLogPath -Force }

    Write-LauncherLog "ModelPath: $ModelPath"
    Write-LauncherLog "StdoutLog: $stdoutPath"
    Write-LauncherLog "StderrLog: $stderrPath"
    Write-LauncherLog "JsonLog: $jsonLogPath"

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
            "--show-task-window",
            "--json-log", $jsonLogPath
        ) -WorkingDirectory $repoRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    }
    else {
        Write-LauncherLog "Release executable not found. Starting through dotnet run."
        $process = Start-Process -FilePath "dotnet" -ArgumentList @(
            "run",
            "--project", $projectPath,
            "-c", "Release",
            "--",
            "--model-path", $ModelPath,
            "--show-task-window",
            "--json-log", $jsonLogPath
        ) -WorkingDirectory $repoRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    }

    Write-LauncherLog "Process started. PID: $($process.Id)"
    Write-LauncherLog "Waiting for CAD Assist process to exit. Close the CAD Assist task window to complete the launcher."
    $process.WaitForExit()
    Write-LauncherLog "Process exited. ExitCode: $($process.ExitCode)"

    if (Test-Path $stdoutPath) {
        Write-LauncherLog "--- stdout begin ---"
        Get-Content -Encoding UTF8 $stdoutPath | ForEach-Object { Write-LauncherLog $_ }
        Write-LauncherLog "--- stdout end ---"
    }

    if (Test-Path $stderrPath) {
        $stderrContent = Get-Content -Encoding UTF8 $stderrPath
        if ($stderrContent.Count -gt 0) {
            Write-LauncherLog "--- stderr begin ---"
            $stderrContent | ForEach-Object { Write-LauncherLog $_ }
            Write-LauncherLog "--- stderr end ---"
        }
    }

    if ($process.ExitCode -ne 0) {
        throw "CAD Assist process failed with exit code $($process.ExitCode). See $stdoutPath, $stderrPath and $jsonLogPath"
    }

    Write-LauncherLog "Launcher finished successfully."
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
    Write-Host "Process output: $stdoutPath"
    Write-Host "Process errors: $stderrPath"
    Write-Host "JSON diagnostics: $jsonLogPath"
    Read-Host "Press Enter to close"
}

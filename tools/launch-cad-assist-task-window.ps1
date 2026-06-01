param(
    [string]$ModelPath = "C:\cad-assist-test\test.m3d"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $repoRoot "src\CadAssist.Kompas.Interop\CadAssist.Kompas.Interop.csproj"

if (-not (Test-Path $projectPath)) {
    throw "CAD Assist project was not found: $projectPath"
}

if (-not (Test-Path $ModelPath)) {
    throw "KOMPAS model was not found: $ModelPath"
}

Start-Process -FilePath "dotnet" -ArgumentList @(
    "run",
    "--project", $projectPath,
    "-c", "Release",
    "--",
    "--model-path", $ModelPath,
    "--show-task-window"
) -WorkingDirectory $repoRoot

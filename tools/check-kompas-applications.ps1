param(
    [switch]$NoPause
)

$ErrorActionPreference = "Continue"
$logDirectory = "C:\cad-assist-test"
$logPath = Join-Path $logDirectory "kompas-applications-diagnostic.log"

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

try {
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Set-Content -Path $logPath -Value "KOMPAS applications diagnostic" -Encoding UTF8

    Write-Log "Machine: $env:COMPUTERNAME"
    Write-Log "User: $env:USERNAME"
    Write-Log "Log: $logPath"

    Write-Log "Searching KOMPAS installation folders..."
    $roots = @(
        "C:\Program Files\ASCON",
        "C:\Program Files (x86)\ASCON",
        "C:\ProgramData\ASCON",
        "C:\Program Files",
        "C:\Program Files (x86)"
    ) | Where-Object { Test-Path $_ }

    foreach ($root in $roots) {
        Write-Log "Root: $root"
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "KOMPAS|Компас|ASCON|АСКОН" } |
            ForEach-Object { Write-Log "  Folder: $($_.FullName)" }
    }

    Write-Log "Searching likely KOMPAS application DLLs. This may take a while..."
    foreach ($root in $roots) {
        try {
            Write-Log "Scanning: $root"
            Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Extension -in @(".dll", ".rtw", ".lta", ".frw") -and
                    $_.FullName -match "KOMPAS|Компас|ASCON|АСКОН|Lib|Library|Application|App|Util|Macro|SDK|API"
                } |
                Select-Object -First 120 |
                ForEach-Object { Write-Log "  File: $($_.FullName)" }
        }
        catch {
            Write-Log "  Scan error for ${root}: $($_.Exception.Message)"
        }
    }

    Write-Log "Searching registry for KOMPAS application/library entries..."
    $registryRoots = @(
        "HKCU:\Software",
        "HKLM:\Software",
        "HKLM:\Software\WOW6432Node"
    ) | Where-Object { Test-Path $_ }

    foreach ($registryRoot in $registryRoots) {
        try {
            Write-Log "Registry root: $registryRoot"
            Get-ChildItem $registryRoot -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match "KOMPAS|Компас|ASCON|АСКОН" } |
                Select-Object -First 120 |
                ForEach-Object { Write-Log "  Registry: $($_.Name)" }
        }
        catch {
            Write-Log "  Registry scan error for ${registryRoot}: $($_.Exception.Message)"
        }
    }

    Write-Log "Checking COM ProgIDs..."
    $progIds = @(
        "KOMPAS.Application.7",
        "Kompas.Application.7",
        "KOMPAS.Application",
        "Kompas.Application"
    )
    foreach ($progId in $progIds) {
        try {
            $type = [type]::GetTypeFromProgID($progId)
            if ($null -eq $type) {
                Write-Log "  ${progId}: not registered"
            }
            else {
                Write-Log "  ${progId}: registered, CLSID=$($type.GUID)"
            }
        }
        catch {
            Write-Log "  ${progId}: error $($_.Exception.Message)"
        }
    }

    Write-Log "Diagnostic finished."
}
catch {
    Write-Log "FATAL ERROR: $($_.Exception.Message)"
    Write-Log "STACK: $($_.ScriptStackTrace)"
}

Write-Host ""
Write-Host "Diagnostic log: $logPath"
if (-not $NoPause) {
    Read-Host "Press Enter to close"
}

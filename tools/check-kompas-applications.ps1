$ErrorActionPreference = "Continue"
$logDirectory = "C:\cad-assist-test"
$logPath = Join-Path $logDirectory "kompas-applications-diagnostic.log"

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Set-Content -Path $logPath -Value "KOMPAS applications diagnostic" -Encoding UTF8

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

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

Write-Log "Searching likely KOMPAS application DLLs..."
$searchRoots = $roots | Select-Object -Unique
foreach ($root in $searchRoots) {
    Get-ChildItem $root -Recurse -Include *.dll,*.rtw,*.lta,*.frw -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -match "KOMPAS|Компас|ASCON|АСКОН|Lib|Library|Application|App|Util|Macro|SDK|API"
        } |
        Select-Object -First 250 |
        ForEach-Object { Write-Log "  File: $($_.FullName)" }
}

Write-Log "Searching registry for KOMPAS application/library entries..."
$registryRoots = @(
    "HKCU:\Software",
    "HKLM:\Software",
    "HKLM:\Software\WOW6432Node"
) | Where-Object { Test-Path $_ }

foreach ($registryRoot in $registryRoots) {
    Get-ChildItem $registryRoot -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "KOMPAS|Компас|ASCON|АСКОН" } |
        Select-Object -First 250 |
        ForEach-Object { Write-Log "  Registry: $($_.Name)" }
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
            Write-Log "  $progId: not registered"
        }
        else {
            Write-Log "  $progId: registered, CLSID=$($type.GUID)"
        }
    }
    catch {
        Write-Log "  $progId: error $($_.Exception.Message)"
    }
}

Write-Log "Diagnostic finished. Send this log back if KOMPAS does not accept the addin DLL."

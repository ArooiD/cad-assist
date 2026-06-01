param(
    [string]$AddinDllPath = "C:\cad-assist-test\addin\CadAssist.Kompas.Addin.dll",
    [switch]$Unregister,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
$logDirectory = "C:\cad-assist-test"
$logPath = Join-Path $logDirectory "cad-assist-addin-registration.log"
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$addinGuid = "{E8D97B81-74F6-4D9E-B76C-47D4B687D9F7}"
$clsidPath = "HKLM:\SOFTWARE\Classes\CLSID\$addinGuid"
$kompasLibraryPath = Join-Path $clsidPath "Kompas_Library"

function Write-RegistrationLog {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

try {
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Set-Content -Path $logPath -Value "CAD Assist KOMPAS addin registration" -Encoding UTF8

    Write-RegistrationLog "AddinDllPath: $AddinDllPath"
    Write-RegistrationLog "RegAsm: $regasm"
    Write-RegistrationLog "CLSID: $addinGuid"

    if (-not (Test-Path $regasm)) {
        throw "RegAsm x64 was not found: $regasm"
    }

    if (-not $Unregister -and -not (Test-Path $AddinDllPath)) {
        throw "Addin DLL was not found: $AddinDllPath"
    }

    if ($VerifyOnly) {
        Write-RegistrationLog "VerifyOnly mode. Registration state will be checked without changes."
    }
    elseif ($Unregister) {
        Write-RegistrationLog "Unregistering addin with RegAsm."
        & $regasm $AddinDllPath /unregister | ForEach-Object { Write-RegistrationLog $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "RegAsm unregister failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-RegistrationLog "Registering addin with RegAsm /codebase."
        & $regasm $AddinDllPath /codebase | ForEach-Object { Write-RegistrationLog $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "RegAsm registration failed with exit code $LASTEXITCODE"
        }
    }

    $clsidExists = Test-Path $clsidPath
    $kompasLibraryExists = Test-Path $kompasLibraryPath
    Write-RegistrationLog "CLSID key exists: $clsidExists"
    Write-RegistrationLog "Kompas_Library key exists: $kompasLibraryExists"

    if (-not $Unregister -and -not $kompasLibraryExists) {
        throw "Kompas_Library registry key was not created: $kompasLibraryPath"
    }

    Write-RegistrationLog "Registration tool finished successfully."
}
catch {
    Write-RegistrationLog "ERROR: $($_.Exception.Message)"
    Write-RegistrationLog "STACK: $($_.ScriptStackTrace)"
    exit 1
}

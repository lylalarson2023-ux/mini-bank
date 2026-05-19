<#
.SYNOPSIS
    Build, test et verifie MBANK_ETUDIANT + ADN_server
.DESCRIPTION
    Script CI/CD local. Verifie que tout compile et que les tests passent.
    Usage: .\build.ps1
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$adn = "C:\Users\hp\ADN_server"

Write-Host "=== MBANK / ADN - BUILD SYSTEM ===" -ForegroundColor Cyan
Write-Host ""

# --- 1. Stop tout processus sur le port 5163 ---
$process = Get-NetTCPConnection -LocalPort 5163 -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "> Arret du serveur MBANK (PID $($process.OwningProcess))..." -ForegroundColor Yellow
    Stop-Process -Id $process.OwningProcess -Force
    Start-Sleep 2
}

# --- 2. Build .NET ---
Write-Host "> Compilation MBANK_ETUDIANT..." -ForegroundColor Yellow
$build = dotnet build "$root\MBANK_ETUDIANT.csproj" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "X ECHEC compilation" -ForegroundColor Red
    exit 1
}
Write-Host "OK Compilation OK" -ForegroundColor Green

# --- 3. Tests ---
Write-Host "> Execution des tests..." -ForegroundColor Yellow
$test = dotnet test "$root\MBANK_ETUDIANT.Tests\MBANK_ETUDIANT.Tests.csproj" --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "X ECHEC tests" -ForegroundColor Red
    exit 1
}
Write-Host "OK Tests OK" -ForegroundColor Green

# --- 4. Verifier Django ---
Write-Host "> Verification Django..." -ForegroundColor Yellow
$python = "$adn\env\Scripts\python.exe"
if (Test-Path $python) {
    $djangoCheck = & $python "$adn\manage.py" check 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: Probleme Django (ignore si migrations non faites)" -ForegroundColor Yellow
    } else {
        Write-Host "OK Django OK" -ForegroundColor Green
    }
} else {
    Write-Host "Warning: Python venv non trouve a $python" -ForegroundColor Yellow
}

# --- Resultat final ---
Write-Host ""
Write-Host "=== BUILD TERMINE AVEC SUCCES ===" -ForegroundColor Cyan

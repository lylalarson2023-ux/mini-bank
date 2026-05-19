<#
.SYNOPSIS
    Démarre MBANK + ADN_server (Django) en un clic.
.DESCRIPTION
    - Tue les process sur les ports 5163 (Blazor) et 8000 (Django)
    - Démarre ADN_server (Django) en arrière-plan
    - Compile et lance MBANK_ETUDIANT (Blazor)
    - Ouvre le navigateur
    - Affiche les URLs pour les testeurs sur le réseau
#>

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$adn = "C:\Users\hp\ADN_server"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   MBANK / ADN - LAUNCH SEQUENCE        " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ─── 1. Tue les process existants ───
Write-Host "> Nettoyage des ports..." -ForegroundColor Yellow

$p5163 = Get-NetTCPConnection -LocalPort 5163 -ErrorAction SilentlyContinue
if ($p5163) { Stop-Process -Id $p5163.OwningProcess -Force; Write-Host "  Port 5163 libere" -ForegroundColor Gray }

$p8000 = Get-NetTCPConnection -LocalPort 8000 -ErrorAction SilentlyContinue
if ($p8000) { Stop-Process -Id $p8000.OwningProcess -Force; Write-Host "  Port 8000 libere" -ForegroundColor Gray }

Start-Sleep 2

# ─── 2. Démarre Django (ADN_server) ───
$python = "$adn\env\Scripts\python.exe"
if (Test-Path $python) {
    Write-Host "> Demarrage ADN_server (Django:8000)..." -ForegroundColor Yellow
    $djangoJob = Start-Process -NoNewWindow -FilePath $python -ArgumentList "$adn\manage.py runserver 0.0.0.0:8000" -PassThru
    Write-Host "  Django PID: $($djangoJob.Id)" -ForegroundColor Gray
    Start-Sleep 3
} else {
    Write-Host "> Python venv non trouve (ADN_server ignore)" -ForegroundColor Yellow
}

# ─── 3. Démarre MBANK (Blazor:5163) ───
Write-Host "> Demarrage MBANK_ETUDIANT (Blazor:5163)..." -ForegroundColor Yellow

try {
    $process = Start-Process -NoNewWindow -FilePath "dotnet" -ArgumentList "run --project `"$root\MBANK_ETUDIANT.csproj`"" -PassThru
    Write-Host "  MBANK PID: $($process.Id)" -ForegroundColor Gray
} catch {
    Write-Host "ERREUR au demarrage : $_" -ForegroundColor Red
    exit 1
}

Start-Sleep 5

# ─── 4. Vérifie que le serveur est bien lancé ───
$check = Get-NetTCPConnection -LocalPort 5163 -ErrorAction SilentlyContinue
if (-not $check) {
    Write-Host "ATTENTION : MBANK n'a pas demarre sur le port 5163" -ForegroundColor Red
    Write-Host "Verifiez les logs ci-dessus pour les erreurs." -ForegroundColor Red
    exit 1
}

# ─── 5. Récupère l'IP locale ───
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
    $_.IPAddress -notmatch "^(127\.|169\.)" -and $_.InterfaceAlias -notmatch "Loopback|Bluetooth|Virtual"
} | Select-Object -First 1).IPAddress

if (-not $ip) { $ip = "192.168.1.55" }

# ─── 6. Affiche les URLs ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   SYSTEME OPERATIONNEL                  " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Acces local      : http://localhost:5163" -ForegroundColor Cyan
Write-Host "  Acces reseau     : http://${ip}:5163" -ForegroundColor Cyan
if (Test-Path $python) {
    Write-Host "  API Django       : http://${ip}:8000/admin" -ForegroundColor Cyan
}
Write-Host ""
Write-Host "  Identifiants admin: admin@mbank.ma / Admin123!" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Appuyez sur Ctrl+C pour arreter." -ForegroundColor Gray

# ─── 7. Ouvre le navigateur ───
Start-Process "http://localhost:5163"

# ─── 8. Attend l'arrêt ───
try {
    $process | Wait-Process
} catch {
    Write-Host "Serveur arrete." -ForegroundColor Yellow
}

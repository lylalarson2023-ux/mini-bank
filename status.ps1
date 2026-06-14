<#
.SYNOPSIS
    État d'ADN_pay en un coup d'œil (feux tricolores).
    Usage : .\status.ps1
#>

$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$port = 5163

# ● coloré + ligne "TITRE   détail"
function Row($color, $title, $detail) {
    Write-Host "   " -NoNewline
    Write-Host ([char]0x25CF) -ForegroundColor $color -NoNewline
    Write-Host ("  {0,-16}" -f $title) -ForegroundColor White -NoNewline
    Write-Host $detail -ForegroundColor Gray
}

Write-Host ""
Write-Host "  ===============================================" -ForegroundColor DarkCyan
Write-Host "     ADN_pay   ·   ÉTAT EN DIRECT" -ForegroundColor Cyan
Write-Host "  ===============================================" -ForegroundColor DarkCyan
Write-Host ""

# 1) APP
try { $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop | Select-Object -First 1 } catch { $conn = $null }
if ($conn) { Row "Green" "APP LANCEE" "http://localhost:$port" }
else       { Row "Red"   "APP ARRETEE" "-> dotnet watch run" }

# 2) PUBLIC / PRIVE
$ssh = Get-Process ssh
if ($ssh) {
    $urlFile = "$env:USERPROFILE\Desktop\ADN_pay_TUNNEL_URL.txt"
    $url = if (Test-Path $urlFile) { (Select-String -Path $urlFile -Pattern 'https://\S+').Matches.Value | Select-Object -First 1 } else { "tunnel actif" }
    Row "Magenta" "PUBLIQUE" $url
} else {
    Row "DarkGray" "PRIVEE" "(local/LAN seulement — .\tunnel.ps1 pour publier)"
}

# 3) BUILD à jour ?
$dll = Join-Path $root "bin\Debug\net10.0\ADN_pay.dll"
if (Test-Path $dll) {
    $buildTime = (Get-Item $dll).LastWriteTime
    $newer = Get-ChildItem $root -Recurse -File -Include *.cs, *.razor, *.csproj, *.css, *.js |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $buildTime } |
        Select-Object -First 1
    if ($newer) { Row "Red"   "BUILD PERIME" "du code a changé -> recompile" }
    else        { Row "Green" "BUILD A JOUR" $buildTime.ToString("dd/MM HH:mm") }
} else {
    Row "DarkGray" "BUILD" "aucun build"
}

# 4) GIT
Push-Location $root
$branch = git rev-parse --abbrev-ref HEAD 2>$null
$desc   = git describe --always --dirty --abbrev=7 2>$null
$dirty  = (git status --porcelain 2>$null | Measure-Object -Line).Lines
Pop-Location
$gitColor = if ($dirty -gt 0) { "Yellow" } else { "Green" }
$gitDetail = if ($dirty -gt 0) { "$desc  ·  $dirty modif(s) non commitée(s)" } else { "$desc  ·  propre" }
Row $gitColor "GIT  $branch" $gitDetail

Write-Host ""

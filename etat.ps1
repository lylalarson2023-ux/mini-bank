<#
.SYNOPSIS
    Tableau de bord visuel de l'état d'ADN_pay (page HTML auto-rafraîchie).
.DESCRIPTION
    Génère etat.html avec l'état courant (app, accès, build, git) sous forme
    de cartes colorées. Avec -Watch : régénère en boucle et ouvre le navigateur.
    Le plus simple : double-cliquer sur etat.bat.
#>
param([switch]$Watch)

$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$port = 5163
$htmlPath = Join-Path $root "etat.html"

function Get-State {
    # --- App ---
    try { $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop | Select-Object -First 1 } catch { $conn = $null }
    $appOn = [bool]$conn

    $ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notmatch "^(127\.|169\.)" -and $_.InterfaceAlias -notmatch "Loopback|Bluetooth|Virtual"
    } | Select-Object -First 1).IPAddress

    # --- Public ---
    $ssh = Get-Process ssh
    $isPublic = [bool]$ssh
    $publicUrl = ""
    if ($isPublic) {
        $urlFile = "$env:USERPROFILE\Desktop\ADN_pay_TUNNEL_URL.txt"
        if (Test-Path $urlFile) { $publicUrl = (Select-String -Path $urlFile -Pattern 'https://\S+').Matches.Value | Select-Object -First 1 }
    }

    # --- Build ---
    $dll = Join-Path $root "bin\Debug\net10.0\ADN_pay.dll"
    $buildTime = $null; $buildFresh = $true
    if (Test-Path $dll) {
        $buildTime = (Get-Item $dll).LastWriteTime
        $newer = Get-ChildItem $root -Recurse -File -Include *.cs, *.razor, *.csproj, *.css, *.js |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $buildTime } | Select-Object -First 1
        $buildFresh = -not $newer
    }

    # --- Git ---
    Push-Location $root
    $branch = git rev-parse --abbrev-ref HEAD 2>$null
    $desc   = git describe --always --dirty --abbrev=7 2>$null
    $dirty  = (git status --porcelain 2>$null | Measure-Object -Line).Lines
    Pop-Location

    [pscustomobject]@{
        AppOn=$appOn; Ip=$ip; IsPublic=$isPublic; PublicUrl=$publicUrl
        BuildTime=$buildTime; BuildFresh=$buildFresh
        Branch=$branch; Desc=$desc; Dirty=$dirty
    }
}

function Card($cls, $dot, $title, $headline, $detail) {
    return @"
    <div class="card $cls">
      <div class="dot"></div>
      <div class="body">
        <div class="title">$title</div>
        <div class="headline">$headline</div>
        <div class="detail">$detail</div>
      </div>
    </div>
"@
}

function Build-Html {
    $s = Get-State

    # App
    if ($s.AppOn) {
        $lan = if ($s.Ip) { "LAN : http://$($s.Ip):$port" } else { "" }
        $cApp = Card "ok" "" "APPLICATION" "En cours d'exécution" "<a href='http://localhost:$port' target='_blank'>http://localhost:$port</a> &nbsp; $lan"
    } else {
        $cApp = Card "ko" "" "APPLICATION" "Arrêtée" "Lancer : <code>dotnet watch run</code>"
    }

    # Accès
    if ($s.IsPublic) {
        $u = if ($s.PublicUrl) { "<a href='$($s.PublicUrl)' target='_blank'>$($s.PublicUrl)</a>" } else { "tunnel actif" }
        $cPub = Card "pub" "" "ACCÈS" "PUBLIQUE" $u
    } else {
        $cPub = Card "muted" "" "ACCÈS" "Privée" "Local / LAN seulement &middot; <code>.\tunnel.ps1</code> pour publier"
    }

    # Build
    if ($null -eq $s.BuildTime) {
        $cBuild = Card "muted" "" "BUILD" "Aucun build" "Compile : <code>dotnet build</code>"
    } elseif ($s.BuildFresh) {
        $cBuild = Card "ok" "" "BUILD" "À jour" "Compilé le $($s.BuildTime.ToString('dd/MM/yyyy HH:mm'))"
    } else {
        $cBuild = Card "ko" "" "BUILD" "Périmé" "Du code a changé depuis le build &middot; recompile"
    }

    # Git
    if ($s.Dirty -gt 0) {
        $cGit = Card "warn" "" "GIT — $($s.Branch)" "$($s.Desc)" "$($s.Dirty) fichier(s) modifié(s) non commité(s)"
    } else {
        $cGit = Card "ok" "" "GIT — $($s.Branch)" "$($s.Desc)" "Working tree propre"
    }

    $now = (Get-Date).ToString("dd/MM/yyyy HH:mm:ss")

    @"
<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta http-equiv="refresh" content="3">
<title>ADN_pay — État</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0d1117; color: #e6edf3;
         min-height: 100vh; display: flex; flex-direction: column; align-items: center; padding: 40px 16px; }
  .head { text-align: center; margin-bottom: 28px; }
  .brand { font-size: 26px; font-weight: 800; letter-spacing: .5px;
           background: linear-gradient(100deg,#0099CC,#30B880); -webkit-background-clip: text; background-clip: text; color: transparent; }
  .sub { color: #8b949e; font-size: 13px; margin-top: 4px; }
  .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; width: 100%; max-width: 720px; }
  .card { position: relative; background: #161b22; border: 1px solid #21262d; border-radius: 14px;
          padding: 20px 20px 20px 26px; display: flex; gap: 14px; overflow: hidden; }
  .card::before { content: ""; position: absolute; left: 0; top: 0; bottom: 0; width: 5px; }
  .dot { width: 13px; height: 13px; border-radius: 50%; margin-top: 5px; flex: none; box-shadow: 0 0 10px currentColor; }
  .title { font-size: 11px; letter-spacing: 1.5px; color: #8b949e; font-weight: 700; }
  .headline { font-size: 20px; font-weight: 700; margin: 3px 0 5px; }
  .detail { font-size: 13px; color: #8b949e; }
  .detail a { color: #58a6ff; text-decoration: none; }
  .detail code { background: #21262d; padding: 2px 7px; border-radius: 6px; color: #e6edf3; font-size: 12px; }
  .ok::before    { background:#30B880 } .ok    .dot { color:#30B880; background:#30B880 }
  .ko::before    { background:#ef4444 } .ko    .dot { color:#ef4444; background:#ef4444 }
  .warn::before  { background:#f59e0b } .warn  .dot { color:#f59e0b; background:#f59e0b }
  .pub::before   { background:#a855f7 } .pub   .dot { color:#a855f7; background:#a855f7 }
  .muted::before { background:#6b7280 } .muted .dot { color:#6b7280; background:#6b7280 }
  .foot { margin-top: 24px; color: #586069; font-size: 12px; }
</style>
</head>
<body>
  <div class="head">
    <div class="brand">ADN_pay</div>
    <div class="sub">Tableau de bord &middot; état en direct</div>
  </div>
  <div class="grid">
$cApp
$cPub
$cBuild
$cGit
  </div>
  <div class="foot">Dernière vérification : $now &middot; rafraîchissement auto toutes les 3 s</div>
</body>
</html>
"@ | Out-File -FilePath $htmlPath -Encoding UTF8
}

if ($Watch) {
    Build-Html
    Start-Process $htmlPath
    Write-Host "Tableau de bord ouvert dans le navigateur." -ForegroundColor Green
    Write-Host "Mise a jour automatique... FERME CETTE FENETRE pour arreter." -ForegroundColor Yellow
    while ($true) { Start-Sleep -Seconds 3; Build-Html }
} else {
    Build-Html
    Write-Host "etat.html genere : $htmlPath" -ForegroundColor Green
}

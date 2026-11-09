<#
.SYNOPSIS
    Teste l'installateur de bout en bout : installation silencieuse, vérifications, désinstallation.

.DESCRIPTION
    Installe dans un dossier temporaire avec la case « Démarrer avec Windows » cochée, vérifie les fichiers,
    la version, le raccourci du menu Démarrer et celui du démarrage, puis désinstalle et vérifie que tout a
    disparu. Utilisé par le contrôle GitHub.

    ATTENTION : le raccourci du menu Démarrer et celui du démarrage sont ceux de l'utilisateur. Sur un PC où
    ACAW est vraiment installé, ce test les remplacerait puis les supprimerait. Il refuse donc de tourner
    hors de GitHub Actions, sauf avec -Force (sur une machine de test).

.PARAMETER Setup
    L'installateur à tester (acaw-setup-x.y.z.exe).
#>
param(
    [Parameter(Mandatory)] [string] $Setup,
    [switch] $Force
)

$ErrorActionPreference = "Stop"
if (-not $env:GITHUB_ACTIONS -and -not $Force) {
    throw "Ce test touche au menu Démarrer et au démarrage de l'utilisateur : à lancer sur GitHub, ou avec -Force sur une machine de test."
}

$dir = Join-Path ([IO.Path]::GetTempPath()) "acaw-test-installation"
$startMenu = Join-Path ([Environment]::GetFolderPath("Programs")) "ACAW.lnk"
$startup = Join-Path ([Environment]::GetFolderPath("Startup")) "ACAW.lnk"
$shell = New-Object -ComObject WScript.Shell

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw "ÉCHEC : $message" }
    Write-Host "ok : $message"
}

# Le désinstalleur se copie ailleurs puis rend la main tout de suite : on attend la fin de son travail.
function Wait-Until([scriptblock] $condition, [int] $seconds = 60) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $condition)) {
        if ($clock.Elapsed.TotalSeconds -gt $seconds) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $true
}

if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }

Write-Host "--- installation silencieuse dans $dir"
$p = Start-Process -FilePath $Setup -Wait -PassThru -ArgumentList @(
    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=`"$dir`"", "/TASKS=startup", "/LOG=`"$dir.install.log`"")
Assert-True ($p.ExitCode -eq 0) "l'installateur se termine sans erreur (code $($p.ExitCode))"
Assert-True (Test-Path (Join-Path $dir "acaw-tray.exe")) "acaw-tray.exe est installé"
Assert-True (Test-Path (Join-Path $dir "unins000.exe")) "le désinstalleur est installé"
$version = & (Join-Path $dir "acaw.exe") --version
Assert-True ($version -match '^ACAW \d+\.\d+\.\d+') "acaw.exe répond « $version »"
Assert-True (Test-Path $startMenu) "le raccourci du menu Démarrer existe"
Assert-True (Test-Path $startup) "le raccourci de démarrage existe"
Assert-True ($shell.CreateShortcut($startup).TargetPath -eq (Join-Path $dir "acaw-tray.exe")) "le démarrage vise la copie installée"

Write-Host "--- désinstallation silencieuse"
Start-Process -FilePath (Join-Path $dir "unins000.exe") -Wait -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"
Assert-True (Wait-Until { -not (Test-Path (Join-Path $dir "acaw.exe")) }) "les fichiers sont supprimés"
Assert-True (-not (Test-Path $startMenu)) "le raccourci du menu Démarrer est supprimé"
Assert-True (-not (Test-Path $startup)) "le raccourci de démarrage est supprimé"

Write-Host "Installation et désinstallation : OK"

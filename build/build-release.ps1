<#
.SYNOPSIS
    Construit une version d'ACAW : publication autonome, installateur, archive portable, empreintes.

.DESCRIPTION
    1. Lit la version dans Directory.Build.props (la seule source de vérité).
    2. Publie acaw-tray.exe et acaw.exe en mode autonome (le runtime .NET est inclus) dans un même dossier.
    3. Vérifie que « acaw.exe --version » annonce bien cette version.
    4. Compile l'installateur Inno Setup (installer\acaw.iss) et zippe la version portable.
    5. Calcule les empreintes SHA-256.

    Utilisé par les contrôles GitHub (ci.yml et release.yml), et à la main :
        powershell -ExecutionPolicy Bypass -File build/build-release.ps1
    Mode d'emploi : docs/guide/09-publier-une-version.md

.PARAMETER Output
    Dossier de sortie (créé, vidé s'il existe), relatif à la racine du dépôt.

.PARAMETER Iscc
    Chemin du compilateur Inno Setup (ISCC.exe). Par défaut, cherché dans le PATH et aux emplacements habituels.
#>
param(
    [string] $Output = "artifacts",
    [string] $Iscc
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Une commande externe qui échoue ne lève pas d'exception en PowerShell : on vérifie son code de sortie.
function Invoke-Checked([string] $what, [scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what a échoué (code $LASTEXITCODE)." }
}

$version = ([xml](Get-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Raw)).SelectSingleNode("//Version").InnerText
if (-not $version) { throw "Version introuvable dans Directory.Build.props." }
Write-Host "ACAW $version"

$out = Join-Path $root $Output
$app = Join-Path $out "app"
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $app | Out-Null

# Les deux programmes dans le même dossier : le runtime .NET n'y est qu'une fois.
foreach ($project in "src/Acaw.Tray", "src/Acaw.Cli") {
    Invoke-Checked "La publication de $project" {
        dotnet publish $project -c Release -r win-x64 --self-contained -p:DebugType=none -o $app
    }
}

$announced = & (Join-Path $app "acaw.exe") --version
if ($announced -ne "ACAW $version") { throw "acaw.exe annonce « $announced », attendu « ACAW $version »." }

$zip = Join-Path $out "acaw-$version-portable.zip"
Compress-Archive -Path (Join-Path $app "*") -DestinationPath $zip

if (-not $Iscc) {
    $candidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $Iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Iscc) { throw "Inno Setup 6 introuvable (ISCC.exe). Installe-le, ou passe -Iscc <chemin>." }
Invoke-Checked "La compilation de l'installateur" {
    & $Iscc /Q "/DAppVersion=$version" "/DSourceDir=$app" "/DOutputDir=$out" (Join-Path $root "installer\acaw.iss")
}

$hashes = Get-ChildItem -LiteralPath $out -File | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$hashes | Set-Content -LiteralPath (Join-Path $out "acaw-$version-sha256.txt") -Encoding ascii

Write-Host ""
Get-ChildItem -LiteralPath $out -File | ForEach-Object { Write-Host ("{0,-40} {1,8:N1} Mo" -f $_.Name, ($_.Length / 1MB)) }

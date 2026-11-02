<#
.SYNOPSIS
    Échoue si la couverture de lignes d'un projet passe sous un seuil.

.DESCRIPTION
    Lit les rapports « coverage.cobertura.xml » produits par
    « dotnet test --collect:"XPlat Code Coverage" » et vérifie le projet demandé (Acaw.Core par défaut).
    Utilisé par le contrôle automatique sur GitHub (.github/workflows/ci.yml), et utilisable à la main :

        dotnet test tests/Acaw.Core.Tests --collect:"XPlat Code Coverage" --results-directory TestResults
        ./build/check-coverage.ps1 -ResultsDirectory TestResults

.PARAMETER ResultsDirectory
    Dossier où chercher les rapports (recherche dans les sous-dossiers).

.PARAMETER Package
    Nom du projet à vérifier, tel qu'il apparaît dans le rapport.

.PARAMETER Threshold
    Seuil minimal, en pourcentage de lignes couvertes.
#>
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [string] $Package = "Acaw.Core",
    [double] $Threshold = 90
)

$ErrorActionPreference = "Stop"

$reports = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml"
if (-not $reports) {
    Write-Error "Aucun rapport de couverture trouvé dans '$ResultsDirectory'."
}

# Chaque projet de test produit son propre rapport : on garde le meilleur chiffre trouvé pour le projet
# demandé (un projet de test qui ne l'exerce pas le verrait à 0 %, à tort).
$best = $null
foreach ($report in $reports) {
    $xml = [xml](Get-Content -LiteralPath $report.FullName -Raw)
    foreach ($node in $xml.coverage.packages.package) {
        if ($node.name -eq $Package) {
            $rate = [double]::Parse($node.'line-rate', [Globalization.CultureInfo]::InvariantCulture) * 100
            if ($null -eq $best -or $rate -gt $best) { $best = $rate }
        }
    }
}

if ($null -eq $best) {
    Write-Error "Le projet '$Package' n'apparaît dans aucun rapport de couverture."
}

$message = "Couverture des lignes de {0} : {1:N1} % (seuil : {2:N1} %)" -f $Package, $best, $Threshold
if ($best -lt $Threshold) {
    Write-Host "::error::$message"   # format reconnu par GitHub : l'erreur s'affiche sur la page du contrôle
    exit 1
}
Write-Host $message

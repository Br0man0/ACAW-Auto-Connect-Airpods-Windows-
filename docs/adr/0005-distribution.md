# ADR-0005 : Distribution d'ACAW (installateur et versions)

**Statut :** Accepté
**Date :** 2026-11-09
**Décideur :** Romsy

## Contexte

Jusqu'à la v0.3, installer ACAW demandait le SDK .NET, un clone du dépôt et deux `dotnet publish`. Les
exécutables produits par le contrôle GitHub dépendaient d'un runtime .NET installé à part, et n'étaient
gardés que 14 jours. Le projet n'avait pas de numéro de version. Contraintes d'ACAW : aucun droit
administrateur (ADR-0001), aucun accès réseau (ADR-0002).

## Décision

| Sujet | Choix | Pourquoi |
|---|---|---|
| Version | **une seule source** : `<Version>` dans `Directory.Build.props` ; affichée par `acaw --version`, le menu de l'icône et le journal | un numéro à changer, un seul endroit |
| Runtime | **publication autonome** (`--self-contained`, win-x64), les deux programmes dans un même dossier | rien à installer en plus ; le runtime n'est présent qu'une fois (~142 Mo installés, 39 Mo d'installateur) |
| Format | **installateur Inno Setup**, **pour l'utilisateur seul** (`PrivilegesRequired=lowest`), dans `%LOCALAPPDATA%\Programs\ACAW` | pas de droits admin ; entrée dans Paramètres → Applications ; désinstalleur ; gratuit |
| En plus | une **archive portable** `.zip` et les **empreintes SHA-256** | pour qui ne veut pas d'installateur, et pour vérifier un téléchargement |
| Démarrage avec Windows | case cochée par défaut ; l'installateur appelle `acaw startup on/off` | **un seul code** pour le menu, la ligne de commande et l'installateur (`StartupShortcut`) |
| Mise à jour | installer la nouvelle version par-dessus ; l'installateur arrête d'abord ACAW **proprement** (`acaw quit`), `taskkill` en dernier recours | un arrêt brutal pendant l'arrivée des AirPods laisserait les haut-parleurs coupés |
| Désinstallation | arrête ACAW, retire le démarrage et les fichiers, puis **demande** s'il faut supprimer réglages et journal | une réinstallation retrouve ses réglages si on le souhaite |
| Publication | un **tag `vX.Y.Z`** déclenche `release.yml` : compilation, tests, installateur testé, GitHub Release | rien à faire à la main, et jamais de Release non testée |
| Contrôle continu | chaque push construit l'installateur et le **teste de bout en bout** (installation et désinstallation silencieuses) | une erreur d'installation se voit tout de suite |

## Options écartées

- **MSIX** : Windows redirigerait les écritures d'ACAW dans `AppData` et dans le registre vers un espace
  privé, exactement le piège vécu du 3 au 9 novembre 2026 (voir le guide 7). Il faudrait aussi le signer.
- **Archive `.zip` et script PowerShell seulement** : pas d'entrée dans les Paramètres, pas de désinstalleur,
  et il faut autoriser l'exécution de scripts.
- **Dépendant du runtime .NET** (~10 Mo) : il faudrait d'abord installer le « .NET 10 Desktop Runtime ».
- **Fichier unique** (`PublishSingleFile`) : chacun des deux programmes embarquerait son propre runtime.
- **Mise à jour automatique** : demanderait un accès réseau (contraire à l'ADR-0002).

## Conséquences

- ✅ Installer ACAW = télécharger `acaw-setup-X.Y.Z.exe` et double-cliquer. Ni SDK, ni droits admin.
- ❌ **Pas de signature de code** : Windows SmartScreen avertit au premier lancement de l'installateur
  (« Informations complémentaires → Exécuter quand même »). Le supprimer demanderait un certificat payant.
- ❌ Taille : ~142 Mo une fois installé, à cause du runtime .NET inclus (WinForms ne peut pas être allégé).
- ✅ Tant que le dépôt est privé, les Releases ne sont visibles que de son propriétaire.
- Pour construire une version à la main : Inno Setup 6 doit être installé (`build/build-release.ps1`).

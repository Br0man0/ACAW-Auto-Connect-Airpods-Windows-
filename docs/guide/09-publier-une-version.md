# 9. Publier une version

Ce guide explique comment une nouvelle version d'ACAW arrive jusqu'à un `acaw-setup-X.Y.Z.exe`
téléchargeable. Les choix sont justifiés dans l'[ADR-0005](../adr/0005-distribution.md).

## Le numéro de version

La version est écrite **à un seul endroit**, `Directory.Build.props` à la racine du dépôt :

```xml
<Version>0.4.0</Version>
```

MSBuild lit ce fichier avant chaque projet : `acaw.exe`, `acaw-tray.exe`, l'installateur et la Release
reprennent tous ce numéro. On le voit avec `acaw --version`, en bas du menu de l'icône, et au début de
chaque lancement dans le journal (« Lancement d'ACAW 0.4.0 : … »).

Règle simple pour choisir le numéro `X.Y.Z` :

| Changement | Exemple | Numéro |
|---|---|---|
| correction sans nouveauté | un bug de l'icône | `0.4.0` → `0.4.1` |
| nouvelle fonction | une option de plus | `0.4.1` → `0.5.0` |
| changement qui casse l'existant | format de `config.json` incompatible | `0.5.0` → `1.0.0` |

## Publier, pas à pas

1. **Changer la version** dans `Directory.Build.props`, puis commiter et pousser comme d'habitude.
   Attendre que le contrôle GitHub soit **vert** (il construit et teste déjà l'installateur).
2. **Créer le tag** de la version (le `v` devant est obligatoire) et le pousser :
   ```bash
   git tag v0.4.0
   ```
   ```bash
   git push origin v0.4.0
   ```
3. Sur GitHub, onglet **Actions**, le workflow **Release** démarre tout seul. Il :
   - vérifie que le tag (`v0.4.0`) correspond à `Directory.Build.props` ;
   - compile avec `-warnaserror` et lance les tests ;
   - construit l'installateur, l'archive portable et les empreintes (`build/build-release.ps1`) ;
   - **teste l'installateur** : installation puis désinstallation silencieuses (`build/test-installer.ps1`) ;
   - crée la **Release** avec les trois fichiers et la liste des commits depuis la version précédente.
4. Onglet **Releases** du dépôt : la version est là. C'est ce lien qu'on donne pour installer ACAW.

Si le workflow échoue, rien n'est publié. Corriger, pousser, puis recréer le tag :

```bash
git tag -d v0.4.0
```

```bash
git push origin :refs/tags/v0.4.0
```

## Les fichiers d'une version

| Fichier | Pour qui |
|---|---|
| `acaw-setup-0.4.0.exe` | **l'utilisateur** : double-clic, installation sans droits administrateur |
| `acaw-0.4.0-portable.zip` | qui ne veut pas d'installateur : décompresser, lancer `acaw-tray.exe` |
| `acaw-0.4.0-sha256.txt` | vérifier qu'un fichier téléchargé n'a pas été abîmé ou modifié |

Vérifier une empreinte :

```bash
Get-FileHash .\acaw-setup-0.4.0.exe -Algorithm SHA256
```

Le résultat doit être identique à la ligne correspondante du fichier `sha256.txt`.

## Construire une version sur son PC

Utile pour tester l'installateur avant de créer le tag. Il faut **Inno Setup 6** (gratuit) :

```bash
winget install --id JRSoftware.InnoSetup -e --scope user
```

Puis, depuis le dossier du dépôt :

```bash
powershell -ExecutionPolicy Bypass -File build/build-release.ps1
```

Tout arrive dans `artifacts\` (ignoré par git). Le test d'installation, lui, **remplace puis supprime** les
raccourcis ACAW du menu Démarrer et du démarrage de l'utilisateur : il refuse donc de tourner ailleurs
que sur GitHub, sauf avec `-Force`, à réserver à une machine de test.

## Ce que fait l'installateur

Le script est `installer/acaw.iss` ([Inno Setup](https://jrsoftware.org/isinfo.php)) :

- **pour l'utilisateur seul** (pas de droits administrateur), dans `%LOCALAPPDATA%\Programs\ACAW` ;
- un raccourci **ACAW** dans le menu Démarrer ;
- une case **« Démarrer avec Windows »**, cochée par défaut, qui appelle `acaw startup on` (ou `off`) ;
- **avant de remplacer les fichiers**, il arrête ACAW proprement avec `acaw quit` : l'hôte s'arrête et
  rétablit le son qu'il aurait coupé. `taskkill` ne sert qu'en dernier recours ;
- il propose de lancer ACAW à la fin.

La désinstallation (Paramètres → Applications → ACAW) arrête ACAW, retire le démarrage avec Windows et les
fichiers, puis **demande** s'il faut aussi supprimer les réglages (`%APPDATA%\ACAW`) et le journal
(`%LOCALAPPDATA%\ACAW`).

> **Deux pièges d'encodage** rencontrés : Inno Setup et Windows PowerShell 5.1 lisent un fichier sans BOM
> comme de l'ANSI, ce qui abîme les accents. `installer/acaw.iss`, `build/build-release.ps1` et
> `build/test-installer.ps1` sont donc enregistrés en **UTF-8 avec BOM**. À garder si on les modifie.

## SmartScreen

L'installateur n'est pas signé (un certificat de signature de code est payant). Au premier lancement,
Windows affiche « Windows a protégé votre ordinateur » : cliquer **Informations complémentaires**, puis
**Exécuter quand même**. Vérifier l'empreinte SHA-256 au préalable permet de s'assurer que le fichier est
bien celui de la Release.

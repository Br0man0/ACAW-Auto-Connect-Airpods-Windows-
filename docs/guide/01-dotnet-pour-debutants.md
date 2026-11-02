# 1. .NET pour débutants

Ce guide donne juste ce qu'il faut pour comprendre et modifier PodHopWin. Il ne remplace pas un cours
de C#, mais il explique chaque notion **là où elle apparaît dans ce projet**.

## Le vocabulaire de base

| Terme | En une phrase | Dans PodHopWin |
|---|---|---|
| **.NET** | La plateforme : un moteur d'exécution + une énorme bibliothèque standard. | Version 10 (support long terme jusqu'en nov. 2028). |
| **C#** | Le langage qu'on écrit. Il ressemble à Java/TypeScript. | Tous les fichiers `.cs`. |
| **Runtime** | Ce qu'il faut pour **exécuter** un programme .NET. | Déjà installé sur ton PC. |
| **SDK** | Ce qu'il faut pour **compiler** (contient aussi le runtime). | .NET SDK 10.0.403. |
| **Projet** (`.csproj`) | Un programme ou une bibliothèque : liste des réglages et des dépendances. | `spikes/Phase0/Phase0.csproj` |
| **Solution** (`.sln`) | Un regroupement de projets, ouvert d'un coup par l'IDE. | `PodHopWin.sln` |
| **NuGet** | Le gestionnaire de paquets (l'équivalent de pip ou npm). | Paquet `Microsoft.Windows.CsWin32`. |
| **TFM** | « Target Framework Moniker » : la cible de compilation. | `net10.0-windows10.0.19041.0` |

> **Le TFM en détail.** `net10.0` = .NET 10. `-windows10.0.19041.0` = « j'utilise des API propres à
> Windows, au minimum celles de Windows 10 version 2004 ». C'est ce suffixe qui donne accès au
> Bluetooth LE (`Windows.Devices.Bluetooth`). Le programme ne tournera donc pas sous Linux ou macOS.

## Les commandes à connaître

Toutes se lancent depuis le dossier `PodHopWin` :

```bash
dotnet build
```
Compile toute la solution. Les fichiers produits vont dans `bin/` et `obj/` (ignorés par git).

```bash
dotnet run --project spikes/Phase0 -- list
```
Compile si besoin, puis lance le programme. **Tout ce qui suit `--` est passé au programme**, pas à `dotnet`.

```bash
dotnet add spikes/Phase0 package NomDuPaquet
```
Ajoute une dépendance NuGet (elle apparaît dans le `.csproj`).

```bash
dotnet clean
```
Efface les fichiers compilés, utile si quelque chose semble « coincé ».

## Lire un fichier `.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>                 <!-- un programme, pas une bibliothèque -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>      <!-- System, System.IO… importés d'office -->
    <Nullable>enable</Nullable>                  <!-- le compilateur traque les null oubliés -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>  <!-- autorise les pointeurs (interop Windows) -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.367" />
  </ItemGroup>
</Project>
```

Il n'y a **pas besoin de lister les fichiers `.cs`** : tous ceux du dossier sont compilés automatiquement.

## Les bases de C# utilisées ici

### Programme « top-level »

`Program.cs` commence directement par du code, sans `class Program { static void Main… }`. C'est le
compilateur qui ajoute l'enveloppe. La variable `args` (les arguments de la ligne de commande) est
disponible d'office.

### Espaces de noms et `using`

```csharp
namespace PodHop.Spike;          // « ce fichier appartient au groupe PodHop.Spike »
using Windows.Win32.Media.Audio; // « je veux utiliser les types de ce groupe sans écrire leur nom complet »
```

### Classes, records, `static`

```csharp
internal sealed record BtEndpoint(string Id, string Name, string? Mac, bool IsA2dp, string KsFilterId);
```

- Un **record** est une classe faite pour **porter des données** : une ligne suffit pour déclarer les
  propriétés, le constructeur, la comparaison et un `ToString()` lisible.
- `internal` = visible seulement dans ce projet. `public` = visible depuis les autres projets.
- `sealed` = on ne peut pas en hériter (plus simple, et légèrement plus rapide).
- Une classe `static` (comme `BluetoothAudio`) ne s'instancie pas : c'est une boîte de fonctions.

### Le `?` et le `null`

`string?` veut dire « une chaîne **ou** rien (null) ». Avec `<Nullable>enable</Nullable>`, le
compilateur prévient si on utilise une valeur peut-être nulle sans vérifier. Opérateurs utiles :

```csharp
ep.Mac ?? "?"            // « Mac, ou "?" si Mac est null »
ep.Mac?.Replace(":", "") // « appelle Replace seulement si Mac n'est pas null »
```

### Les lambdas (fonctions anonymes)

```csharp
WaitFor(ep, s => s == DEVICE_STATE.DEVICE_STATE_ACTIVE, TimeSpan.FromSeconds(30));
```

`s => s == …` est une petite fonction passée en paramètre : « étant donné un état `s`, renvoie vrai
s'il vaut ACTIVE ». On s'en sert aussi pour réagir aux événements (`watcher.Received += (_, args) => …`).

### LINQ : manipuler des listes

```csharp
endpoints.Where(ep => ep.IsA2dp).Select(ep => ep.Name).ToList();
```

« Garde ceux qui sont A2DP, prends leur nom, fais-en une liste. » Ça se lit de gauche à droite comme
une chaîne de traitements.

### `switch` sur une valeur

```csharp
return args[0] switch
{
    "list" => List(),
    "ble"  => Ble(),
    _      => Fail(Usage),   // _ = « tous les autres cas »
};
```

### Exceptions

Les erreurs se « lancent » (`throw`) et se « rattrapent » (`try … catch`). Les appels à Windows
échouent avec une `COMException` qui contient un code `HResult` (ex. `0x800710DF` = appareil non prêt).

## Parler à Windows depuis C# : interop, COM, WinRT

C'est la partie la plus inhabituelle du projet. Trois mondes :

1. **.NET pur** : listes, fichiers, texte… Rien de spécial.
2. **WinRT** (les API « modernes » de Windows, comme `Windows.Devices.Bluetooth`) : utilisables
   presque comme du C# normal grâce au TFM `-windows…`. C'est ce qu'utilise `AppleBle.cs`.
3. **COM** (les API « historiques », dont l'audio Core Audio et Kernel Streaming) : des objets écrits
   en C++ qu'on manipule par des **interfaces** (`IMMDevice`, `IKsControl`…). C'est ce qu'utilise
   `BluetoothAudio.cs`.

Pour le COM, il faut décrire chaque interface en C# exactement comme Windows l'attend (identifiant
GUID, ordre des méthodes). Une erreur → plantage. Plutôt que de l'écrire à la main, on utilise
**CsWin32** :

- On écrit les noms voulus dans `NativeMethods.txt` (un par ligne).
- À la compilation, CsWin32 **génère** le code C# correspondant à partir des métadonnées officielles
  de Microsoft. C'est un « générateur de source » : le code n'apparaît pas dans le dossier, mais
  l'IDE le montre (F12 sur un nom pour voir sa définition).
- Tout ce qui est généré vit sous `Windows.Win32.*` (ex. `Windows.Win32.Media.Audio.IMMDevice`).

**Mémoire allouée par Windows.** Certaines fonctions renvoient une chaîne (`PWSTR`) que Windows a
allouée : c'est à nous de la libérer (`Marshal.FreeCoTaskMem`), sinon la mémoire fuit. C'est le rôle de
`TakeString` dans `BluetoothAudio.cs`.

**`unsafe` et pointeurs.** Quelques appels COM prennent des pointeurs (`void*`). Le C# les autorise
dans des blocs ou méthodes marqués `unsafe`, à condition d'avoir `AllowUnsafeBlocks` dans le `.csproj`.

## Travailler dans un IDE

### Visual Studio (2022 installé)

> **Attention à la version.** .NET 10 est officiellement pris en charge par **Visual Studio 2026**.
> Visual Studio 2022 doit au minimum être à jour (17.14) et peut quand même refuser le projet. Si
> c'est le cas, installe Visual Studio 2026 Community (gratuit) ou utilise VS Code ci-dessous.

1. Lancer Visual Studio Installer → **Modifier** → cocher **« Développement .NET Desktop »**.
2. Ouvrir `PodHopWin.sln`.
3. Clic droit sur `Phase0` → **Propriétés** → **Déboguer** → **Ouvrir l'interface utilisateur des
   profils de lancement** → « Arguments de ligne de commande » : `list` (ou `connect AirPods`…).
4. **F5** lance avec le débogueur ; **F9** pose un point d'arrêt sur la ligne courante ; **F10/F11**
   avancent pas à pas ; survoler une variable montre sa valeur.

### VS Code (installé)

1. Installer l'extension **C# Dev Kit** (Microsoft).
2. Ouvrir le dossier `PodHopWin`.
3. Terminal intégré (Ctrl+ù) → `dotnet run --project spikes/Phase0 -- list`.
4. Pour déboguer : onglet « Exécuter et déboguer » → « C# » → choisir `Phase0`.

## Pour aller plus loin

- [Visite guidée de C#](https://learn.microsoft.com/dotnet/csharp/tour-of-csharp/) (Microsoft, en français)
- [Core Audio APIs](https://learn.microsoft.com/windows/win32/coreaudio/core-audio-apis-in-windows-vista)
- [CsWin32](https://github.com/microsoft/CsWin32)

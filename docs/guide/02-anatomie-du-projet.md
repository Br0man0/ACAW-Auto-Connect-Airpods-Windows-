# 2. Anatomie du projet

## Arborescence (phase 2)

```
AutoConnectAirpodWin (ACAW)/
├─ ACAW.sln                       la solution : ouvre tous les projets d'un coup
├─ README.md                      présentation + démarrage rapide
├─ .gitignore                     exclut bin/, obj/, réglages d'IDE…
├─ mesures-phase0.csv             mesures brutes de la phase 0
├─ docs/
│  ├─ adr/                        décisions d'architecture (le « pourquoi »)
│  ├─ guide/                      ces guides (le « comment »)
│  ├─ protocole-ble-airpods.md    ce qu'on sait des annonces BLE
│  └─ glossaire.md
├─ src/
│  ├─ Acaw.Core/                  LE CERVEAU : logique pure, sans Windows
│  │  ├─ Switching/               machine à états de l'ADR-0003 (événements, commandes, délais)
│  │  ├─ Audio/                   tracker : « est-ce que le PC joue ? »
│  │  ├─ Hosting/                 l'hôte (file d'événements) et ses ports (interfaces)
│  │  ├─ Ble/                     décodage des annonces BLE des AirPods
│  │  └─ Config/                  configuration (JSON)
│  ├─ Acaw.Windows/               LES MAINS : tout ce qui touche Windows (bibliothèque partagée)
│  │  ├─ NativeMethods.txt/.json  API Windows que CsWin32 doit générer
│  │  ├─ Bluetooth/               endpoints audio, reconnexion KS, notifications, WindowsHeadset
│  │  ├─ Audio/                   volume crête (AudioMeter), contrôles média (MediaSessions)
│  │  ├─ Ble/                     écoute BLE (BleScanner, AirPodsRadar)
│  │  └─ FileLog.cs               journal %LOCALAPPDATA%\ACAW\acaw.log
│  ├─ Acaw.Tray/                  acaw-tray.exe : icône + menu (WinForms), sans console
│  └─ Acaw.Cli/                   acaw.exe : setup, status, switch, release
├─ tests/
│  ├─ Acaw.Core.Tests/            unitaires, invariants, intégration de l'hôte (faux ports)
│  └─ Acaw.Windows.Tests/         tests fumée sur les vraies API Windows (lecture seule)
└─ spikes/
   └─ Phase0/                     outil de mesure de la phase 0, figé
```

Dossiers générés à la compilation (jamais versionnés) : `bin/` (les programmes compilés) et `obj/`
(fichiers intermédiaires, dont le code généré par CsWin32).

Ce qui appartient à l'utilisateur n'est **pas** dans le dépôt :
- la configuration : `%APPDATA%\ACAW\config.json` ;
- le journal : `%LOCALAPPDATA%\ACAW\acaw.log`.

## Qui dépend de qui

```
 acaw-tray.exe (Acaw.Tray)      acaw.exe (Acaw.Cli)
              \                     /
               ▼                   ▼
             Acaw.Windows  (adaptateurs Windows)
                    │
                    ▼
             Acaw.Core     (logique pure, testable partout)
```

- Une flèche est une `<ProjectReference>` dans le `.csproj` : le projet du haut peut utiliser les classes
  de celui du bas, **jamais l'inverse**.
- `Acaw.Core` cible `net10.0` tout court : il ne peut pas utiliser d'API Windows, même par erreur. Il décrit
  ce dont il a besoin par des **interfaces** (`Hosting/Ports.cs`) qu'`Acaw.Windows` implémente.
- `Acaw.Tray` et `Acaw.Cli` sont minces : ils assemblent les pièces et affichent.

## Le principe : le Core décide, Windows exécute

```
Acaw.Windows  (Windows)                     Acaw.Core  (pur C#)
───────────────────────                     ───────────────────
son, médias, casque, BLE ──── événements ──► AcawHost → tracker → machine à états
WindowsHeadset, MediaSessions ◄─ commandes ─ (« reconnecte », « mets en pause »)
```

Le détail de l'hôte est dans le [guide 7](07-application-residente.md), les tests dans le
[guide 8](08-strategie-de-test.md).

## Et le spike ?

Le **spike** (`spikes/Phase0`) était un prototype jetable pour répondre à « est-ce faisable, et en
combien de temps ? ». Sa logique utile a été reprise dans `src/`. On le garde tel quel comme **outil de
mesure** : `bench` servira encore en phase 3 pour l'optimisation.

# 2. Anatomie du projet

## Arborescence (phase 1)

```
AutoConnectAirpodWin (ACAW)/
├─ ACAW.sln                       la solution : ouvre les 4 projets d'un coup
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
│  │  ├─ Ble/                     décodage des annonces BLE des AirPods
│  │  └─ Config/                  configuration (JSON)
│  └─ Acaw.App/                   LES MAINS : tout ce qui touche Windows → acaw.exe
│     ├─ NativeMethods.txt        API Windows que CsWin32 doit générer
│     ├─ Bluetooth/               endpoints audio, reconnexion KS, notifications d'état
│     ├─ Ble/                     écoute BLE (WinRT), branchée sur le décodeur du Core
│     └─ Program.cs               commandes : setup, status, switch, release
├─ tests/
│  └─ Acaw.Core.Tests/            tests unitaires du Core (xUnit)
└─ spikes/
   └─ Phase0/                     outil de mesure de la phase 0, figé
```

Dossiers générés à la compilation (jamais versionnés) : `bin/` (les programmes compilés) et `obj/`
(fichiers intermédiaires, dont le code généré par CsWin32).

La configuration de l'utilisateur n'est **pas** dans le dépôt : elle vit dans
`%APPDATA%\ACAW\config.json`, propre à chaque utilisateur Windows.

## Le principe : le Core décide, l'App exécute

```
Acaw.App  (Windows)                         Acaw.Core  (pur C#)
───────────────────                         ───────────────────
Bluetooth, BLE, son ──── événements ───────► SwitchStateMachine
                                             (« la lecture a démarré »)
BluetoothAudio  ◄──────── commandes ─────── (« reconnecte »)
```

- `Acaw.Core` cible `net10.0` tout court : il ne peut **pas** utiliser d'API Windows, même par erreur.
  C'est ce qui permet de le tester en quelques millisecondes, sans AirPods ni Bluetooth.
- `Acaw.App` cible `net10.0-windows10.0.19041.0` et **référence** le Core (`<ProjectReference>` dans
  son `.csproj`) : il peut utiliser ses classes, l'inverse est impossible.
- `Acaw.Core.Tests` référence lui aussi le Core, et rien d'autre.

## Qui fait quoi dans l'App

| Fichier | Monde Windows | Rôle |
|---|---|---|
| `Bluetooth/BluetoothAudio.cs` | COM (Core Audio + Kernel Streaming) | lister les casques, lire leur état, les (dé)connecter |
| `Bluetooth/EndpointWatcher.cs` | COM (`IMMNotificationClient`) | être prévenu quand l'état change, au lieu de le relire en boucle |
| `Ble/BleScanner.cs` | WinRT (`Windows.Devices.Bluetooth.Advertisement`) | écouter les annonces BLE et les passer au décodeur du Core |
| `Program.cs` | — | les commandes `acaw …` |

## Et le spike ?

Le **spike** (`spikes/Phase0`) était un prototype jetable pour répondre à « est-ce faisable, et en
combien de temps ? ». Sa logique utile a été reprise dans `src/` : `BluetoothAudio` presque à
l'identique, le décodage BLE réécrit et testé dans le Core. On le garde tel quel comme **outil de
mesure** : `bench` servira encore en phase 3 pour l'optimisation.

Pour aller plus loin : le [guide 5](05-coeur-et-tests.md) explique le Core et ses tests, le
[guide 6](06-utiliser-acaw.md) l'utilisation d'`acaw`.

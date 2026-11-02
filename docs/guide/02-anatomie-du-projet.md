# 2. Anatomie du projet

## Arborescence actuelle (phase 0)

```
PodHopWin/
├─ PodHopWin.sln               la solution : à ouvrir dans Visual Studio
├─ README.md                   présentation + démarrage rapide
├─ .gitignore                  exclut bin/, obj/, réglages d'IDE…
├─ docs/
│  ├─ adr/                     décisions d'architecture (le « pourquoi »)
│  ├─ guide/                   ces guides (le « comment »)
│  └─ glossaire.md
└─ spikes/
   └─ Phase0/                  outil de mesure JETABLE
      ├─ Phase0.csproj         réglages du projet + dépendance CsWin32
      ├─ NativeMethods.txt     liste des API Windows que CsWin32 doit générer
      ├─ Program.cs            commandes : list, connect, disconnect, bench, ble
      ├─ BluetoothAudio.cs     endpoints audio Bluetooth + reconnexion KS
      └─ AppleBle.cs           écoute et décodage des annonces BLE des AirPods
```

Dossiers générés à la compilation (jamais versionnés) : `bin/` (le programme compilé, `phase0.exe`)
et `obj/` (fichiers intermédiaires, dont le code généré par CsWin32).

## Pourquoi un dossier `spikes/` ?

Un **spike** est un prototype jetable qui répond à une question précise. Celui-ci répond à « est-ce
que la reconnexion KS marche, et en combien de temps ? ». Il est écrit vite, sans tests ni
architecture : **il ne doit pas devenir le vrai programme**. Une fois la phase 0 finie, on le garde
comme outil de mesure, et on écrit le vrai code dans `src/` en suivant l'[ADR-0002](../adr/0002-modele-processus.md).

## Arborescence prévue (après la phase 0)

```
src/PodHop.Core/        logique pure : machine à états, événements, décodage BLE, configuration
                        → ne dépend PAS de Windows, donc testable partout
src/PodHop.App/         tout ce qui touche Windows : audio, Bluetooth, BLE, barre des tâches
tests/PodHop.Core.Tests rejoue des scénarios (« lecture démarre », « iPhone reprend »…)
```

Le principe : **le Core décide, l'App exécute.** Le Core reçoit des événements (« la lecture a
démarré ») et renvoie des commandes (« reconnecte »). Il ne sait pas ce qu'est un `IKsControl`. On
peut donc le tester en quelques millisecondes, sans AirPods.

## Où vit quoi : les deux couches Windows

| Fichier | Monde Windows | Rôle |
|---|---|---|
| `BluetoothAudio.cs` | COM (Core Audio + Kernel Streaming) | voir les casques, leur état, les (dé)connecter |
| `AppleBle.cs` | WinRT (`Windows.Devices.Bluetooth.Advertisement`) | écouter les annonces BLE |

Voir le [guide 3](03-spike-phase0-explique.md) pour le détail du code.

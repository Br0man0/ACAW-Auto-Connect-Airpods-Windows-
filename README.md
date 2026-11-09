# ACAW : Auto Connect AirPods Windows

[![CI](https://github.com/Br0man0/ACAW-Auto-Connect-Airpods-Windows-/actions/workflows/ci.yml/badge.svg)](https://github.com/Br0man0/ACAW-Auto-Connect-Airpods-Windows-/actions/workflows/ci.yml)

Bascule automatique des AirPods entre un PC **Windows** et un iPhone :
- quand le PC se met à jouer du son, les AirPods quittent l'iPhone et viennent sur le PC (~2 s) ;
- quand le PC se met en pause ou se tait, il les relâche, et l'iPhone les reprend (~2–3 s).

Inspiré de PodHop (Linux), avec un mécanisme propre à Windows : on demande à la pile audio Bluetooth de
Windows de connecter ou de relâcher le casque (voir l'[ADR-0001](docs/adr/0001-mecanisme-bascule.md)).
Aucun driver, aucun droit administrateur, aucun accès réseau.

> **État : phase 2.** `acaw-tray.exe` fait la bascule automatique depuis la barre des tâches, sans
> console, et peut démarrer avec Windows. `acaw.exe` sert à la configuration et au diagnostic.

## Démarrage rapide

Prérequis : Windows 10/11, Bluetooth activé, AirPods appairés au PC, [SDK .NET 10](https://dotnet.microsoft.com/download).

1. Retirer le profil « Mains libres » des AirPods (sinon une bascule prend 10 s au lieu de 2) : voir le
   [guide d'utilisation](docs/guide/06-utiliser-acaw.md#une-seule-fois--installer).
2. Construire les deux programmes :
   ```bash
   dotnet publish src/Acaw.Tray -c Release -o "%LOCALAPPDATA%\Programs\ACAW"
   ```
   ```bash
   dotnet publish src/Acaw.Cli -c Release -o "%LOCALAPPDATA%\Programs\ACAW"
   ```
3. Lancer `acaw-tray.exe` depuis ce dossier, puis cocher « Démarrer avec Windows » dans le menu de l'icône.
   S'il n'y a qu'une paire d'AirPods appairée, ACAW la choisit tout seul ; sinon, menu de l'icône → AirPods.

Lancer les tests (159 tests, quelques secondes) :

```bash
dotnet test
```

## Documentation

Écrite pour quelqu'un qui découvre .NET, à lire dans l'ordre :

1. [.NET pour débutants](docs/guide/01-dotnet-pour-debutants.md) : SDK, projets, compilation, bases de C#
2. [Anatomie du projet](docs/guide/02-anatomie-du-projet.md) : à quoi sert chaque dossier
3. [Le spike de la phase 0 expliqué](docs/guide/03-spike-phase0-explique.md) : l'accès au Bluetooth, pas à pas
4. [Protocole de test de la phase 0](docs/guide/04-protocole-test-phase0.md) : les mesures et leurs résultats
5. [Le cœur et ses tests](docs/guide/05-coeur-et-tests.md) : machine à états, `TimeProvider`, xUnit
6. [Utiliser ACAW](docs/guide/06-utiliser-acaw.md) : installation, menu, commandes, dépannage
7. [L'application résidente](docs/guide/07-application-residente.md) : file d'événements, ports, WinForms
8. [Stratégie de test](docs/guide/08-strategie-de-test.md) : pyramide, invariants, recette manuelle
9. [Glossaire](docs/glossaire.md) : Bluetooth, audio Windows, .NET

Référence : [annonces BLE des AirPods, ce qu'on sait](docs/protocole-ble-airpods.md)

Décisions d'architecture :

- [ADR-0001 : mécanisme de bascule](docs/adr/0001-mecanisme-bascule.md)
- [ADR-0002 : modèle de processus](docs/adr/0002-modele-processus.md)
- [ADR-0003 : règles de bascule entre le PC et l'iPhone](docs/adr/0003-regles-de-bascule.md)

## Feuille de route

| Phase | Objectif | État |
|---|---|---|
| 0 | Faisabilité : reconnexion chronométrée, retour iPhone, annonces BLE | ✅ ([résultats](docs/adr/0001-mecanisme-bascule.md#constats-de-la-phase-0)) |
| 1 | Cœur (machine à états, décodage BLE, configuration) + `acaw setup / status / switch / release` | ✅ |
| 2 | Bascule automatique, application barre des tâches sans console, démarrage avec Windows | ✅ code et tests · recette manuelle en cours |
| 3 | Optimisation de la vitesse, mesurée | ⏳ |
| 4 | Pause au retrait d'un écouteur | ⏳ |

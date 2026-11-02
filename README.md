# PodHopWin

Bascule automatique des AirPods vers un PC **Windows** : quand le PC se met à jouer du son, les
AirPods quittent l'iPhone et viennent sur le PC. Quand on reprend l'iPhone, la bascule automatique
d'Apple les ramène.

Version Windows de l'idée de [PodHop](../PodHop) (Linux), avec un mécanisme différent : Windows ne permet
pas d'ouvrir le canal Apple AAP, on demande donc à la pile audio Bluetooth de Windows de reconnecter le
casque (voir [ADR-0001](docs/adr/0001-mecanisme-bascule.md)).

> **État : phase 0 (faisabilité).** Seul un outil de mesure jetable existe (`spikes/Phase0`). Le vrai
> programme sera écrit une fois les tests de la phase 0 concluants.

## Démarrage rapide (5 minutes)

Prérequis : Windows 10/11, Bluetooth activé, AirPods appairés au PC, [SDK .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet build
```

```bash
dotnet run --project spikes/Phase0 -- list
```

```bash
dotnet run --project spikes/Phase0 -- connect AirPods
```

La suite des tests est décrite dans le [protocole de la phase 0](docs/guide/04-protocole-test-phase0.md).

## Documentation

Écrite pour quelqu'un qui découvre .NET, à lire dans l'ordre :

1. [.NET pour débutants](docs/guide/01-dotnet-pour-debutants.md) : SDK, projets, compilation, bases de C#
2. [Anatomie du projet](docs/guide/02-anatomie-du-projet.md) : à quoi sert chaque fichier
3. [Le spike de la phase 0 expliqué](docs/guide/03-spike-phase0-explique.md) : le code, pas à pas
4. [Protocole de test de la phase 0](docs/guide/04-protocole-test-phase0.md) : quoi faire, quoi noter
5. [Glossaire](docs/glossaire.md) : Bluetooth, audio Windows, .NET

Référence : [annonces BLE des AirPods, ce qu'on sait](docs/protocole-ble-airpods.md)

Décisions d'architecture :

- [ADR-0001 : mécanisme de bascule](docs/adr/0001-mecanisme-bascule.md)
- [ADR-0002 : modèle de processus](docs/adr/0002-modele-processus.md)
- [ADR-0003 : règles de bascule entre le PC et l'iPhone](docs/adr/0003-regles-de-bascule.md)

## Feuille de route

| Phase | Objectif | État |
|---|---|---|
| 0 | Spikes : reconnexion chronométrée, retour iPhone, annonces BLE | ✅ A, B, B2, C faits ([résultats](docs/adr/0001-mecanisme-bascule.md#constats-de-la-phase-0)) · comportement après la veille à établir |
| 1 | Cœur + ligne de commande (`switch`, `release`, `status`) | ⏳ |
| 2 | Bascule automatique, barre des tâches, démarrage auto | ⏳ |
| 3 | Optimisation de la vitesse, mesurée | ⏳ |
| 4 | Pause au retrait d'un écouteur, batterie | ⏳ |

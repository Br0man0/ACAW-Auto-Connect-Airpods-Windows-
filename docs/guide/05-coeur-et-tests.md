# 5. Le cœur et ses tests

`Acaw.Core` contient les **décisions** d'ACAW : quand prendre les AirPods, quand les rendre. Ce guide
explique comment c'est construit, puis comment le vérifier par des tests.

## Trois briques : événements, état, commandes

```
   événement                 machine à états                 commandes
PlaybackStarted ──►  ┌──────────────────────────┐  ──►  [Reconnect]
AirPodsSeen     ──►  │ État : Away → Switching  │
Tick            ──►  │ Minuteurs, mémoire BLE   │  ──►  [Disconnect]
                     └──────────────────────────┘
```

**Les événements** (`Switching/SwitchEvent.cs`) décrivent ce qui arrive : « la lecture a démarré »,
« les AirPods ont été vus en BLE », « l'endpoint s'est déconnecté »… Chacun est un `record` vide :

```csharp
public abstract record SwitchEvent;                     // la famille
public sealed record PlaybackStarted : SwitchEvent;     // un membre de la famille
```

Le mot-clé `abstract` veut dire « on ne crée jamais un `SwitchEvent` tout court, seulement un de ses
membres ». C'est une façon simple de dire « voici la liste fermée de ce qui peut arriver ».

**L'état** (`Switching/SwitchState.cs`) est un `enum`, une liste de valeurs nommées : `Away`,
`Switching`, `OnWindows`, `Releasing`, `Lost`.

**Les commandes** (`Switching/SwitchCommand.cs`) sont aussi un `enum` : `Reconnect`, `Disconnect`,
`PauseMedia`. La machine **ne fait rien elle-même** : elle renvoie une liste de commandes, et c'est
l'App qui les exécute.

## La machine à états

`SwitchStateMachine.Handle(événement)` renvoie la liste des commandes à exécuter :

```csharp
public IReadOnlyList<SwitchCommand> Handle(SwitchEvent e) => e switch
{
    PlaybackStarted => OnPlaybackStarted(),   // règle R1
    PlaybackPaused  => OnPlaybackStopped(options.PauseDelay),   // R2
    SilenceDetected => OnPlaybackStopped(options.SilenceDelay), // R3
    ...
};
```

Le `switch` sur le **type** de l'événement aiguille vers la bonne méthode. Chaque méthode applique une
règle de l'[ADR-0003](../adr/0003-regles-de-bascule.md), dont le numéro figure en commentaire.

### Le temps, sans horloge

Les règles parlent de délais (« relâcher 5 s après une pause »). La machine ne lance aucun minuteur :
- elle **note une échéance** (`_releaseAt = maintenant + 5 s`) ;
- l'App lui envoie régulièrement un événement `Tick` ;
- à chaque `Tick`, elle compare l'heure à ses échéances.

« Maintenant » vient d'un `TimeProvider`, la classe standard de .NET pour lire l'heure, passée au
constructeur :

```csharp
public sealed class SwitchStateMachine(SwitchOptions options, TimeProvider time)
```

En production, on passe `TimeProvider.System`, la vraie horloge. En test, on passe une **fausse
horloge** qu'on avance à la main. Un test de « 30 s de silence » dure donc quelques microsecondes.

> Cette façon de recevoir ce dont on a besoin par le constructeur, au lieu d'aller le chercher
> soi-même, s'appelle l'**injection de dépendances**.

## Les tests

Un **test unitaire** est une petite méthode qui met le code dans une situation précise et **vérifie**
le résultat. On les lance tous d'un coup :

```bash
dotnet test
```

Résultat attendu : `Réussi! - échec: 0, réussite: 138` pour le cœur (et 35 de plus pour les tests Windows : 10 tests fumée et 25 sans matériel, sur le démarrage avec Windows, les chemins de driver Bluetooth, la surveillance de `config.json`, le profil mains libres et le signal d'arrêt). Si un test échoue, la sortie dit lequel, ce qui
était attendu et ce qui a été obtenu.

Le projet utilise **xUnit** :
- `[Fact]` marque un test ;
- `[Theory]` + `[InlineData(...)]` marque un test répété avec plusieurs jeux de données ;
- `Assert.Equal(attendu, obtenu)` fait échouer le test si les deux valeurs diffèrent.

### Lire un test

Extrait de `tests/Acaw.Core.Tests/SwitchStateMachineTests.cs` :

```csharp
[Fact]
public void Scenario_de_l_ADR_video_mise_en_pause_puis_AirPods_rendus_apres_5_s()
{
    PlayingOnWindows();              // 1. Situation : AirPods sur le PC, une vidéo joue
    Send(new PlaybackPaused());      // 2. Action : l'utilisateur met en pause

    Assert.Empty(After(4.9));                 // 3. À 4,9 s : rien ne se passe
    Assert.Equal([Disconnect], After(0.2));   //    À 5,1 s : la machine rend les AirPods
    Assert.Equal(SwitchState.Releasing, _machine.State);
}
```

- Le nom du test est une phrase : quand il échoue, on sait tout de suite quel comportement est cassé.
- `After(4.9)` avance la fausse horloge de 4,9 s puis envoie un `Tick`.
- `[Disconnect]` est une liste qui contient une seule commande.

### Ce que couvrent les tests

| Fichier | Vérifie |
|---|---|
| `SwitchStateMachineTests.cs` | les règles R1 à R6 de l'ADR-0003, plus : AirPods endormis, connexion trop longue, connexion non demandée, déconnexion non confirmée |
| `AppleAdvertisementParserTests.cs` | le décodage BLE, sur des trames reconstruites depuis les relevés des tests C1 et C3 |
| `ConfigStoreTests.cs` | l'enregistrement puis la relecture de la configuration |
| `AudioActivityTrackerTests.cs` | « est-ce que le PC joue ? » : médias, sons de jeu, notifications |
| `AcawHostTests.cs` | l’hôte complet avec de faux ports : des situations réelles de bout en bout |
| `InvariantTests.cs` | des milliers de séquences aléatoires et les règles qui doivent toujours tenir |

Le détail de la stratégie (pyramide, invariants, recette) est dans le [guide 8](08-strategie-de-test.md).

### Ajouter un test

1. Copier un test existant proche de ce qu'on veut vérifier.
2. Renommer la méthode avec une phrase qui décrit le comportement.
3. Ajuster la situation, l'action et les vérifications.
4. `dotnet test`. Un nouveau test **doit d'abord échouer** si le comportement n'existe pas encore :
   c'est la preuve qu'il vérifie vraiment quelque chose.

Dans Visual Studio : menu **Test → Explorateur de tests**, puis « Exécuter tout ». Dans VS Code avec
C# Dev Kit : l'icône en forme de fiole dans la barre latérale.

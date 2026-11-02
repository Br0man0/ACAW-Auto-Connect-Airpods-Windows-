# Glossaire

## Bluetooth

| Terme | Définition |
|---|---|
| **Bluetooth classique (BR/EDR)** | Le Bluetooth « historique », utilisé pour le son. |
| **BLE (Bluetooth Low Energy)** | Variante basse consommation, utilisée par les AirPods pour diffuser des annonces (batterie, boîtier ouvert…). |
| **Annonce BLE (advertisement)** | Petit message diffusé à tout le monde, sans connexion. |
| **Appairage (pairing)** | Échange de clés fait une fois ; ensuite les appareils se reconnaissent. |
| **ACL** | La liaison radio de base entre deux appareils en Bluetooth classique. Les AirPods n'en gardent qu'une à la fois. |
| **Paging** | L'action d'« appeler » un appareil appairé pour établir la liaison. |
| **Profil** | Un usage normalisé du Bluetooth. |
| **A2DP** | Profil musique, en stéréo et bonne qualité. C'est lui qu'on connecte. |
| **HFP** | Profil mains libres : micro + son en qualité téléphone. À éviter pour la musique. |
| **AVRCP** | Profil télécommande (lecture/pause depuis le casque). |
| **L2CAP / RFCOMM** | Couches de transport Bluetooth. Windows n'ouvre que RFCOMM aux applications. |
| **AAP** | Protocole de contrôle privé d'Apple pour les AirPods (sur L2CAP). Utilisé par PodHop sous Linux, inaccessible ici. |
| **MAC (adresse Bluetooth)** | Identifiant fixe de l'appareil, ex. `A1:B2:C3:D4:E5:F6`. |
| **Adresse BLE aléatoire** | Adresse qui change régulièrement dans les annonces, pour empêcher le pistage. |
| **Company ID `0x004C`** | Identifiant fabricant d'Apple dans les annonces BLE. |

## Audio Windows

| Terme | Définition |
|---|---|
| **Endpoint** | Une sortie (ou entrée) son vue par Windows : haut-parleurs, casque… |
| **Core Audio / MMDevice** | Les API Windows pour lister les endpoints et suivre leur état. |
| **Sortie par défaut** | L'endpoint où va le son des applications qui ne choisissent pas. |
| **WASAPI** | API audio bas niveau. Permet aussi de savoir quelles applications jouent du son. |
| **Volume crête** | Niveau maximal du son à un instant, de 0 (silence) à 1. ACAW le lit toutes les 250 ms pour détecter les sons sans lecteur média. |
| **SMTC** | « System Media Transport Controls » : ce qui affiche le lecteur dans le panneau du volume. Dit si Spotify, le navigateur… sont en lecture, et permet de les mettre en pause. |
| **Kernel Streaming (KS)** | Couche des drivers audio. Le « filtre KS » Bluetooth est le driver de ton casque. |
| **`IKsControl`** | Interface pour envoyer des propriétés à un filtre KS. |
| **`KSPROPERTY_ONESHOT_RECONNECT`** | Propriété qui demande au driver Bluetooth de connecter le casque. |

## .NET et Windows

| Terme | Définition |
|---|---|
| **SDK / runtime** | Pour compiler / pour exécuter. |
| **Solution, projet** | `.sln` regroupe des `.csproj`. |
| **NuGet** | Gestionnaire de paquets .NET. |
| **TFM** | Cible de compilation, ex. `net10.0-windows10.0.19041.0`. |
| **COM** | Ancien modèle d'objets Windows, toujours au cœur de l'audio. |
| **WinRT** | API Windows modernes (Bluetooth LE, médias…), faciles à utiliser en C#. |
| **Interop** | Faire dialoguer C# avec du code natif Windows. |
| **CsWin32** | Générateur qui écrit le code d'interop à partir de `NativeMethods.txt`. |
| **HRESULT** | Code de résultat Windows ; `0x8…` = erreur. |
| **GUID** | Identifiant unique de 128 bits, ex. `7FA06C40-B8F6-4C7E-8556-E8C33A12E54D`. |
| **Test unitaire** | Petite méthode qui place le code dans une situation précise et vérifie le résultat. Lancés par `dotnet test`. |
| **xUnit** | Bibliothèque de tests utilisée ici : `[Fact]`, `[Theory]`, `Assert.Equal`. |
| **Assertion** | Vérification dans un test (`Assert.…`) : si elle est fausse, le test échoue. |
| **`TimeProvider`** | Classe standard de .NET pour lire l'heure. Remplaçable par une fausse horloge dans les tests. |
| **Injection de dépendances** | Recevoir ce dont on a besoin (horloge, réglages) par le constructeur au lieu d'aller le chercher soi-même. |
| **`record`** | Classe faite pour porter des données : propriétés, comparaison et affichage générés automatiquement. |
| **`enum`** | Liste fermée de valeurs nommées, par ex. les états `Away`, `Switching`… |
| **Interface** | Contrat : « je sais faire ceci », sans dire comment. Plusieurs classes peuvent le remplir (vraie ou fausse). |
| **Ports et adaptateurs** | Architecture où la logique décrit ses besoins par des interfaces (ports), remplies par du code Windows ou par des faux en test (adaptateurs). |
| **Thread** | Fil d'exécution. Plusieurs threads tournent en même temps ; s'ils modifient les mêmes données sans précaution, c'est une **condition de course**. |
| **`Channel`** | File d'attente de .NET : plusieurs threads y déposent, un seul lit. Utilisée par l'hôte pour traiter les événements un par un. |
| **WinExe** | Type d'exécutable sans console (`<OutputType>WinExe</OutputType>`), utilisé par `acaw-tray.exe`. |
| **WinForms** | Bibliothèque d'interface historique de Windows. `NotifyIcon` = icône de la barre des tâches, `ContextMenuStrip` = menu du clic droit. |
| **Test d'intégration** | Test qui assemble plusieurs pièces réelles (ici : l'hôte, la file, le tracker, la machine) et ne remplace que l'extérieur. |
| **Test fumée** | Test rapide qui vérifie que les branchements réels répondent, sans tester les règles en détail. |
| **Invariant** | Règle qui doit toujours être vraie, quelle que soit la suite d'événements. |
| **Couverture** | Part du code exécutée par les tests (lignes, branches). |
| **Recette** | Vérification manuelle, dans les conditions réelles, avant de déclarer une phase terminée. |

## Méthode

| Terme | Définition |
|---|---|
| **ADR** | Architecture Decision Record : une décision, ses options, ses raisons. |
| **Spike** | Prototype jetable qui répond à une question précise. |
| **Médiane / p95** | Valeur sous laquelle se trouvent 50 % / 95 % des mesures. |
| **Machine à états** | Logique qui passe d'un état à l'autre selon les événements (Idle → Switching → OnWindows…). |
| **Cooldown** | Pause imposée après un événement, pour éviter les va-et-vient. |
| **Backoff** | Attente de plus en plus longue entre deux tentatives ratées. |

## Contrôle automatique

| Terme | Définition |
|---|---|
| **CI (intégration continue)** | Compiler et tester automatiquement à chaque modification, pour repérer une erreur tout de suite. |
| **GitHub Actions** | Le service de GitHub qui exécute la CI sur ses propres machines. |
| **Workflow** | Fichier `.github/workflows/*.yml` qui décrit les étapes du contrôle et quand le lancer. |
| **Artefact** | Fichier produit par un contrôle (résultats, exécutables), téléchargeable depuis sa page. |
| **Badge** | Petite image dans le README qui affiche l'état du dernier contrôle (vert ou rouge). |
| **`global.json`** | Fichier qui fixe la version du SDK .NET : la même sur ton PC et sur GitHub. |
| **`-warnaserror`** | Option de compilation qui transforme chaque avertissement en erreur. |

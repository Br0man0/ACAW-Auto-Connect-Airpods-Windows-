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
| **SMTC** | « System Media Transport Controls » : ce qui affiche le lecteur dans le panneau du volume. Dit si Spotify, le navigateur… sont en lecture. |
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

## Méthode

| Terme | Définition |
|---|---|
| **ADR** | Architecture Decision Record : une décision, ses options, ses raisons. |
| **Spike** | Prototype jetable qui répond à une question précise. |
| **Médiane / p95** | Valeur sous laquelle se trouvent 50 % / 95 % des mesures. |
| **Machine à états** | Logique qui passe d'un état à l'autre selon les événements (Idle → Switching → OnWindows…). |
| **Cooldown** | Pause imposée après un événement, pour éviter les va-et-vient. |
| **Backoff** | Attente de plus en plus longue entre deux tentatives ratées. |

# 3. Le spike de la phase 0 expliqué

Ce guide suit le code dans l'ordre où il s'exécute. Ouvre les fichiers à côté pour suivre.

## Vue d'ensemble

```
phase0 connect AirPods
   │
   ├─ Program.cs        Find("AirPods")  → quel casque ?
   │                    Connect(ep)      → lance le chrono
   │
   ├─ BluetoothAudio.cs SendOneShot(RECONNECT)  → demande à Windows de connecter
   │                    GetState(...)           → relu toutes les 25 ms
   │
   └─ Program.cs        « Connecté en 2843 ms »
```

## Étape 1 : trouver le casque (`BluetoothAudio.ListEndpoints`)

Windows représente chaque sortie son par un **endpoint** (« point de terminaison audio ») : les
haut-parleurs, la prise casque, et **chaque casque Bluetooth déjà appairé**, même éteint.

```csharp
Enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATEMASK_ALL, out var collection);
```

- `eRender` = les sorties (le contraire, `eCapture`, serait les micros).
- `DEVICE_STATEMASK_ALL` = tous les endpoints, **même déconnectés**. C'est essentiel : on veut
  justement agir sur un casque qui n'est pas encore connecté.

Pour chaque endpoint, on remonte jusqu'au **driver** qui le gère et on lit son chemin, par exemple :

```
{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_vid&0001004c_pid&2027#7&…&0&a1b2c3d4e5f6_c00000000#{…}\src
        │              │                                    │                    │
        │              └ 0000110b = service « Audio Sink »  └ vid 004c = Apple   └ adresse MAC
        └ bthenum = driver Bluetooth classique
```

Ce chemin nous dit trois choses : c'est du Bluetooth (`bth`), c'est le profil musique A2DP
(`0000110b`), et quelle est la MAC. Les endpoints mains libres (`bthhfenum#bthhfpaudio…`) ne
contiennent **pas** la MAC : c'est une des raisons pour lesquelles on vise toujours l'A2DP.

`Find()` (dans `Program.cs`) filtre ensuite par nom ou par MAC et préfère l'endpoint A2DP.

## Étape 2 : remonter jusqu'au driver (`OpenKsControl`)

Windows décrit le chemin du son comme une **topologie** : une chaîne de pièces reliées par des
connecteurs. L'endpoint est le bout de la chaîne côté applications ; de l'autre côté du premier
connecteur se trouve le **filtre KS** (Kernel Streaming) du driver Bluetooth.

```
IMMDevice (l'endpoint)
   └─ Activate(IDeviceTopology)          « montre-moi ta topologie »
        └─ GetConnector(0)               son connecteur
             └─ GetConnectedTo()         le connecteur d'en face = celui du driver
                  └─ (IPart) GetTopologyObject()   la topologie du driver
                       └─ GetDeviceId()             son identifiant « {2}.\\?\bthenum#… »
                            └─ GetDevice(id)        le driver vu comme un IMMDevice
                                 └─ Activate(IKsControl)   « donne-moi la télécommande du driver »
```

`IKsControl` est une « télécommande » générique : on lui envoie des **propriétés** identifiées par un
jeu (GUID) et un numéro.

> **Le piège.** `IPart` possède lui aussi une méthode `Activate`, et on pourrait croire qu'elle donne
> `IKsControl`. Non : elle répond `E_NOINTERFACE` (« je n'ai pas ça »), que .NET transforme en
> `InvalidCastException`. La télécommande s'obtient sur le **périphérique** du driver, d'où le détour
> par `GetTopologyObject → GetDeviceId → GetDevice`. C'est la première version du spike qui a révélé
> ce piège ; l'outil ToothTray fait de même.

## Étape 3 : envoyer l'ordre (`SendOneShot`)

```csharp
property.Set   = typeof(KSPROPSETID_BtAudio).GUID;      // jeu « audio Bluetooth »
property.Id    = (uint)KSPROPERTY_ONESHOT_RECONNECT;    // 0 = reconnecter, 1 = déconnecter
property.Flags = KSPROPERTY_TYPE_GET;                   // ces propriétés s'utilisent en « lecture »
ks.KsProperty(in property, size, null, 0, out _);
```

C'est exactement ce que fait Windows quand tu cliques sur **Connecter** dans les paramètres Bluetooth.
**L'appel rend la main presque tout de suite** : la connexion radio (appel des AirPods, négociation
A2DP) se fait ensuite, en arrière-plan.

## Étape 4 : chronométrer (`WaitFor`)

Comme l'ordre est asynchrone, on relit l'état de l'endpoint toutes les 25 ms jusqu'à ce qu'il passe
à `DEVICE_STATE_ACTIVE` (ou 30 s maximum : large exprès, pour mesurer aussi les connexions lentes). `Stopwatch` mesure le temps écoulé avec une précision bien
inférieure à la milliseconde.

États possibles d'un endpoint :

| État | Valeur | Sens |
|---|---|---|
| `ACTIVE` | 1 | connecté, utilisable |
| `DISABLED` | 2 | désactivé à la main dans les paramètres son |
| `NOTPRESENT` | 4 | driver absent : le plus souvent, **le Bluetooth du PC est coupé** |
| `UNPLUGGED` | 8 | « débranché » : **c'est l'état d'un casque Bluetooth appairé mais déconnecté** |

> Dans le vrai programme, on ne relira pas l'état en boucle : on s'abonnera aux notifications
> `IMMNotificationClient`, plus précises et sans coût. Le sondage suffit pour un spike.

## Étape 5 : `bench`, répéter et faire des statistiques

`bench` enchaîne N connexions. Entre deux essais, soit il déconnecte lui-même (mode par défaut :
AirPods « libres »), soit il attend que tu remettes les AirPods sur l'iPhone (`--manuel`). Chaque
essai est ajouté à `mesures-phase0.csv`, puis il affiche :

- **médiane** : la moitié des essais sont plus rapides. Plus représentative que la moyenne, car un
  seul essai raté à 30 s ne la fausse pas.
- **p95** : 95 % des essais sont plus rapides. C'est ce que l'utilisateur ressent « dans les mauvais jours ».

## Étape 6 : les annonces BLE (`AppleBle.cs`)

En plus du Bluetooth « classique » (celui du son), les AirPods émettent en permanence de petites
annonces **Bluetooth Low Energy**, comme une balise. C'est grâce à elles que l'iPhone affiche la
fenêtre de batterie quand on ouvre le boîtier.

```csharp
var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
watcher.Received += (_, args) => { … };   // appelé à chaque annonce reçue
watcher.Start();
```

On garde les annonces d'Apple (identifiant fabricant `0x004C`), puis on cherche le bloc de type `0x07`
(« proximity pairing »). Ses premiers octets sont en clair : modèle, statut, batteries, couvercle,
couleur. Les 16 derniers sont chiffrés et changent toutes les quelques secondes (anti-pistage).

Ce qu'on a appris de ces octets est rassemblé dans les [notes BLE](../protocole-ble-airpods.md). Le
spike en tire deux colonnes lisibles : `oreille=` (nombre d'écouteurs portés) et `boîtier=` (nombre
d'écouteurs posés dans le boîtier).

La commande `ble` n'affiche une ligne **que si la partie en clair change**. Ouvre le boîtier, sors un
écouteur, mets-le dans l'oreille : les bits qui bougent te diront lesquels signifient quoi. C'est le
test C du [protocole](04-protocole-test-phase0.md).

> L'adresse BLE affichée (`ble=…`) **n'est pas** la MAC des AirPods : elle est aléatoire et change
> régulièrement, toujours pour empêcher le pistage.

## Ce que ce spike ne fait volontairement pas

- Pas de bascule automatique ni de détection de lecture : ce sera la phase 2.
- Pas de tests unitaires : c'est du code jetable.
- Pas de gestion de la veille ou du Bluetooth coupé en cours de route.

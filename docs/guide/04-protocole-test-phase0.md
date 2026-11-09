# 4. Protocole de test de la phase 0

**But :** décider si l'[ADR-0001](../adr/0001-mecanisme-bascule.md) est validée, avec des mesures
réelles. Compte environ 45 minutes.

## Avant de commencer

- [ ] Bluetooth du PC **activé** (Paramètres → Bluetooth et appareils).
- [ ] AirPods appairés au PC **et** à l'iPhone.
- [ ] iPhone : Réglages → Bluetooth → (i) à côté des AirPods → « Se connecter à cet iPhone » =
      **Automatiquement**.
- [ ] Ouvrir un terminal dans le dossier du dépôt (`AutoConnectAirpodWin (ACAW)`) et compiler :

```bash
dotnet build
```

- [ ] Vérifier que les AirPods apparaissent, avec `profil=A2DP` et une MAC :

```bash
dotnet run --project spikes/Phase0 --no-build -- list
```

Dans la suite, remplace `AirPods` par une partie du nom affiché si besoin.

## Test A : la reconnexion marche-t-elle, et en combien de temps ? (risque R1)

Garde les AirPods **dans les oreilles** (ou boîtier ouvert à côté du PC) pendant tout le test.

**A1, AirPods libres** (connectés à rien) : le programme déconnecte et reconnecte tout seul.

```bash
dotnet run --project spikes/Phase0 --no-build -- bench AirPods --n 10 --label libre
```

**A2, AirPods sur l'iPhone, iPhone sans musique** : avant chaque essai, connecte les AirPods à
l'iPhone (Centre de contrôle → icône AirPlay → AirPods), puis appuie sur Entrée.

```bash
dotnet run --project spikes/Phase0 --no-build -- bench AirPods --n 5 --manuel --label iphone-inactif
```

**A3, AirPods sur l'iPhone, musique en cours sur l'iPhone** : même chose, avec une musique qui joue.

```bash
dotnet run --project spikes/Phase0 --no-build -- bench AirPods --n 5 --manuel --label iphone-lecture
```

**Note pour chaque essai** si le son de l'iPhone s'est bien coupé dans les AirPods.

## Test B : l'iPhone reprend-il les AirPods tout seul ? (risque R2)

1. Connecte les AirPods au PC :
   ```bash
   dotnet run --project spikes/Phase0 --no-build -- connect AirPods
   ```
2. Lance une vidéo ou une musique **sur le PC** (pour que le lien audio soit utilisé).
3. Sur l'iPhone, lance une musique **sans toucher** au Centre de contrôle.
4. Attends 15 secondes et note ce qui se passe :
   - les AirPods passent seuls sur l'iPhone (✅ idéal) ;
   - l'iPhone affiche une notification « Se connecter aux AirPods ? » (🟡 acceptable) ;
   - rien (❌ : il faudra relâcher les AirPods quand le PC est silencieux).
5. Refais l'essai en **arrêtant** la lecture sur le PC à l'étape 2. Ça change quelque chose ?

> Résultat du 2026-11-01 : ❌ dans les deux cas. D'où le test B2.

## Test B2 : si le PC relâche les AirPods, l'iPhone les récupère-t-il ? (risque R2)

C'est ce que ferait ACAW après quelques secondes de silence sur le PC.

1. Connecte les AirPods au PC, mets-les dans les oreilles, et joue 10 s de son sur le PC, puis arrête :
   ```bash
   dotnet run --project spikes/Phase0 -- connect AirPods
   ```
2. iPhone **sans musique**. Relâche les AirPods en lançant un chronomètre (sur ta montre ou ton téléphone) :
   ```bash
   dotnet run --project spikes/Phase0 -- disconnect AirPods
   ```
3. Note s'ils se connectent seuls à l'iPhone (icône AirPods dans le Centre de contrôle, ou réglages
   Bluetooth), et au bout de combien de secondes. Attends jusqu'à 30 s.
4. Recommence avec une musique **en pause** dans Apple Music, puis une troisième fois avec une
   musique **en lecture** sur le haut-parleur de l'iPhone. Les AirPods la reprennent-ils ?
5. Fais chaque variante 2 ou 3 fois.

## Test C : que disent les annonces BLE ? (risques R4, R5)

**C1, décodage.** Lance l'écoute, puis fais les gestes un par un en notant l'heure de chacun :

```bash
dotnet run --project spikes/Phase0 --no-build -- ble --secondes 180
```

1. Boîtier fermé, écouteurs dedans → attendre 20 s
2. Ouvrir le boîtier → attendre 10 s
3. Sortir l'écouteur gauche → 10 s
4. Le mettre dans l'oreille → 10 s
5. Sortir et mettre le droit → 10 s
6. Retirer le gauche de l'oreille → 10 s
7. Tout ranger, fermer le boîtier

Copie la sortie dans un fichier : c'est elle qui permettra d'écrire le vrai décodeur.

> Résultat du 2026-11-01 : analysé dans [les notes BLE](../protocole-ble-airpods.md).

**C3, rythme d'émission.** Les deux écouteurs **dans les oreilles** pendant les 60 s, **sans y toucher
du tout** : ni retrait, ni boîtier. On mesure le rythme « au repos ».
Fais-le deux fois : AirPods connectés à l'iPhone, puis connectés au PC (`phase0 connect AirPods`).

```bash
dotnet run --project spikes/Phase0 -- ble --secondes 60 --modele 2027
```

Note le résumé « Rythme d'émission » affiché à la fin : l'**écart max** de chaque adresse.

**C2, effet du scan BLE sur la vitesse.** Même chose que A1, avec l'écoute BLE active pendant les
mesures :

```bash
dotnet run --project spikes/Phase0 --no-build -- bench AirPods --n 10 --ble --label libre
```

Compare la médiane avec celle de A1.

## Test D : le retrait d'un écouteur se voit-il vite et sans erreur ? (ADR-0004, étape 0)

Avant de coder la pause au retrait, on vérifie que le BLE est assez rapide et assez fiable pour
mettre une vidéo en pause. La commande `oreille` te guide geste par geste. À chaque question, **appuie
sur Entrée au moment même où tu fais le geste** : c'est ce qui déclenche le chronomètre. Le cycle est
toujours le même : retirer un écouteur (2→1), retirer le second (1→0), en remettre un (0→1), remettre
le second (1→2).

> **Avant de commencer :** quitte ACAW (`Stop-Process -Name acaw-tray`), sinon il rendrait les AirPods
> à l'iPhone pendant la mesure. Garde les écouteurs retirés **en main**, sans les poser dans le boîtier.

**D1, AirPods sur le PC, vidéo en lecture.** Connecte les AirPods au PC (`phase0 connect AirPods`),
lance une vidéo YouTube, puis :

```bash
dotnet run --project spikes/Phase0 -- oreille --modele 2027 --n 10 --label pc-video
```

Les lignes `[média]` montrent si Windows reçoit de lui-même une pause quand tu retires un écouteur.

**D2, AirPods sur l'iPhone.** Même chose avec les AirPods connectés à l'iPhone, sans rien jouer sur le
PC (on vérifie que la détection ne dépend pas de l'appareil connecté) :

```bash
dotnet run --project spikes/Phase0 -- oreille --modele 2027 --n 5 --label iphone
```

**D3, stabilité.** Les deux écouteurs portés pendant 10 min, connectés au PC, **sans les toucher** :
bouge, parle, tourne la tête, mâche, penche-toi. Le compte doit rester à 2 tout le temps :

```bash
dotnet run --project spikes/Phase0 -- oreille --modele 2027 --n 0 --libre 600 --label stabilite-pc
```

**Ce que la commande affiche pour chaque geste :**

| Mesure | Sens |
|---|---|
| 1re trame | délai entre Entrée et la première annonce qui montre le nouveau compte. Il peut être négatif si tu as appuyé un peu après le geste |
| confirmé | délai jusqu'à la **deuxième** annonce identique de suite : ce que coûterait un filtre anti-rebond de deux trames |
| trames périmées | annonces reçues après Entrée qui montraient encore l'ancien compte |
| retours | annonces qui reviennent à l'ancien compte après le changement : **devrait rester à 0** |
| valeurs inattendues | un compte qui n'est ni l'ancien ni le nouveau (par exemple 0 en retirant un seul écouteur) : **devrait rester à 0** |
| Windows | changement d'état d'un lecteur média vu par Windows pendant le geste |

Les mesures sont ajoutées à `mesures-oreille.csv` et `mesures-oreille-stabilite.csv` (séparateur `;`).

**Résultat du 2026-11-09 (D1, 3 essais) :** le retrait du premier écouteur (2→1) n'est **jamais** vu ; les
autres gestes le sont en 3 à 6,5 s. Au repos, un seul écouteur émet, et il ne voit pas (ou trop tard) le
retrait de l'autre. Les autres voies (partie chiffrée des annonces, canal AAP par un driver) ont été
étudiées puis écartées : la pause au retrait est **reportée** ([ADR-0004](../adr/0004-pause-au-retrait.md)).
Le test D reste utile pour refaire la mesure après une mise à jour du firmware des AirPods (attendre alors
30 s au lieu de 10).

## Test L : la liaison radio annonce-t-elle l'arrivée des AirPods ? (bascule suivie, étape 0)

**Pourquoi.** Pendant une bascule, ACAW coupe les haut-parleurs et attend la sortie audio des AirPods, avec
une limite de 20 s (`MuteTimeout`). C'est un délai à l'aveugle : la commande de connexion
(`KSPROPERTY_ONESHOT_RECONNECT`) ne dit ni « en cours » ni « échoué ». Une connexion Bluetooth audio se fait
pourtant en deux étapes : la **liaison radio** (ACL), puis le **profil musique** (A2DP), qui fait apparaître la
sortie audio. Si la liaison radio s'établit nettement avant la sortie audio, et seulement quand les AirPods
arrivent vraiment, ACAW pourrait suivre la bascule au lieu de deviner :
- pas de liaison après N s → AirPods injoignables → son rétabli tout de suite ;
- liaison établie → ils arrivent → haut-parleurs coupés jusqu'à l'arrivée, sans limite courte.

**Premier constat (2026-11-09, 22:28).** Liaison radio **connectée** (WinRT et Win32) alors que la sortie audio
était **déconnectée**. La liaison peut donc rester établie sans les AirPods sur le PC. Le test doit dire quand
c'est le cas, et si la liaison reste malgré tout un signal utile.

**L'outil.** `acaw watch` affiche sur une seule ligne de temps, à 50 ms près :

| Source | Ce que c'est |
|---|---|
| `liaison WinRT` | état de la liaison radio poussé par Windows (`BluetoothDevice.ConnectionStatus`) |
| `liaison Win32` | même chose par une autre API (`BluetoothGetDeviceInfo`, relevé toutes les 50 ms), pour recouper |
| `sortie audio` | l'endpoint A2DP : `Connected` = le son va dans les AirPods |
| `journal` | les lignes ajoutées au journal d'acaw-tray, dont `Commande Reconnect` |

Après chaque `Commande Reconnect`, chaque ligne affiche `[+x s depuis Reconnect]` : c'est la mesure.

**Déroulé.** acaw-tray tourne normalement. Dans **ton** terminal (pas celui de Claude) :

```powershell
acaw watch | Tee-Object "$env:USERPROFILE\Desktop\acaw-watch.txt"
```

| # | Situation | Répéter | Ce qu'on cherche |
|---|---|---|---|
| L1 | Micro des AirPods **retiré**, AirPods dans les oreilles sur l'iPhone (musique sur l'iPhone) : lancer une vidéo sur le PC, attendre l'arrivée, mettre en pause, attendre la relâche (5 s) | 5 fois | délais Reconnect → liaison, et liaison → sortie audio |
| L2 | Pareil, AirPods portés mais **rien ne joue** sur l'iPhone | 3 fois | la liaison est-elle déjà là avant le Reconnect ? |
| L3 | Micro des AirPods **coché** (bascules lentes) : comme L1 | 5 fois | où passent les ~10 s : avant ou après la liaison ? |
| L4 | AirPods **dans le boîtier fermé**, posé à côté : menu → « Amener sur ce PC » | 3 fois | que fait la liaison quand ils ne viendront jamais ? (attendre 30 s à chaque fois) |
| L5 | AirPods **dans la poche d'une veste dans une autre pièce**, boîtier ouvert : « Amener sur ce PC » | 2 fois | idem, hors de portée |
| L6 | Sans rien faire, 2 min AirPods sur l'iPhone, puis 2 min AirPods rangés | 1 fois | la liaison bouge-t-elle toute seule ? |

Ctrl+C pour arrêter, puis envoie-moi le fichier `acaw-watch.txt` (Bureau) : je fais le tableau des délais.

**Résultats du 2026-11-09 (L1–L2 : 9 bascules, micro retiré ; L3 : 4 bascules, micro activé).**

| Délai, depuis `Commande Reconnect` | Micro retiré (9) | Micro activé (4) |
|---|---|---|
| liaison radio établie | 0,50 à 3,94 s | 0,37 à 3,81 s |
| **liaison → sortie audio** | **0,68 à 0,85 s** | **7,5 à 12,2 s** |
| sortie audio connectée | 1,31 à 4,65 s | 7,90 à 14,15 s |
| après la relâche : liaison coupée | **3,1 s** après la sortie audio (régulier) | **11 à 21 s** après |

- WinRT et Win32 disent toujours la même chose, à 50 ms près : une seule source suffira.
- La liaison radio s'établit **toujours avant** la sortie audio, et toutes les bascules l'ont eue en moins de
  4 s. Le temps perdu avec le micro est **après** la liaison : c'est la mise en place des deux profils.
- La liaison reste établie ~3 s après chaque relâche : c'est ce qu'a vu le premier constat de 22:28.
- **Défaut trouvé (L3)** : micro activé, la relâche ne coupe que la musique (A2DP). Le micro (HFP) reste
  connecté et garde les AirPods 11 à 21 s ; l'iPhone ne les reprend pas tout seul. Corrigé : la relâche coupe
  aussi le micro (`BluetoothAudio.ReleaseHandsFree`). Le périphérique du micro n'a pas la MAC dans son
  chemin ; on la lit dans son **parent** (`BTHENUM\{…111E…}…MAC…`), par CfgMgr.

**Résultats du 2026-11-09, deuxième session (H9 puis L4–L6, micro activé).**

- **L4–L6 ✅** : AirPods dans le boîtier fermé ou hors de portée, « Amener sur ce PC » 4 fois : **aucune
  liaison radio**, jamais, et ACAW abandonne à 30 s. Une liaison n'apparaît que si les AirPods viennent.
- **H9 ❌, premier correctif inopérant** : la sortie audio du micro n'est pas visible au moment de la relâche
  (`acaw watch` : `micro (HFP)` à « profil retiré », puis `Connected` seulement 20 s après la relâche). Le
  premier correctif la cherchait parmi les sorties audio : il ne trouvait rien, n'envoyait rien, et la
  liaison restait jusqu'à 71 s. L'iPhone ne reprenait les AirPods qu'en les demandant dans ses réglages
  Bluetooth.
- **Second correctif** : l'ordre de déconnexion est envoyé **directement au filtre du driver du micro**
  (`\\?\BTHHFENUM#BthHFPAudio#…#{6994ad04-…}\wave`), retrouvé par CfgMgr à partir de la MAC de son parent.
  Ce filtre existe tant que le profil est activé, connecté ou non, et s'ouvre sans droits administrateur
  (vérifié). Le journal note désormais « Micro des AirPods relâché aussi », ou le code Windows en cas de refus.
- **H9 ✅ (23:19, 4 relâches, micro activé)** : « Micro des AirPods relâché aussi » à chaque fois, liaison
  coupée **3,1 à 3,2 s** après la relâche (comme sans micro, contre 11 à 71 s avant), et l'iPhone reprend
  les AirPods tout seul.

**Verdict du test L** : première ligne du tableau de décision ci-dessous. La liaison arrive toujours après le
Reconnect, toujours en moins de 4 s, bien avant le son quand le micro est activé (7 à 12 s d'écart), et jamais
quand les AirPods ne viendront pas. La « bascule suivie » est réalisable : c'est la prochaine étape, une fois
le H9 validé (fait).

**Décision.**

| Résultat | Suite |
|---|---|
| la liaison apparaît après le Reconnect, ≥ 2 s avant la sortie audio dans les bascules lentes, et jamais en L4/L5 | on code la bascule suivie : délai court sans liaison, coupure jusqu'à l'arrivée avec liaison |
| la liaison est souvent déjà là avant le Reconnect (L2, constat de 22:28) | elle ne dit pas « ils arrivent » : seule sa **disparition** pendant la bascule sert (échec certain → son rétabli tout de suite) ; on garde la limite de 20 s pour le reste |
| la liaison et la sortie audio arrivent ensemble | aucun signal utile : on garde la limite de 20 s, éventuellement relevée |

## Résultats à rapporter

| Mesure | Valeur |
|---|---|
| A1 libre : réussites / médiane / p95 | |
| A2 iPhone inactif : réussites / médiane / p95 / son coupé sur l'iPhone ? | |
| A3 iPhone en lecture : réussites / médiane / p95 / son coupé ? | |
| B : comportement de l'iPhone (✅ / 🟡 / ❌), avec et sans son sur le PC | ❌ / ❌ |
| B2 : reprise par l'iPhone après `disconnect` (oui/non, délai) : sans musique / en pause / en lecture | ✅ ~2–3 s / ✅ / ✅ |
| C1 : sortie de la commande `ble` | ✅ analysée |
| C3 : écart max entre trames (paire), AirPods sur l'iPhone / sur le PC, sans y toucher 5,9 s / 2,6 s (un seul écouteur émet au repos) |
| C2 : médiane avec BLE vs sans | ~1,7 s avec / ~2,1 s sans (contrôle `libre-controle`, mêmes conditions) → pas d'effet du scan |
| D1 : par geste, vus / 1re trame (médiane, p95) / confirmé (p95) / retours / inattendues | 2→1 jamais vu (3 essais) ; autres gestes 3 à 6,5 s → ❌ |
| D1 : Windows reçoit-il une pause des AirPods au retrait ? | |
| D2 : mêmes mesures, AirPods sur l'iPhone | |
| D3 : anomalies en 10 min (nombre, durée max) / écart max entre trames | |

Toutes les mesures chronométrées sont aussi dans `mesures-phase0.csv` (ouvrable dans Excel,
séparateur `;`).

## Décision

| Résultat | Suite |
|---|---|
| A2/A3 réussissent et B ✅ ou 🟡 | ADR-0001 **acceptée** → phase 1 |
| A2/A3 réussissent mais B ❌ | ADR-0001 acceptée, avec relâche obligatoire après silence |
| A1 réussit, A2/A3 échouent | Essayer l'option B (`BluetoothSetServiceState`), sinon décider de l'option C |
| A1 échoue | Diagnostiquer (Bluetooth, adaptateur, droits) avant toute autre chose |

Pour le test D (pause au retrait, ADR-0004) :

| Résultat | Suite |
|---|---|
| retraits (2→1, 1→0) vus à 100 %, p95 de la 1re trame ≤ 3 s, aucun retour ni valeur inattendue, D3 sans anomalie | on code la pause au retrait (étapes 1 à 8) |
| critères tenus avec un filtre de deux trames, mais pas sans | idem, avec un anti-rebond de deux trames (le délai « confirmé » devient le délai réel) |
| retraits vus en plus de 3 s (p95) | fonction utilisable mais lente : à rediscuter avant de coder |
| anomalies en D3 ou valeurs inattendues | risque de pauses intempestives : on s'arrête, ou on cherche un autre signal |

## Dépannage

| Symptôme | Piste |
|---|---|
| « Le Bluetooth du PC est indisponible » | Activer le Bluetooth dans les paramètres Windows. |
| `list` n'affiche pas les AirPods | Les appairer : Paramètres → Bluetooth → Ajouter un appareil, boîtier ouvert, bouton maintenu jusqu'au voyant blanc. |
| `Échec : pas connecté après 30000 ms` | Boîtier fermé ou AirPods trop loin. |
| `Casque absent pour Windows` (état `NOTPRESENT`) | Le Bluetooth du PC est coupé : l'activer. |
| `Windows refuse l'accès au driver… (E_NOINTERFACE)` | Casque mal appairé : le supprimer dans les paramètres Bluetooth et le ré-appairer. |
| `Erreur Windows 0x80070490` | Élément introuvable : le casque a peut-être été désappairé ; relancer `list`. |
| `Plusieurs casques correspondent` | Utiliser un filtre plus précis ou la MAC affichée par `list`. |

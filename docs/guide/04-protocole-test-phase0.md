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

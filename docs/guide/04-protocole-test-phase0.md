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

Toutes les mesures chronométrées sont aussi dans `mesures-phase0.csv` (ouvrable dans Excel,
séparateur `;`).

## Décision

| Résultat | Suite |
|---|---|
| A2/A3 réussissent et B ✅ ou 🟡 | ADR-0001 **acceptée** → phase 1 |
| A2/A3 réussissent mais B ❌ | ADR-0001 acceptée, avec relâche obligatoire après silence |
| A1 réussit, A2/A3 échouent | Essayer l'option B (`BluetoothSetServiceState`), sinon décider de l'option C |
| A1 échoue | Diagnostiquer (Bluetooth, adaptateur, droits) avant toute autre chose |

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

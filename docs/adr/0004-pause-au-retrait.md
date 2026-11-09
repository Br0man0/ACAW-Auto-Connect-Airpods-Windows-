# ADR-0004 : Pause au retrait d'un écouteur

**Statut :** Reportée. Aucune voie sans driver ne donne l'état de chaque écouteur de façon fiable.
**Date :** 2026-11-09
**Décideur :** Romsy

## Contexte

Objectif : comme sur un iPhone, mettre en pause la lecture du PC quand on retire un écouteur, et la
reprendre quand on le remet. Les décisions de conception avaient été validées le 2026-11-02 (reprise à
la remise, pas de relâche des AirPods pendant 2 min, pause à toute baisse 2→1 et 1→0, un `EarTracker`
dans le Core). Une étape 0 devait d'abord vérifier que le signal est assez rapide et fiable : retrait vu
dans 100 % des cas, en 3 s au plus (p95), sans valeur parasite.

## Ce qui a été essayé

### 1. Le statut en clair des annonces BLE : insuffisant

Test D du [guide 04](../guide/04-protocole-test-phase0.md), 3 essais le 2026-11-09 (AirPods sur le PC,
vidéo en lecture) :

| Geste | Résultat |
|---|---|
| retirer le premier écouteur (2→1) | **jamais vu** en 10 s (6 à 12 annonces reçues disaient encore « 2 portés ») |
| les autres gestes | vus en 3 à 6,5 s |

Une capture Wireshark de Romsy, le même jour, explique pourquoi
([notes BLE](../protocole-ble-airpods.md#gauche-et-droite)) : au repos, **un seul écouteur émet**, et les bits
d'oreille sont **relatifs à l'émetteur** (`0x02` = « moi dans l'oreille », `0x08` = « l'autre »). L'émetteur
voit vite son propre retrait, mais celui de l'autre écouteur arrive tard, ou pas du tout. Le comportement
dépendrait donc de l'écouteur retiré : inacceptable pour une fonction de confort.

### 2. La partie chiffrée des annonces : impossible sous Windows seul

Les 16 derniers octets de chaque annonce sont chiffrés (AES-ECB) avec une clé, `accKey`, que le projet
PodHop sait extraire. Mais cette clé est **propre à chaque paire d'AirPods**, et ne s'obtient que par le
canal AAP (voir 3). Chaque utilisateur devrait donc passer par Linux ou par un driver : contraire à
l'objectif d'un ACAW 100 % Windows. De plus, la partie chiffrée est produite par le même écouteur
émetteur, qui ne connaît peut-être pas mieux l'état de l'autre. Piste abandonnée sans test réel.

### 3. Le canal AAP : techniquement faisable, mais bloqué par la signature

AAP (Apple Accessory Protocol) est le canal de contrôle des AirPods : connexion L2CAP classique sur le
PSM `0x1001`, poignée de main, abonnement (`0x0F`), puis les AirPods envoient d'eux-mêmes l'état de
**chaque** écouteur (`00` dans l'oreille, `01` dehors, `02` dans le boîtier), presque instantanément.
C'est ce qu'utilise l'iPhone (voir `docs/PROTOCOL.md` du projet PodHop, et les définitions AAP de LibrePods).

- ✅ Windows expose déjà le service AAP des AirPods comme un périphérique sans driver :
  `BTHENUM\{74EC2172-0BAD-4D01-8F77-997B2BE0722A}_VID&0001004C_PID&2027\…`. Un driver qui déclarerait cet
  identifiant serait chargé automatiquement, sur le modèle de l'exemple BthEcho de Microsoft.
- ❌ Windows n'ouvre pas L2CAP en mode utilisateur (ni Winsock, ni WinRT) : il faut un **driver noyau**.
- ❌ Windows 11 (Secure Boot) ne charge qu'un driver **signé par Microsoft** : compte Partner Center et
  certificat EV payant (quelques centaines d'euros par an), avec vérification d'identité.
- ❌ Le mode test (Secure Boot désactivé) est **incompatible avec Riot Vanguard**, installé sur le PC de
  Romsy : inutilisable au quotidien.
- ❌ `MagicAAP` (le driver de MagicPods) : la version « Community » est signée par un contournement
  (certificat racine maison à importer, politique de drivers de Windows à désactiver, détection possible
  par Defender et les anti-triches) ; la version officielle est liée à MagicPods, sans licence connue
  pour d'autres logiciels. À écarter.
- ⚠️ À savoir pour plus tard : ouvrir le canal AAP **prend** les AirPods à l'appareil qui les tient
  (constaté avec PodHop). Il ne faudrait l'ouvrir que lorsqu'ils sont déjà sur le PC.

## Décision

**La pause au retrait est reportée.** ACAW reste sans driver et sans droits administrateur (ADR-0001).
On ne met pas en pause sur la base d'un signal qui ne suit qu'un écouteur sur deux.

## Ce qui rouvrirait le sujet

- un driver AAP **signé** et utilisable par d'autres logiciels (licence ouverte) qui apparaîtrait ;
- une API Windows qui ouvrirait les canaux L2CAP classiques en mode utilisateur ;
- un certificat de signature, si ACAW devenait un projet distribué qui le justifie ;
- un firmware des AirPods dont les annonces suivraient les deux écouteurs (refaire le test D, en
  attendant 30 s au lieu de 10, avant toute autre chose).

## Ce qui reste utile

- La commande `phase0 oreille` (test D) et `mesures-oreille.csv` : de quoi refaire la mesure en quelques
  minutes après une mise à jour du firmware.
- Les notes BLE sur gauche et droite (bits relatifs à l'émetteur, bit `0x20`) : utilisables pour
  afficher l'état ou les batteries, sans s'en servir pour mettre en pause.
- Le nombre d'écouteurs portés reste affiché par `acaw status`.

## Conséquences

- ✅ ACAW garde sa promesse : aucun driver, aucun droit administrateur, aucun accès réseau.
- ❌ Pas de pause au retrait. L'appui sur la tige de l'AirPod met toujours en pause (recette, ligne 4).
- ✅ Les connaissances acquises sont consignées ici et dans les notes BLE : pas besoin de les
  redécouvrir si le sujet revient.

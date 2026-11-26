<div align="center">

# 🎧 ACAW
### Auto Connect AirPods Windows

**Tes AirPods passent tout seuls de ton iPhone à ton PC Windows, et reviennent.**

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Sans droits admin](https://img.shields.io/badge/droits%20admin-aucun-2E7D32)
![Sans driver](https://img.shields.io/badge/driver-aucun-2E7D32)

**Français** · [English](README.en.md)

<img src="assets/menu.png" alt="Menu d'ACAW dans la barre des tâches" width="380">

</div>

---

## Sommaire

1. [Pourquoi ACAW ?](#pourquoi)
2. [Ce que fait ACAW](#ce-que-fait-acaw)
3. [Ce qu'ACAW ne peut pas faire, et pourquoi](#limites)
4. [Installation](#installation)
5. [Captures d'écran](#captures)
6. [Compiler depuis les sources](#compiler)

---

<a id="pourquoi"></a>
## Pourquoi ACAW ?

Entre appareils Apple, les AirPods changent d'appareil tout seuls : tu lances une vidéo sur ton Mac, ils
quittent ton iPhone. Cette bascule automatique passe par ton compte iCloud, et **Windows n'en fait pas
partie**.

Sur un PC Windows, les AirPods ne sont qu'un casque Bluetooth comme un autre. Résultat, à chaque fois :

- tu ouvres les **paramètres Bluetooth** pour les connecter au PC ;
- quand tu as fini, tu dois **les déconnecter à la main**, sinon ton iPhone ne les récupère jamais :
  l'iPhone ne reprend pas les AirPods à un appareil qui n'est pas Apple.

ACAW est né de cette frustration, en s'inspirant de [PodHop](https://github.com/Mur5h/PodHop), un outil du même genre pour
Linux. Il fait la bascule à ta place, en arrière-plan, depuis une petite icône dans la barre des tâches.

---

<a id="ce-que-fait-acaw"></a>
## Ce que fait ACAW

| | |
|---|---|
| ▶️ **Le PC joue du son** | Les AirPods quittent l'iPhone et arrivent sur le PC en **~2 s**. |
| ⏸️ **Le PC se met en pause** | 5 s plus tard, ACAW rend les AirPods : l'iPhone les reprend en ~2–3 s. |
| 🔇 **Le PC se tait** (jeu, appel terminé…) | Même chose, après 30 s de silence. |
| 🔈 **Pendant l'arrivée des AirPods** | Les haut-parleurs du PC sont coupés : le son ne sort jamais dans la pièce. |
| 📱 **L'iPhone reprend les AirPods**, ou tu les enlèves | La vidéo du PC se met en pause, au lieu de continuer sur les haut-parleurs. |
| 💼 **AirPods rangés ou hors de portée** | ACAW le détecte en moins de 6 s et laisse le son sur les haut-parleurs. |
| 💻 **Au démarrage du PC** | Windows reconnecte parfois les AirPods tout seul : ACAW les rend aussitôt à l'iPhone. |
| 🎙️ **Micro des AirPods** | Une case du menu l'active pour tes appels, et le retire ensuite pour garder des bascules rapides. |

**L'icône dans la barre des tâches** dit où sont tes AirPods :

| Couleur | Signification |
|---|---|
| 🟢 Vert | AirPods sur ce PC |
| 🟠 Orange | Connexion ou relâche en cours |
| 🔵 Gris-bleu | AirPods ailleurs (sur l'iPhone) ou libres |
| ⚪ Gris clair | Bascule automatique suspendue |

**Le menu** (clic droit sur l'icône) permet aussi d'**amener les AirPods sur ce PC** ou de **les rendre à
l'iPhone** à la main, de suspendre la bascule automatique, de choisir d'autres AirPods et de démarrer ACAW avec
Windows.

ACAW n'a besoin d'**aucun driver**, d'**aucun droit administrateur** et d'**aucun accès réseau**. Il ne fait
que demander à Windows de connecter ou de relâcher le casque, comme le ferait un clic dans les paramètres
Bluetooth.

---

<a id="limites"></a>
## Ce qu'ACAW ne peut pas faire, et pourquoi

### ❌ Mettre en pause quand tu retires un écouteur, changer le mode de réduction de bruit

Ces fonctions passent par un protocole propre à Apple, l'**AAP** (*Apple Accessory Protocol*). Les AirPods le
parlent sur un canal Bluetooth bas niveau (L2CAP), et c'est par lui que l'iPhone apprend qu'un écouteur est
retiré, ou change le mode de réduction de bruit et de transparence.

**Le problème, c'est Windows** : une application normale n'a pas le droit d'ouvrir ce type de canal sur un
appareil Bluetooth classique. Windows voit bien le service AAP des AirPods, mais ne fournit aucun moyen de lui
parler. Il faudrait écrire un **driver**, un programme qui tourne au cœur de Windows, et Windows n'accepte de
charger un driver que s'il est **signé par Microsoft** :

- la signature exige un certificat d'entreprise payant (plusieurs centaines d'euros par an) et une validation par
  Microsoft ;
- le contournement existe (le « mode test » de Windows), mais il affaiblit la sécurité du PC, et les anti-triche
  des jeux (Riot Vanguard, par exemple) refusent de fonctionner avec.

Les annonces Bluetooth que les AirPods diffusent librement ne suffisent pas non plus : seul l'écouteur qui
émet y décrit son état, et la partie qui parle des deux écouteurs est chiffrée, avec une clé qu'on ne peut
obtenir… que par l'AAP.

**ACAW reste donc volontairement sans driver** : il s'installe en un clic, sans droits administrateur, et ne
touche à rien d'autre dans Windows.

### ❌ Laisser l'iPhone reprendre les AirPods de lui-même

L'iPhone ne rend la main qu'entre appareils Apple. C'est donc toujours **le PC qui relâche** les AirPods : quand
il se met en pause, quand il se tait, ou quand tu cliques sur « Rendre à l'iPhone ». Si tu lances de la musique
sur l'iPhone pendant que les AirPods sont sur le PC, ils restent sur le PC jusqu'à ce que celui-ci les rende.

### ⚠️ Micro des AirPods et bascules lentes

Pour utiliser le micro des AirPods, Windows active un profil Bluetooth supplémentaire (« mains libres ») qui fait
passer chaque bascule de ~2 s à **10–14 s**. ACAW fonctionne dans les deux cas, mais le plus agréable est de
garder le micro désactivé au quotidien et de le cocher seulement le temps d'un appel (menu → **Micro des
AirPods**).

### ⚠️ Autres casques

ACAW ne pilote que des **AirPods** (et en principe les Beats à puce Apple). Il s'appuie sur les annonces
Bluetooth propres à Apple pour savoir si les AirPods sont sortis du boîtier. Il a été testé sur des AirPods Pro ;
les autres modèles devraient fonctionner de la même façon.

---

<a id="installation"></a>
## Installation

**Il te faut :** Windows 10 ou 11 (64 bits), le Bluetooth activé, et tes AirPods **appairés au PC** (Paramètres
→ Bluetooth et appareils → Ajouter un appareil, boîtier ouvert, bouton du boîtier maintenu jusqu'au voyant blanc).

1. Va dans les [**Releases**](../../releases/latest) et télécharge **`acaw-setup-X.Y.Z.exe`**.
2. Lance-le. Windows peut afficher « Windows a protégé votre ordinateur » : l'installateur n'est pas signé.
   Clique sur **Informations complémentaires → Exécuter quand même**.
3. C'est tout : ACAW s'installe sans droits administrateur (.NET inclus), se lance et démarrera avec Windows.
   S'il n'y a qu'une paire d'AirPods, il la choisit tout seul ; sinon, menu de l'icône → **AirPods**.

**Version portable** : `acaw-X.Y.Z-portable.zip`, à dézipper où tu veux, puis lance `acaw-tray.exe`. Rien
n'est installé.

**Désinstaller** : Paramètres → Applications → ACAW → Désinstaller.

---

<a id="captures"></a>
## Captures d'écran

<div align="center">

| Le menu | Une notification |
|:---:|:---:|
| <img src="assets/menu.png" alt="Menu d'ACAW" width="320"> | <img src="assets/notification.png" alt="Notification d'ACAW" width="380"> |

**L'icône selon l'état**

<img src="assets/icone-etats.png" alt="Couleurs de l'icône d'ACAW" width="640">

</div>

---

<a id="compiler"></a>
## Compiler depuis les sources

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download) :

```bash
dotnet build ACAW.sln -c Release
```

L'application résidente est `src/Acaw.Tray` (`acaw-tray.exe`) ; `src/Acaw.Cli` fournit la ligne de commande
`acaw.exe` (`status`, `switch`, `release`, `handsfree on|off`…).

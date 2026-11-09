# 8. Stratégie de test

**Objectif :** vérifier qu'ACAW fonctionne dans tous les cas possibles et probables, et le revérifier
à chaque modification, en quelques secondes.

## La pyramide

```
             /  Recette manuelle  \        quelques scénarios, avec les vrais AirPods et l'iPhone
            /    Tests Windows      \      9 tests fumée (vraies API, sans rien modifier) + 18 tests (démarrage, chemins de driver, config.json)
           /  Tests d'intégration    \     22 tests de l'hôte + 18 du coordinateur de configuration, avec de faux Windows
          /  Invariants (aléatoire)   \    des milliers de séquences générées
         /     Tests unitaires         \   machine, tracker, décodeur, configuration
```

Plus on monte, plus un test est réaliste, mais lent et difficile à automatiser. On en met donc beaucoup
en bas et peu en haut.

| Niveau | Projet | Ce qu'il vérifie | Quand le lancer |
|---|---|---|---|
| Unitaires | `Acaw.Core.Tests` | chaque règle de l'ADR-0003, le décodage BLE, la configuration | à chaque modification |
| Invariants | `Acaw.Core.Tests/InvariantTests.cs` | des règles qui doivent tenir **quelle que soit** la suite d'événements | à chaque modification |
| Intégration | `Acaw.Core.Tests/AcawHostTests.cs` | des situations de la vie réelle, de bout en bout, avec de faux ports | à chaque modification |
| Fumée | `Acaw.Windows.Tests` | que les vraies API Windows répondent (après une mise à jour de Windows, de .NET…) | avant chaque version |
| Recette | ce guide, plus bas | le comportement réel avec les AirPods et l'iPhone | à la fin de chaque phase |

Tout lancer :

```bash
dotnet test
```

Seulement ce qui ne dépend pas de Windows (sur une autre machine, par exemple) :

```bash
dotnet test --filter "Categorie!=Windows"
```

## Le contrôle automatique (GitHub Actions)

À chaque `git push` sur `main` et à chaque pull request, GitHub lance tout seul, sur une machine Windows
neuve, le fichier [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) :

| Étape | Échoue si… |
|---|---|
| installer .NET (version fixée par `global.json`) | — |
| compiler en Release avec `-warnaserror` | une erreur, **ou le moindre avertissement** |
| lancer les tests (sans les tests fumée) | un seul test échoue |
| vérifier la couverture (`build/check-coverage.ps1`) | `Acaw.Core` passe sous 90 % des lignes couvertes |
| publier `acaw-tray.exe` et `acaw.exe` | l'application ne se construit pas |

Les **tests fumée** ne tournent pas sur GitHub : la machine n'a ni Bluetooth ni carte son, ils y
échoueraient pour de mauvaises raisons. Lance-les sur ton PC avant chaque version (`dotnet test`).

**Où voir le résultat ?**
- le badge en haut du README : vert = tout passe, rouge = quelque chose a cassé ;
- sur GitHub, onglet **Actions** : la liste des contrôles, avec le détail de chaque étape ;
- à côté de chaque commit sur GitHub : ✓ vert ou ✗ rouge.

**Quand c'est rouge :**
1. Onglet Actions → cliquer sur le contrôle rouge → ouvrir l'étape en ✗ : le message dit quel test ou
   quel avertissement pose problème.
2. En bas de la page, **Artifacts** : `resultats-tests` (détail des tests et couverture) et `acaw-windows`
   (les exécutables construits, utiles pour essayer une version sans compiler).
3. Reproduire en local, puis corriger :
   ```bash
   dotnet build -c Release -warnaserror
   ```
   ```bash
   dotnet test --filter "Categorie!=Windows"
   ```
4. Pour relancer un contrôle sans rien changer : bouton **Re-run jobs**. Pour en lancer un à la main :
   onglet Actions → **CI** → **Run workflow**.

Vérifier la couverture à la main :

```bash
dotnet test tests/Acaw.Core.Tests --collect:"XPlat Code Coverage" --results-directory TestResults
```

```bash
powershell -ExecutionPolicy Bypass -File build/check-coverage.ps1 -ResultsDirectory TestResults
```

> `TestResults/` est ignoré par git. Le seuil de 90 % a été vérifié dans les deux sens : il passe à 98,4 %
> et bloque bien quand on l'élève à 99,9 %.

## Les tests d'invariants : « tous les cas possibles »

On ne peut pas écrire à la main toutes les combinaisons d'événements. Les tests d'invariants en
**génèrent** : 2 000 séquences de 200 événements tirés au hasard, y compris des enchaînements absurdes
(« connecté » deux fois de suite, « relâche » sans AirPods…), avec des écarts de temps aléatoires. Après
chaque événement, on vérifie des règles qui doivent **toujours** tenir :

1. une annonce BLE ne déclenche rien (sauf le rétablissement du son déjà dû, qui sort au premier
   événement venu) ;
2. `Reconnect` ne répond qu'à une lecture, une demande ou la fin d'une relâche ;
3. une lecture ne prend jamais des AirPods endormis ;
4. pas de reprise automatique pendant le cooldown (anti ping-pong) ;
5. `Disconnect` ne part que si les AirPods sont sur le PC (ou en route) ;
6. `PauseMedia` seulement quand le PC rend ou perd les AirPods ;
7. jamais deux fois la même commande, jamais `Reconnect` et `Disconnect` ensemble ;
8. un battement ne fait jamais « apparaître » les AirPods ;
9. les haut-parleurs ne sont coupés qu'en appelant les AirPods, jamais deux fois de suite, et on ne
   rétablit que ce qu'on a coupé ;
10. **le PC n'est jamais laissé muet** : la coupure ne survit ni à un abandon, ni à une perte, ni à une
    relâche, et ne dépasse jamais 6 s.

Le tracker audio a le sien : début et fin de lecture alternent toujours.

Le **coordinateur de configuration** a les siens (`ConfigCoordinatorInvariantTests`, 1 000 séquences de 60
opérations) : fichiers modifiés, abîmés, verrouillés ou supprimés, casques qui apparaissent et
disparaissent, créations d'hôte qui échouent, clics dans le menu. Après chaque opération :

1. jamais deux hôtes en marche en même temps ;
2. l'hôte que voit l'icône est exactement celui qui tourne ;
3. un hôte ne démarre qu'après l'arrêt de tous ceux d'avant (créer → arrêter l'ancien → démarrer) ;
4. l'hôte en marche a une configuration utilisable, celle qu'annonce le coordinateur, et aucun problème
   n'est affiché ;
5. une simple relecture n'écrase jamais un fichier abîmé ;
6. la sélection automatique ne choisit jamais au hasard entre plusieurs paires.

> L'ajout de la règle 10 a fait échouer la règle 1 : le rétablissement du son peut sortir sur une annonce
> BLE, si c'est le premier événement après l'échéance. C'est voulu (le son doit revenir dès que possible) :
> la règle 1 a été précisée. C'est exactement le rôle des invariants, révéler les interactions entre règles.

Le hasard est **à graine fixe** : la même graine redonne exactement la même séquence. Quand un invariant
casse, le message donne la graine et l'étape, par exemple
`graine 55433, étape 41 : Away --PlaybackStarted--> Switching [Reconnect]`. On peut alors rejouer le cas.

**Ces tests détectent-ils vraiment les erreurs ?** Vérifié pendant la phase 2 : on a volontairement retiré
la vérification « AirPods éveillés » de la machine. Les tests de scénario **et** les invariants ont échoué
aussitôt, en désignant la règle 3. Même vérification pour la limite de 6 s de la coupure du son (règle 10)
et pour les corrections de la revue de code. Les tests ont ensuite été relancés sur le code restauré.

## Les tests d'intégration : des situations réelles

`AcawHostTests` assemble le vrai hôte, la vraie file, le vrai tracker, la vraie machine et le vrai
minuteur. Seuls Windows et les AirPods sont remplacés par des faux, et une **fausse horloge** fait passer
30 s en une microseconde. Situations couvertes :

| Situation | Attendu |
|---|---|
| vidéo YouTube puis pause | AirPods appelés, puis rendus 5 s après la pause |
| vidéo pendant l'arrivée des AirPods | haut-parleurs coupés, rétablis 1 s après l'arrivée |
| démarrage après un arrêt brutal | le son coupé par ACAW est rétabli |
| « ding » Discord de 1,25 s | rien |
| jeu qui fait du son 2,5 s | AirPods appelés après 2 s |
| boîtier fermé (aucune annonce BLE) | rien |
| bascule automatique suspendue | rien, mais « Amener sur ce PC » marche |
| bascule réactivée pendant une lecture | AirPods appelés |
| AirPods déjà connectés au lancement | état « sur ce PC » |
| iPhone qui reprend les AirPods pendant une vidéo | vidéo mise en pause, pas de reprise |
| notifications Windows en double | comptées une seule fois |
| ordre Windows qui échoue | erreur notée, l'application continue |
| écoute BLE arrêtée ou impossible | relancée toutes les 10 s |
| sortie de veille | tout est relu, écoute BLE relancée |
| rafale de 50 annonces BLE | une seule entrée dans la file |
| journal impossible à écrire | l’application continue de basculer |
| casque reconnecté par Windows sans notification | vu dans les 30 s |
| message « ça joue » arrivé en retard | ignoré : on relit l’état réel |
| file traitée en arrière-plan | oui, sans appel manuel |
| hôte arrêté pendant un ordre Windows | l'arrêt attend la fin de l'ordre, puis plus rien ne part |

`ConfigCoordinatorTests` raconte de la même façon la configuration : premier lancement avec une seule
paire (choisie toute seule), plusieurs paires (rien au hasard, un seul message), liste vide à
l'ouverture de session (pas de bulle), délai modifié (nouvel hôte créé avant l'arrêt de l'ancien),
création qui échoue (l'ancien continue), fichier abîmé ou verrouillé, AirPods remplacés, choix dans le menu.

## La couverture

La **couverture** mesure la part du code exécutée par les tests :

```bash
dotnet test tests/Acaw.Core.Tests --collect:"XPlat Code Coverage"
```

| Zone | Lignes | Branches | Objectif |
|---|---|---|---|
| `Acaw.Core` (logique) | 97 % | — | ≥ 90 % |
| `Acaw.Windows` (branchements) | — | — | couvert par les tests fumée et la recette |
| `Acaw.Tray`, `Acaw.Cli` (affichage) | — | — | couvert par la recette |

Une couverture élevée ne prouve pas que le code est juste ; elle montre surtout ce qui n'est **jamais**
essayé. C'est pour ça qu'on la complète par les invariants et par la vérification « le test échoue-t-il
quand on casse le code ? ».

## Ce qui n'est pas automatisable (et pourquoi)

| Zone | Raison | Couverture |
|---|---|---|
| connexion radio réelle | il faut de vrais AirPods et un iPhone | recette |
| interface (icône, menu) | WinForms se teste mal automatiquement | recette |
| lancement réel à l'ouverture de session | il faut fermer puis rouvrir la session | recette (ligne 13). Le raccourci et le registre, eux, sont testés automatiquement dans un dossier et une clé temporaires (`StartupShortcutTests`), sans toucher au vrai démarrage. **Leçon du 9 novembre 2026 :** ces tests prouvent que le code écrit ce qu'il doit, pas que Windows s'en sert, ni que l'écriture arrive au bon endroit (voir le piège MSIX du guide 07). Seule la recette le prouve |

## Recette manuelle

À faire à la fin de chaque phase, avec `acaw-tray.exe` lancé et les AirPods dans les oreilles. Le journal
(menu « Ouvrir le journal ») sert de preuve pour chaque ligne.

> **Lancer ACAW et `acaw setup` depuis un terminal Windows ordinaire** (ou par le raccourci du menu
> Démarrer), jamais depuis un outil empaqueté comme l'application Claude : ses écritures dans `AppData` et
> dans le registre seraient redirigées ailleurs, et la recette testerait une autre configuration que la
> vraie (voir le guide 07, « Démarrer avec Windows »).

| # | Geste | Attendu | ✓ |
|---|---|---|---|
| 1 | lancer `acaw-tray.exe` | aucune console, icône dans la barre des tâches | |
| 2 | AirPods sur l'iPhone, lancer une vidéo YouTube sur le PC | AirPods sur le PC en ~2 s, icône verte, **aucun son sur les haut-parleurs** | |
| 3 | mettre la vidéo en pause | AirPods rendus ~5 s plus tard, l'iPhone les reprend | |
| 4 | relancer la vidéo, puis appuyer sur la tige d'un AirPod | la vidéo se met en pause (= pause explicite) | ✅ 2026-11-02 |
| 5 | recevoir une notification (Discord, Teams…) sans rien jouer | rien ne bouge | |
| 6 | vidéo sur le PC, prendre les AirPods par le Centre de contrôle de l'iPhone | vidéo mise en pause sur le PC, pas de reprise | |
| 7 | menu « Amener sur ce PC » sans rien jouer | AirPods sur le PC, et ils y restent | |
| 8 | menu « Rendre à l'iPhone » | AirPods rendus | |
| 9 | décocher « Bascule automatique », lancer une vidéo | rien ; icône gris clair | |
| 10 | boîtier fermé, lancer une vidéo | rien : le son sort normalement des haut-parleurs, sans coupure | |
| 10b | AirPods sortis mais posés loin (autre pièce), lancer une vidéo | au pire 6 s de silence, puis le son revient sur les haut-parleurs | |
| 11 | mettre le PC en veille 1 min, réveiller, lancer une vidéo | la bascule marche encore | à faire |
| 12 | couper puis rallumer le Bluetooth du PC | « Écoute BLE arrêtée » dans le journal, puis la bascule remarche | |
| 13 | cocher « Démarrer avec Windows », fermer la session, la rouvrir | ACAW démarre tout seul, sans console. Le journal montre « Lancement de … (Windows démarré il y a …) » **puis « ACAW démarré »** : la première ligne seule ne suffit pas | |
| 13b | le désactiver dans Paramètres → Applications → Démarrage, puis ouvrir le menu de l'icône | la case « Démarrer avec Windows » est décochée | |
| 14 | lancer `acaw-tray.exe` une seconde fois | rien (une seule icône) | |

### Recette de la configuration

Mêmes conditions (ACAW lancé depuis un terminal ordinaire). Garder le journal ouvert.

| # | Geste | Attendu | ✓ |
|---|---|---|---|
| C1 | quitter ACAW, renommer `config.json` en `config.old.json`, relancer ACAW (une seule paire d'AirPods appairée) | bulle « ACAW pilote … », `config.json` recréé, la bascule marche | |
| C2 | menu → AirPods | tes AirPods cochés, « Paramètres Bluetooth… » en bas | |
| C3 | dans `config.json`, passer `PauseDelay` à `"00:00:10"` et enregistrer | journal : « Configuration appliquée » ; une pause rend les AirPods 10 s plus tard (remettre 5 s ensuite) | |
| C4 | dans `config.json`, passer `AutoSwitch` à `false` | la case « Bascule automatique » se décoche, l'icône devient gris clair, sans redémarrage du moteur | |
| C5 | supprimer une virgule dans `config.json` et enregistrer | une seule bulle « Configuration illisible… précédente gardée », la bascule continue ; corriger → plus de bulle | |
| C6 | pendant une vidéo (AirPods sur le PC), modifier un délai | les AirPods restent sur le PC, le son ne reste jamais coupé | |
| C7 | couper le Bluetooth du PC, quitter puis relancer ACAW, rallumer le Bluetooth | « Recherche des AirPods… » ou erreur, puis ACAW démarre tout seul en 30 s au plus | |
| C8 | (si tu as d'autres AirPods ou des Beats) les appairer, puis les choisir dans le menu | ils sont pilotés tout de suite ; vérifier que `acaw status` les voit « éveillés » (hypothèse « modèle BLE = pid » à confirmer pour ce modèle) | |

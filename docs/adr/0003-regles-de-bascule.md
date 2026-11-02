# ADR-0003 : Règles de bascule entre le PC et l'iPhone

**Statut :** Accepté pour la v1. Les délais seront ajustés à l'usage.
**Date :** 2026-11-01
**Décideur :** Romsy

## Contexte

La phase 0 a établi deux faits ([ADR-0001](0001-mecanisme-bascule.md)) :

- **Test B ❌** : tant que le PC tient les AirPods, l'iPhone ne les reprend pas tout seul. La bascule
  automatique d'Apple ne concerne que les appareils Apple du même compte iCloud.
- **Test B2 ✅** : dès que le PC relâche les AirPods, l'iPhone les reprend en ~2–3 s.

Le retour vers l'iPhone dépend donc entièrement d'une **décision du PC**. Cas difficile : une vidéo joue
sur le PC et l'utilisateur lance une musique sur son téléphone. Le PC ne voit rien de ce qui se passe
sur l'iPhone, et comme il joue encore, une simple règle « relâcher après silence » ne se déclenche jamais.

Le PC doit donc deviner l'intention de l'utilisateur à partir de signaux qu'il peut observer, sans
provoquer de ping-pong et sans faire sortir le son par les haut-parleurs par surprise.

## Décision

### Les six règles de la v1

| # | Règle | Déclencheur | Action |
|---|---|---|---|
| **R1** | Prendre au démarrage d'une lecture | passage de « rien ne joue » à « ça joue » côté PC (SMTC `Playing` ou nouvelle session WASAPI active), AirPods pas sur le PC, AirPods éveillés (trame BLE du bon modèle reçue depuis moins de `T_eveil`, validé au test C), hors cooldown | `Reconnect` |
| **R2** | Relâcher après une pause explicite | SMTC passe à `Paused` / `Stopped` (clic, touche média, ou appui sur l'AirPod) et le reste pendant `T_pause` | `Disconnect` |
| **R3** | Relâcher après un silence prolongé | plus aucun son pendant `T_silence`, sans pause explicite (jeu, Discord, appli sans contrôle média) | `Disconnect` |
| **R4** | Relâcher à la demande | bouton « Rendre à l'iPhone » (barre des tâches) ou raccourci clavier | pause des médias du PC, puis `Disconnect` immédiat |
| **R5** | Ne jamais reprendre ce qu'on vient de perdre | les AirPods quittent le PC **sans** que PodHopWin l'ait demandé (iPhone via Centre de contrôle, hors de portée…) | passer en `Lost` : pas de reprise tant qu'une **nouvelle** lecture n'a pas démarré après la perte, ni avant `T_cooldown` |
| **R6** | Mettre le PC en pause quand il perd les AirPods | R5 se produit alors qu'un média joue | `TryPauseAsync` sur les sessions SMTC en lecture, pour que le son ne bascule pas sur les haut-parleurs |

**Le scénario du contexte avec ces règles :** l'utilisateur met YouTube en pause (sur le PC ou d'un appui
sur l'AirPod) et lance sa musique sur l'iPhone. Après `T_pause` + ~2–3 s, les AirPods sont sur l'iPhone.
S'il ne met pas en pause, il reste le Centre de contrôle de l'iPhone : R5 empêche alors le PC de reprendre
les AirPods, et R6 met YouTube en pause.

### Paramètres (configurables)

| Paramètre | Défaut | Raison |
|---|---|---|
| `T_pause` | 5 s | assez long pour ignorer une pause rapide (relire une phrase), assez court pour suivre l'intention |
| `T_silence` | 30 s | les silences sans pause sont souvent involontaires (chargement, blanc dans une vidéo) |
| `T_cooldown` | 5 s | empêche qu'une lecture qui redémarre seule (lecture automatique) reprenne aussitôt les AirPods |
| `T_eveil` | 30 s | 5 fois le pire écart entre deux annonces BLE au repos (5,9 s, test C3) ; voir les [notes BLE](../protocole-ble-airpods.md) |

Une pause suivie d'une reprise avant `T_pause` **annule** la relâche.

### Machine à états

Elle précise celle, simplifiée, de l'[ADR-0002](0002-modele-processus.md) :

```
                    R1 (nouvelle lecture)
   ┌──────────┐ ─────────────────────────► ┌───────────┐  endpoint ACTIVE  ┌────────────────────────┐
   │   Away   │                            │ Switching │ ─────────────────►│       OnWindows        │
   │ (ailleurs│ ◄───────────────────────── └───────────┘                   │ Playing / Paused /     │
   │ ou libres)│   échec (backoff)                                         │ Silent (minuteurs)     │
   └──────────┘                                                            └────────────────────────┘
        ▲  ▲                                                                  │ R2/R3/R4      │ perte non
        │  │            endpoint ≠ ACTIVE        ┌───────────┐                │               │ demandée (R5)
        │  └──────────────────────────────────── │ Releasing │ ◄──────────────┘               ▼
        │                                        └───────────┘                         ┌────────────┐
        └────────────── nouvelle lecture après la perte + T_cooldown ───────────────── │    Lost    │
                                                                                       │ (+ R6)     │
                                                                                       └────────────┘
```

Une connexion **non demandée** (Windows qui reconnecte de lui-même) fait passer directement à `OnWindows`.

## Options étudiées

### Option A : signaux observables sur le PC (pause, silence, bouton) ✅ v1

| Dimension | Évaluation |
|---|---|
| Complexité | Faible : SMTC, WASAPI et minuteurs |
| Coût | Nul, sans réseau ni droits admin |
| Fiabilité | Bonne : la pause est un signal volontaire |
| Expérience | Il faut une action côté PC (pause) ou côté iPhone (Centre de contrôle) |

### Option B : raccourci iOS → requête sur le réseau local 🟡 v2, en option

L'app Raccourcis de l'iPhone, en automatisation « quand j'ouvre Apple Music / Spotify », envoie une
requête HTTP au PC (`http://<nom-du-pc>.local:port/release` + jeton secret). Le PC applique alors R4.

**Pour :** le seul moyen d'obtenir « le dernier qui lance gagne », comme entre appareils Apple.
**Contre :**
- un petit serveur à faire tourner sur le PC ;
- une règle de pare-feu (réseau privé) et un secret à configurer ;
- ne marche qu'en Wi-Fi local ;
- assouplit l'exigence « aucun accès réseau » de l'ADR-0002, d'où une option **désactivée par défaut**.

### Option C : détecter l'activité de l'iPhone par BLE 🔬 recherche

Les iPhone diffusent en BLE un indicateur d'activité (écran allumé, son en cours).
**Contre :** leurs adresses BLE changent régulièrement. Distinguer *ton* iPhone de ceux des voisins est
un problème non résolu, avec un risque de relâcher les AirPods à cause d'un autre téléphone.

### Option D : silence seul (règle R3 uniquement) ❌

Ne couvre pas le scénario du contexte : tant que le PC joue, rien ne se passe.

## Arbitrage

A couvre le scénario en une action naturelle (mettre en pause), sans réseau et sans risque.
B apporte le confort « zéro action », mais au prix d'une configuration et d'une surface réseau : elle n'a
de sens qu'une fois la v1 éprouvée. C reste trop incertaine.

## Conséquences

- ✅ Comportement prévisible : le PC ne prend les AirPods qu'au démarrage d'une lecture, et ne les rend
  qu'après une pause, un silence ou une demande.
- ✅ Pas de ping-pong (R5) ni de son surprise sur les haut-parleurs (R6).
- ❌ Si la vidéo continue sur le PC, passer sur l'iPhone demande un geste : pause, bouton, ou Centre de contrôle.
- ❌ R6 ne peut mettre en pause que les applis qui exposent des contrôles média (SMTC). Un jeu ou Discord
  continueront sur les haut-parleurs.
- 🔁 Les valeurs `T_pause`, `T_silence` et `T_cooldown` sont à revoir après quelques jours d'usage.

## Actions

1. [ ] Coder les règles dans `PodHop.Core` (machine à états pure, horloge injectée).
2. [ ] Tests unitaires d'au moins ces scénarios :
   - vidéo PC → pause → musique iPhone → relâche après `T_pause` ;
   - pause de 3 s puis reprise → pas de relâche ;
   - perte via le Centre de contrôle pendant la lecture → pause des médias, pas de reprise ;
   - nouvelle lecture sur le PC après la perte → reprise ;
   - lecture automatique dans les `T_cooldown` qui suivent la perte → pas de reprise ;
   - silence de 30 s sans pause → relâche.
3. [ ] Vérifier que l'appui sur l'AirPod (commande de pause Bluetooth) apparaît bien comme `Paused` dans SMTC.
4. [ ] v2 : spike du raccourci iOS (option B).

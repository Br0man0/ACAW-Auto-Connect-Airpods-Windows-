# ADR-0002 : Modèle de processus et architecture interne

**Statut :** Proposé
**Date :** 2026-11-01
**Décideur :** Romsy

## Contexte

L'application doit réagir à des événements venant de sources très différentes (lecture média,
annonces BLE, état des endpoints audio, veille/réveil), arrivant sur des threads arbitraires, et
piloter une seule ressource : la connexion des AirPods. Elle doit rester testable sans matériel.

## Décisions

### 1. Application de session utilisateur, pas service Windows

Un service tourne en session 0 : il ne voit ni les sessions audio de l'utilisateur, ni les contrôles
média (SMTC), ni de façon fiable son endpoint par défaut. ACAW est donc une **application de barre
des tâches** lancée à l'ouverture de session (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`),
sans droits admin.

### 2. « Cœur fonctionnel, coque impérative »

```
 Adaptateurs Windows (threads COM/WinRT variés)
 ┌────────────┐ ┌──────────────┐ ┌──────────────┐ ┌───────────────┐
 │ SMTC/WASAPI│ │ BLE watcher  │ │ Endpoint     │ │ Minuterie /   │
 │ lecture    │ │ boîtier/oreille│ │ notifications│ │ veille/réveil │
 └─────┬──────┘ └──────┬───────┘ └──────┬───────┘ └──────┬────────┘
       └───────── événements immuables ─┴────────────────┘
                          ▼
              Channel<Event>  (file unique)
                          ▼
         SwitchStateMachine   ← pure, mono-thread, IClock injectée
                          ▼
                   Commandes (Reconnect, Disconnect, Log…)
                          ▼
              Exécuteur → BtAudioController (IKsControl)
```

- **Pas de verrous ni de courses** : un seul thread fait évoluer l'état.
- **Pas de deadlock MMDevice** : la doc Microsoft interdit d'appeler l'API MMDevice depuis un rappel
  `IMMNotificationClient` ; les rappels ne font que poster un événement.
- **Testable** : on rejoue une séquence d'événements avec une fausse horloge et on vérifie les commandes.

États prévus, en version simplifiée (les règles détaillées et la machine à états complète sont dans
l'[ADR-0003](0003-regles-de-bascule.md)) :

```
Idle ──(lecture démarre + AirPods éveillés)──► Switching ──(endpoint ACTIVE)──► OnWindows
  ▲                                               │ échec / délai                       │
  │                                               ▼                                     │ silence N s
  └──────────── Cooldown ◄──(l'iPhone les reprend)───────────── Releasing ◄─────────────┘
```

### 3. Interop COM générée par CsWin32

Le paquet `Microsoft.Windows.CsWin32` génère les définitions COM (`IMMDevice`, `IKsControl`…) à partir
des métadonnées officielles de Windows, ce qui évite les erreurs de GUID ou d'ordre de méthodes.

### 4. Identifier les AirPods par MAC

Le nom de l'endpoint est traduit et modifiable. On stocke la **MAC** et on la retrouve dans le chemin
du filtre KS. On cible l'endpoint **A2DP**, jamais le mains libres (HFP).

## Risques

| # | Risque | Impact | Mesure |
|---|---|---|---|
| R1 | La reconnexion KS ne prend pas les AirPods à l'iPhone | Bloquant | Test A phase 0 → repli B, puis décision C |
| R2 | L'iPhone ne reprend pas seul les AirPods à un appareil non-Apple | Fort — **confirmé** au test B | **Validé au test B2** : relâcher après N s de silence, l'iPhone les reprend en ~2–3 s. Plus une action « Rendre à l'iPhone » immédiate dans la barre des tâches |
| R3 | Windows reconnecte de lui-même, ou ping-pong avec l'iPhone | Ping-pong | Traiter toute connexion non demandée ; état `Lost` + cooldown (ADR-0003, règle R5) |
| R4 | Le scan BLE ralentit la connexion radio | ~~Vitesse~~ **Écarté** | **Tests C2 et contrôle (2026-11-02, mêmes conditions)** : médiane ~1,7 s avec scan, ~2,1 s sans scan, mêmes plages (≈ 1–4 s). Le scan passif ne ralentit pas la connexion : on le laisse actif en permanence, sans pause pendant `Switching` |
| R5 | Autres appareils Apple ou autres AirPods à proximité | Faux « éveillés » | **Test C1** : filtrer par modèle BLE = « pid » du driver, ce qui écarte les autres appareils Apple ([notes BLE](../protocole-ble-airpods.md)). Il reste le cas de deux paires du même modèle. Le BLE autorise une bascule, il ne la déclenche pas |
| R6 | Veille, radio coupée : watcher arrêté, objets COM périmés | Bascule morte | Réinitialiser sur les événements d'alimentation et de radio. **Observé le 2026-11-02** : après une sortie de veille moderne, `phase0 connect` a échoué alors que chaque lancement repart de zéro (pas d'objet COM périmé) : le problème viendrait donc de la pile Bluetooth au réveil. Cause à établir |
| R7 | Couper les haut-parleurs pendant le switch puis crash | PC muet | **Fait en phase 2** (ADR-0003, R7) : rétablissement garanti par la machine (20 s max depuis 2026-11-09, 6 s avant), et au démarrage suivant grâce à un fichier témoin |
| R8 | `IPolicyConfig` (non documentée) pour changer la sortie par défaut | Casse à une MAJ | Exclu |

## Exigences non fonctionnelles

- Latence déclencheur → son : médiane < 3 s, p95 < 5 s (AirPods éveillés), **mesurée**.
- Au repos : CPU < 1 %, RAM < 60 Mo.
- Aucun droit admin, aucun accès réseau. Seule exception envisagée : l'option v2 « raccourci iOS » de
  l'ADR-0003, désactivée par défaut.
- Au plus une tentative de reconnexion par déclencheur, avec backoff.

## Conséquences

- ✅ Logique testable unitairement, threads maîtrisés.
- ❌ Un peu plus de code « plomberie » (événements, commandes) qu'un code direct.
- ✅ La structure `src/Acaw.Core` + `src/Acaw.App` + `tests/` a été créée en phase 1 (2026-11-02).

## Mise à jour : phase 2 (2026-11-02)

- **Deux exécutables.** `acaw-tray.exe` est un `WinExe` (aucune console ne s'ouvre au démarrage de
  Windows), `acaw.exe` reste un programme console. Le code Windows commun est dans la bibliothèque
  `Acaw.Windows` (`Acaw.Cli`, `Acaw.Tray`, `Acaw.Windows`, `Acaw.Core`, plus 2 projets de tests).
- **L'hôte est dans le Core.** `AcawHost` (file `Channel`, un seul lecteur, battement de 250 ms) ne
  dépend que d'interfaces (`Hosting/Ports.cs`) qu'`Acaw.Windows` implémente. Il est ainsi testé de bout
  en bout avec de faux ports et une fausse horloge ([guide 8](../guide/08-strategie-de-test.md)).
- **Threads COM.** Les adaptateurs Windows sont créés hors du thread de l'interface (STA) et utilisés
  depuis les threads de l'hôte (MTA). L'interface ne fait que déposer des demandes dans la file.
- **Démarrage avec Windows** par la clé `HKCU\…\Run`, désactivé par défaut, activable depuis le menu.

## Mise à jour : configuration à chaud (2026-11-09)

- **Une nouvelle configuration recrée l'hôte** au lieu de modifier ses pièces en marche : un seul chemin
  de démarrage, déjà éprouvé. Seul `AutoSwitch` s'applique directement. La décision est prise par
  `ConfigCoordinator` (Core, testé avec de faux ports `IConfigSource`, `IHeadsetCatalog`,
  `IAcawServiceFactory`) ; l'icône ne fait qu'afficher ([guide 7](../guide/07-application-residente.md#la-configuration-à-chaud)).
- **Ordre du remplacement : créer le nouveau → arrêter l'ancien → démarrer le nouveau.** Si la création
  échoue, l'ancien continue. `AcawHost.Dispose` attend la fin du traitement en cours (1 s au plus), et
  l'arrêt des branchements rétablit le son coupé par la règle R7.
- **Un seul thread** pour le coordinateur (celui de l'interface). Les notifications de `config.json`
  (`ConfigWatcher`, regroupées sur 0,5 s) et les minuteurs y sont ramenés.
- **Sélection automatique** seulement sans ambiguïté (une seule paire Apple appairée) ; jamais au hasard.
- Alternative écartée : modifier en marche l'hôte et chaque adaptateur (adresse du casque, modèle BLE,
  délais de la machine). Plus fin (pas d'interruption d'une bascule en cours), mais chaque pièce aurait
  eu son propre chemin de mise à jour à tester.

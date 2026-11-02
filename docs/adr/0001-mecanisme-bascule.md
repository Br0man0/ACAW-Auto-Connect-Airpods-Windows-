# ADR-0001 : Mécanisme de bascule des AirPods vers Windows

**Statut :** Accepté (2026-11-01, après les tests A, B et B2 de la phase 0)
**Date :** 2026-11-01
**Décideur :** Romsy

## Contexte

PodHop, projet Linux dont ACAW reprend l'idée, prend les AirPods en ouvrant le canal de contrôle Apple **AAP**
sur Bluetooth L2CAP (PSM `0x1001`). Sous Windows, une application ordinaire ne peut pas ouvrir de canal
L2CAP : Winsock ne propose que RFCOMM et WinRT n'expose pas L2CAP classique. Il faut un autre moyen
de faire « appeler » les AirPods par le PC.

## Décision

Utiliser **`KSPROPERTY_ONESHOT_RECONNECT` / `KSPROPERTY_ONESHOT_DISCONNECT`** (jeu de propriétés
`KSPROPSETID_BtAudio`, GUID `7FA06C40-B8F6-4C7E-8556-E8C33A12E54D`) sur l'endpoint audio Bluetooth
des AirPods. `IKsControl` s'obtient sur le **périphérique du filtre KS** du driver Bluetooth :
`IMMDevice (endpoint) → IDeviceTopology → IConnector → GetConnectedTo → IPart → GetTopologyObject →
GetDeviceId → IMMDeviceEnumerator.GetDevice → IMMDevice.Activate(IKsControl)`.

## Options étudiées

### Option A — Reconnexion KS (`ONESHOT_RECONNECT`) ✅ retenue

| Dimension | Évaluation |
|---|---|
| Complexité | Faible à moyenne (interop COM) |
| Coût | Nul : pas de droits admin, pas de signature de code |
| Robustesse | Propriété documentée par Microsoft, utilisée par l'outil ToothTray |
| Couverture | Connexion / déconnexion seulement, pas de commandes AAP |

**Pour :** mode utilisateur, rapide à prototyper, passe par la pile audio officielle.
**Contre :** on ne contrôle pas ce que fait la pile Windows. Rien ne garantit encore que les AirPods
quittent l'iPhone → **risque R1**, objet du test A de la phase 0.

### Option B — `BluetoothSetServiceState` (désactiver/réactiver le service A2DP)

**Pour :** API simple. **Contre :** lent (ré-énumération du périphérique), endpoints qui disparaissent
et réapparaissent, droits admin parfois nécessaires. Solution de repli seulement.

### Option C — Driver noyau KMDF avec L2CAP + AAP

**Pour :** parité complète avec PodHop (+ ANC, détection d'oreille exacte).
**Contre :** WDK, mode test-signing (Secure Boot désactivé) ou certificat EV + signature Microsoft,
risque d'écran bleu, plusieurs semaines de travail. Hors de proportion pour la v1.

### Option D — Linux annexe (Raspberry Pi, WSL + clé USB)

**Rédhibitoire :** le son arriverait dans Linux, pas dans Windows.

## Arbitrage

A est la seule option à la fois gratuite, sans admin et rapide à valider. C n'est justifiée que si A
échoue sur le vol depuis l'iPhone **et** que ce vol est indispensable. B sert de repli si `IKsControl`
est inaccessible sur un adaptateur donné.

## Conséquences

- ✅ Projet 100 % en mode utilisateur, installation sans droits admin.
- ❌ Pas d'ANC ni de commandes AAP ; détection d'oreille et batterie par BLE uniquement.
- 🔁 À revoir si la phase 0 montre que le vol depuis l'iPhone ne marche pas.

## Constats de la phase 0

| Date | Constat |
|---|---|
| 2026-11-01 | Le chemin `IMMDevice → … → IKsControl` s'ouvre sur l'adaptateur Intel du PC de dev. Les endpoints A2DP exposent la MAC dans le chemin du filtre KS (`…&<mac>_c00000000`) ; les endpoints mains libres (`bthhfenum`) **non**. |
| 2026-11-01 | Bluetooth allumé, un casque déconnecté est en `DEVICE_STATE_UNPLUGGED`. `NOTPRESENT` signifie que le driver est absent, le plus souvent parce que le Bluetooth du PC est coupé. La machine à états doit traiter « tout sauf ACTIVE » comme déconnecté, et `NOTPRESENT` comme « inutile d'essayer ». |
| 2026-11-01 | `IPart.Activate(IKsControl)` renvoie `E_NOINTERFACE` (vu en .NET comme `InvalidCastException`). Il faut activer `IKsControl` sur le périphérique du filtre KS (`IMMDevice.Activate`), comme ToothTray. |
| 2026-11-01 | **Premier succès** : `ONESHOT_RECONNECT` connecte les AirPods Pro en 13,1 s (l'appel lui-même rend la main en 2 ms) ; `ONESHOT_DISCONNECT` les libère en 93 ms. État des AirPods avant l'essai non contrôlé : à refaire avec le protocole. |
| 2026-11-01 | **Test A1 (libre, 10 essais)** : 10/10 réussis. 1er essai 1 959 ms, puis 285–414 ms (médiane ≈ 300 ms). Biais : les AirPods venaient d'être déconnectés 5 s avant par le spike, c'est un cas « à chaud », pas des AirPods au repos. |
| 2026-11-01 | **Test A2 (sur l'iPhone inactif, 5 essais)** : 5/5 réussis. Min 11,1 s · médiane 12,9 s · max 18,0 s. |
| 2026-11-01 | **Test A3 (sur l'iPhone en lecture, 5 essais)** : 5/5 réussis. Min 8,3 s · médiane 10,1 s · max 16,4 s. |
| 2026-11-01 | **Bilan A** : le vol depuis l'iPhone **fonctionne** (R1 levé, sous réserve que le son se soit bien coupé sur l'iPhone). En revanche, la latence (≈ 10–13 s) est **très loin** de l'objectif (médiane < 3 s). Prochaine étape : décomposer ces secondes (liaison radio ou négociation des profils ?). |
| 2026-11-01 | **Profil mains libres (HFP) retiré** (Propriétés des AirPods → Services → « Mains libres téléphonie » décoché). A2 : 7 essais valides, min 1,38 s · **médiane 2,0 s** · max 4,6 s. A3 : 5 essais, min 1,15 s · **médiane 2,3 s** · max 4,7 s. Soit **environ 5 à 6 fois plus rapide** : l'essentiel des 10–13 s venait de la négociation du profil mains libres. L'objectif « médiane < 3 s » est atteint ; le p95 reste à confirmer sur plus d'essais (distribution en deux paquets : ~1,2–2,4 s et ~4,2–4,7 s). |
| 2026-11-01 | 3 essais A2 marqués `invalide` dans le CSV : les AirPods étaient encore sur le PC, le spike les comptait à tort comme des échecs. Corrigé : `bench` redemande, ou ignore l'essai. |

### Conséquence de ces constats

Le retrait du profil mains libres devient une **étape d'installation recommandée** de ACAW.
Contrepartie : le micro des AirPods n'est plus utilisable sur le PC. À étudier plus tard : un mode
« appel » qui réactive le profil à la demande (`BluetoothSetServiceState`, probablement avec droits admin).
| 2026-11-01 | **Test B : ❌.** AirPods sur le PC (YouTube en lecture, puis arrêté), musique lancée sur l'iPhone : l'iPhone ne reprend **pas** les AirPods, même après 15 s. Le passage manuel par le Centre de contrôle de l'iPhone fonctionne. Explication : la bascule automatique d'Apple n'agit qu'entre appareils Apple du même compte iCloud ; le PC n'en fait pas partie, donc l'iPhone ne « réclame » pas les AirPods. |
| 2026-11-02 | **A1 refait sans mains libres** (`libre-controle`, 10 essais) : 10/10, min 0,97 s · médiane ~2,1 s · max 4,2 s. Les ~0,3 s du premier A1 étaient sans doute un artefact. Hypothèse : avec le mains libres actif, `ONESHOT_DISCONNECT` ne coupait que l'A2DP, et la liaison radio restait ouverte par le profil mains libres ; la reconnexion n'avait alors pas besoin d'appeler les AirPods. Sans ce profil, chaque essai refait l'appel radio : ~1–4 s, comme pour le vol depuis l'iPhone. |
| 2026-11-01 | **Test B2 : ✅.** Quand le PC relâche les AirPods (`ONESHOT_DISCONNECT`), l'iPhone les reprend seul en **~2–3 s**, que sa musique soit arrêtée, en pause ou en lecture. |

### Décision après la phase 0 (2026-11-01)

L'option A est **acceptée**. Le cycle complet fonctionne sans Apple ni driver :

- **vers le PC** : `ONESHOT_RECONNECT` → ~2 s de médiane (profil mains libres retiré) ;
- **vers l'iPhone** : le PC relâche les AirPods (`ONESHOT_DISCONNECT`) → l'iPhone les reprend en ~2–3 s.

Le retour vers l'iPhone n'est pas déclenché par l'iPhone : c'est **le PC qui doit relâcher**. Le délai
perçu au retour vaut donc « délai de silence avant relâche + ~2–3 s ».

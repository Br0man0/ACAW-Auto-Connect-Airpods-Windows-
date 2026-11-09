# Annonces BLE des AirPods : ce qu'on sait

Notes établies par l'observation (test C1 de la phase 0, 2026-11-01, AirPods Pro modèle `2027`).
Chaque affirmation porte un niveau de confiance : **✅ établi**, **🟡 probable**, **❓ inconnu**.

## Où trouver la trame

Annonce BLE → données fabricant d'Apple (identifiant `0x004C`) → bloc de type `0x07` (« proximity
pairing »), de 25 octets (`0x19`) :

| Octet | Contenu | Confiance |
|---|---|---|
| 0 | toujours `01` | ✅ |
| 1–2 | **modèle**, petit-boutiste : octets `27 20` → `0x2027` | ✅ |
| 3 | **statut** (voir plus bas) | 🟡 |
| 4 | batteries des écouteurs, un quartet chacun : 0–10 = ×10 %, 15 = inconnu | ✅ |
| 5 | quartet haut : indicateurs de charge ; quartet bas : batterie du boîtier | 🟡 |
| 6 | « couvercle » : varie, signification non établie | ❓ |
| 7 | couleur (`00` pour ces AirPods) | 🟡 |
| 8 | suffixe | ❓ |
| 9–24 | 16 octets chiffrés, qui changent toutes les quelques secondes | ✅ |

## Reconnaître *ses* AirPods

- ✅ **Le modèle BLE est égal au « pid » du driver Bluetooth classique** (`…_pid&2027#…`, affiché par
  `phase0 list`). C'est le lien entre « les AirPods appairés au PC » et « les annonces reçues ».
- ✅ **D'autres appareils Apple émettent aussi des blocs `0x07`**, avec des valeurs qui changent à chaque
  trame (modèles `A9D5`, `F24E`, `678C`…). Il faut donc **filtrer par modèle**.
- ✅ **Deux adresses BLE pour une seule paire** : chaque écouteur émet ses propres annonces. Leurs
  adresses sont aléatoires et changent régulièrement : ne jamais les mémoriser.
- ❓ Deux paires du **même modèle** à proximité (colocataire) restent indiscernables. La couleur et les
  batteries peuvent aider, sans certitude.

## L'octet de statut

Observations, une ligne par geste (les deux écouteurs émettent des statuts proches, mais pas identiques) :

| Situation | Écouteur A | Écouteur B |
|---|---|---|
| Les deux dans le boîtier ouvert | `0111 0101` | `0001 0101` |
| Un sorti, tenu en main | `0011 0001` | `0101 0001` |
| Un dans l'oreille, l'autre dans le boîtier | `0011 0011` | `0101 0011` |
| Un dans l'oreille, l'autre en main | `0010 0011` | — |
| Les deux dans les oreilles | `0010 1011` | — |
| Un dans l'oreille, l'autre retiré | — | `0000 0011` |

Ce qu'on en tire :

| Bit | Masque | Sens | Confiance |
|---|---|---|---|
| 1 et 3 | `0x02`, `0x08` | un écouteur dans l'oreille chacun. **Le nombre de bits à 1 = nombre d'écouteurs portés** | ✅ pour « au moins un » et pour le nombre (cohérent avec tous les gestes de C1 et C3, et `oreille=2` constant sur 2 × 60 s avec les deux portés) ; 🟡 pour le côté, hors boîtier (voir « Gauche et droite ») |
| 2 | `0x04` | les deux écouteurs dans le boîtier | ✅ |
| 4 | `0x10` | au moins un écouteur dans le boîtier | 🟡 |
| 5 | `0x20` | dit quel écouteur émet : constant pour une adresse donnée ; 1 = gauche, 0 = droit | ✅ pour « quel écouteur émet » ; 🟡 pour le côté |
| 6 | `0x40` | change d'écouteur pendant les manipulations : rôle « principal » ? | ❓ |
| 0 | `0x01` | à 1 presque tout le temps | ❓ |

Indicateurs de charge (quartet haut de l'octet 5) : les bits 0 et 1 sont à 1 pour chaque écouteur
posé dans le boîtier (`1011` les deux, `1010`/`1001` un seul, `1000` aucun) ✅. Le bit 3 est toujours
à 1 ❓. La batterie du boîtier n'est donnée que si un écouteur y est posé ✅.

## Gauche et droite

Capture Wireshark du 2026-11-09 (USBPcap, `acaw-tray` lancé, 32 s, 20 annonces, 2 adresses). Romsy a
retiré le **droit** en premier, puis l'a remis et a retiré le gauche.

| Temps | Émetteur (bit `0x20`) | Statut | `0x02` | `0x08` |
|---|---|---|---|---|
| 0 → 4,2 s | A (0) | `0x0B` | 1 | 1 |
| 4,9 → 8,0 s | A (0) | `0x09` | **0** | 1 |
| 10,5 → 20,9 s | B (1) | `0x23` | 1 | **0** |
| 25,0 → 31,9 s | A (0) | `0x03` | 1 | **0** |

Toute la capture s'explique si les bits sont **relatifs à l'écouteur qui émet** : `0x02` = « moi dans
l'oreille », `0x08` = « l'autre dans l'oreille ». A est alors l'écouteur retiré en premier, donc le
**droit**, et B le gauche. On voit aussi que c'est l'écouteur porté qui prend le relais pour émettre.
D'où la règle, **🟡 probable** :

```
émetteur = gauche si 0x20, sinon droit
gauche dans l'oreille = 0x20 ? bit 0x02 : bit 0x08
droit  dans l'oreille = 0x20 ? bit 0x08 : bit 0x02
```

Limites :
- une seule capture, avec des trous jusqu'à 7 s : les étapes « les deux retirés » et « les deux remis »
  n'y apparaissent pas ;
- ❓ **un écouteur posé dans le boîtier semble suivre une autre logique**. En C1, avec un écouteur dans
  l'oreille et l'autre dans le boîtier, les **deux** émetteurs annonçaient `0x02` (« moi dans l'oreille »),
  ce qui contredit la lecture relative. Le bit `0x40`, qui apparaît surtout boîtier ouvert, pourrait jouer
  là-dedans ;
- les batteries étaient égales (60 %/60 %) : on ne sait pas encore si leur ordre s'inverse aussi selon
  l'émetteur.

À confirmer par un test minuté : un geste toutes les 20 s, gauche seul puis droit seul.

## Éveil et rythme

- ✅ **Boîtier fermé = aucune annonce.** Rien n'est reçu avant l'ouverture, ni après la fermeture.
- ✅ **Réaction rapide** : la première annonce arrive moins d'une seconde après l'ouverture du boîtier.
- ✅ **Les AirPods sortis du boîtier émettent en continu**, qu'ils soient connectés au PC ou à l'iPhone
  (test C3, 2026-11-02, 60 s chacun, en retirant puis remettant un écouteur puis l'autre ; ces
  manipulations ont pu allonger les écarts) :

  | Connectés à | Écouteur le plus bavard | L'autre |
  |---|---|---|
  | PC | 20 trames, écart max 11,3 s | 12 trames, écart max 2,2 s |
  | iPhone | 32 trames, écart max 14,0 s | 7 trames regroupées, puis silence |

  Un écouteur peut se taire longtemps : il faut raisonner sur **la paire** (toutes adresses du bon
  modèle confondues). La commande `ble --modele` affiche maintenant cet écart pour la paire.
- ✅ **Au repos** (les deux portés, sans y toucher, 60 s, 2026-11-02), **un seul écouteur émet** :

  | Connectés à | Trames | Écart max (paire) |
  |---|---|---|
  | iPhone | 50 (~0,8 par seconde) | 5,9 s |
  | PC | 52 | 2,6 s |

  Le second écouteur ne se manifeste que lorsqu'on manipule la paire.
- ✅ **Délai « endormis » retenu : 30 s sans trame de la paire**, soit 5 fois le pire écart au repos.
  La marge est volontairement large : croire à tort les AirPods endormis ferait rater une bascule, alors
  que les croire à tort éveillés ne coûte qu'une tentative de connexion inutile.
- ✅ Les adresses BLE ont changé d'un jour à l'autre (`497B…`/`7BA9…` → `588A…`/`6249…`).
- 🟡 Puissance reçue à un bureau de distance : −32 à −63 dBm.

## Conséquences pour ACAW

- **Règle R1 (ADR-0003)** : « AirPods éveillés » = une trame du bon modèle reçue dans les 30 dernières
  secondes. C'est fiable : boîtier fermé, rien n'est émis ; portés, ils émettent en continu.
- **Détection d'oreille** : « au moins un écouteur porté » et le nombre d'écouteurs portés se lisent
  dans les bits 1 et 3, et le côté suit probablement la règle ci-dessus, hors boîtier. **Mais** l'émetteur
  ne voit vite que son propre retrait : celui de l'autre écouteur arrive tard, ou pas du tout (test D du
  2026-11-09 : 2→1 jamais vu en 10 s, autres gestes en 3 à 6,5 s). Ces annonces ne suffisent donc pas pour
  mettre en pause au retrait : la fonction est reportée ([ADR-0004](adr/0004-pause-au-retrait.md)).
  Elles restent utilisables pour afficher l'état.
- **Batterie** : utilisable dès maintenant, sans savoir quel chiffre correspond à quel côté.

# 6. Utiliser `acaw`

En phase 1, ACAW est un outil en ligne de commande. La bascule automatique arrivera en phase 2.

## Une seule fois : préparer les AirPods

1. **Appairer** les AirPods au PC : Paramètres → Bluetooth et appareils → Ajouter un appareil, boîtier
   ouvert, bouton du boîtier maintenu jusqu'au voyant blanc.
2. **Retirer le profil mains libres**, sinon chaque bascule prend 10 à 13 s au lieu de ~2 s
   ([ADR-0001](../adr/0001-mecanisme-bascule.md)) :
   ```bash
   control printers
   ```
   Clic droit sur les AirPods → Propriétés → onglet Services → décocher « Mains libres téléphonie ».
   Contrepartie : pas de micro AirPods sur le PC.
3. **Choisir les AirPods** dans ACAW, depuis le dossier du dépôt :
   ```bash
   dotnet run --project src/Acaw.App -- setup
   ```
   Tape le numéro des AirPods dans la liste. ACAW enregistre leur adresse et leur modèle dans
   `%APPDATA%\ACAW\config.json`.

## Les commandes

| Commande | Effet |
|---|---|
| `setup [numéro]` | choisir les AirPods (sans numéro, affiche la liste et demande) |
| `status` | connectés à ce PC ? éveillés ? combien d'écouteurs portés ? batteries |
| `switch` | amener les AirPods sur ce PC, avec le temps mis |
| `release` | rendre les AirPods : l'iPhone les reprend en 2 à 3 s |

Exemple :

```bash
dotnet run --project src/Acaw.App -- status
```

```
AirPods : Casque (AirPods Pro) (modèle 2027)
Sur ce PC : non (ailleurs ou libres)
Écoute BLE pendant 5 s…
Éveillés : oui · écouteurs portés : 2 · dans le boîtier : 0
Batteries : écouteurs 80 % / 80 % · boîtier —
```

Les batteries ne disent pas quel écouteur est le gauche : les annonces BLE ne permettent pas encore de
le savoir. La batterie du boîtier n'est connue que si un écouteur y est posé.

## Lancer `acaw` directement

`dotnet run` recompile si besoin, ce qui est pratique en développement mais lent. Pour obtenir un
exécutable :

```bash
dotnet publish src/Acaw.App -c Release -o publish
```

Puis :

```bash
publish\acaw.exe status
```

## Les délais

Le fichier `%APPDATA%\ACAW\config.json` contient aussi les délais de l'ADR-0003. En phase 1, seul
`SwitchTimeout` sert (temps d'attente maximal de `switch` et `release`). Les autres serviront à la
bascule automatique.

## Dépannage

| Message | Que faire |
|---|---|
| `Aucun AirPods configuré` | lancer `setup` |
| `… n'est plus appairé à ce PC` | ré-appairer les AirPods dans Windows, puis relancer `setup` |
| `Bluetooth du PC coupé` / `Le Bluetooth du PC est indisponible` | activer le Bluetooth dans les paramètres Windows |
| `Les AirPods ne sont pas venus` | boîtier fermé ou hors de portée ; `status` dit s'ils sont éveillés |
| `Windows refuse l'accès au driver… (E_NOINTERFACE)` | supprimer les AirPods des paramètres Bluetooth, les ré-appairer, relancer `setup` |
| `Éveillés : non détectés` alors qu'ils sont portés | relancer `status` ; vérifier que le modèle dans `config.json` correspond à celui affiché par `setup` |

# 6. Utiliser ACAW

ACAW se compose de deux programmes :

| Programme | Rôle |
|---|---|
| `acaw-tray.exe` | l'application résidente : icône dans la barre des tâches, **bascule automatique** |
| `acaw.exe` | la ligne de commande : configuration (`setup`), état (`status`), bascule à la main |

## Une seule fois : installer

1. **Appairer** les AirPods au PC : Paramètres → Bluetooth et appareils → Ajouter un appareil, boîtier
   ouvert, bouton du boîtier maintenu jusqu'au voyant blanc.
2. **Retirer le profil mains libres**, sinon chaque bascule prend 10 à 13 s au lieu de ~2 s
   ([ADR-0001](../adr/0001-mecanisme-bascule.md)) :
   ```bash
   control printers
   ```
   Clic droit sur les AirPods → Propriétés → onglet Services → décocher « Mains libres téléphonie ».
   Contrepartie : pas de micro AirPods sur le PC.
3. **Construire les deux programmes** dans un dossier qui ne bougera plus, depuis le dossier du dépôt :
   ```bash
   dotnet publish src/Acaw.Tray -c Release -o "%LOCALAPPDATA%\Programs\ACAW"
   ```
   ```bash
   dotnet publish src/Acaw.Cli -c Release -o "%LOCALAPPDATA%\Programs\ACAW"
   ```
4. **Lancer** `acaw-tray.exe` (double-clic dans `%LOCALAPPDATA%\Programs\ACAW`), puis, dans le menu de
   l'icône, cocher **« Démarrer avec Windows »**.
5. **Les AirPods sont choisis tout seuls** s'il n'y a qu'une paire d'AirPods appairée au PC : une bulle
   le confirme. S'il y en a plusieurs, une bulle invite à choisir dans le menu de l'icône → **AirPods**.
   Le choix est enregistré dans `%APPDATA%\ACAW\config.json` (adresse, modèle et nom des AirPods).

> Lance `acaw-tray.exe` depuis l'Explorateur ou un terminal Windows ordinaire, **pas depuis un outil
> empaqueté** comme l'application Claude : ses écritures dans `%APPDATA%` seraient redirigées ailleurs
> (voir le [guide 7](07-application-residente.md#démarrer-avec-windows)).

> Le dossier compte : « Démarrer avec Windows » retient le chemin de `acaw-tray.exe`. Lance-le toujours
> depuis `%LOCALAPPDATA%\Programs\ACAW`, pas depuis le dossier de compilation (`bin\…`), qu'une nouvelle
> compilation peut effacer ou verrouiller. Si tu le déplaces, la case apparaît décochée : recoche-la.

## Au quotidien

Il n'y a rien à faire :
- tu lances une vidéo ou une musique sur le PC → les AirPods viennent (~2 s) ;
- tu mets en pause → ils repartent sur l'iPhone 5 s plus tard ;
- un jeu ou un appel Discord : les AirPods viennent si le son dure plus de 2 s, et repartent 30 s
  après la fin du son ;
- pendant que les AirPods arrivent, **les haut-parleurs sont coupés** : le son ne sort pas dans la pièce.
  La lecture continue (tu peux perdre 1 à 2 s du début). Si les AirPods ne viennent pas (posés trop
  loin…), le son revient sur les haut-parleurs au bout de 6 s maximum.

Et si les AirPods ne sont pas là ?

| Situation | Ce qui se passe |
|---|---|
| boîtier fermé, ou AirPods hors de portée du Bluetooth | ACAW ne tente rien : le son sort normalement des haut-parleurs |
| AirPods sortis mais injoignables (autre pièce, pris par un autre appareil qui refuse) | ACAW essaie ; au pire 6 s de silence, puis le son revient sur les haut-parleurs, et l'essai s'arrête au bout de 30 s |
| Bluetooth du PC coupé, AirPods plus appairés | ACAW ne tente rien ou échoue proprement ; l'erreur est notée dans le journal |

Dans aucun cas ACAW ne bloque la lecture ni ne s'arrête : il ne met en pause que si on lui demande de rendre
les AirPods, ou si l'iPhone les reprend pendant une lecture.

Le menu de l'icône (clic droit) :

| Entrée | Effet |
|---|---|
| (ligne grisée) | l'état actuel |
| Amener sur ce PC | prend les AirPods tout de suite, et les garde même sans son |
| Rendre à l'iPhone | met le PC en pause et relâche les AirPods |
| AirPods ▸ | les AirPods appairés au PC, avec une coche sur ceux qu'ACAW pilote. Cliquer une autre paire la fait piloter **tout de suite**, sans relancer ACAW. En bas : « Paramètres Bluetooth… » pour en appairer de nouveaux |
| Bascule automatique | décocher pour suspendre (réunion, film sur la TV…) ; le choix est retenu |
| Démarrer avec Windows | lancer ACAW à l'ouverture de session |
| Ouvrir le journal | le détail de ce qu'ACAW a vu et fait |
| Quitter | arrête ACAW |

La couleur de l'icône : 🟢 sur ce PC · 🟠 en cours · gris-bleu ailleurs · gris clair suspendu.

## La ligne de commande

| Commande | Effet |
|---|---|
| `setup [numéro]` | choisir les AirPods (sans numéro, affiche la liste et demande). Facultatif depuis que le menu de l'icône le fait ; si ACAW tourne, il prend le nouveau choix en compte tout seul |
| `status` | connectés à ce PC ? éveillés ? combien d'écouteurs portés ? batteries |
| `switch` | amener les AirPods sur ce PC, avec le temps mis |
| `release` | rendre les AirPods : l'iPhone les reprend en 2 à 3 s |

```
> acaw status
AirPods : Casque (AirPods Pro) (modèle 2027)
Sur ce PC : non (ailleurs ou libres)
Écoute BLE pendant 5 s…
Éveillés : oui · écouteurs portés : 2 · dans le boîtier : 0
Batteries : écouteurs 80 % / 80 % · boîtier —
```

Les batteries ne disent pas quel écouteur est le gauche : les annonces BLE ne permettent pas encore de
le savoir. La batterie du boîtier n'est connue que si un écouteur y est posé.

En développement, `dotnet run --project src/Acaw.Cli -- status` fait la même chose sans publier.

## Les réglages

`%APPDATA%\ACAW\config.json` contient l'état de la bascule automatique (`AutoSwitch`) et les délais de
l'[ADR-0003](../adr/0003-regles-de-bascule.md) : `PauseDelay` (5 s), `SilenceDelay` (30 s), `Cooldown`
(5 s), `AwakeWindow` (30 s), `SwitchTimeout` (30 s), `SoundConfirmDelay` (2 s), `SilenceConfirmDelay`
(2 s), `UnmuteDelay` (1 s), `MaxMuteDuration` (6 s), et `MuteWhileSwitching` (`true` ; `false` pour ne
jamais couper les haut-parleurs). On peut les modifier à la main (format `"00:00:05"`) : **ACAW relit le
fichier tout seul** une demi-seconde après l'enregistrement, sans qu'il faille le relancer.

| Ce qui change dans le fichier | Effet |
|---|---|
| `AutoSwitch` seul | appliqué directement, comme la case du menu |
| `Name` seul | rien (il ne sert qu'à l'affichage) |
| adresse, modèle ou un délai | ACAW redémarre son moteur avec les nouveaux réglages. Une bascule en cours est interrompue, les AirPods restent où ils sont, et le son coupé est rétabli |
| fichier abîmé (virgule oubliée…) | ACAW **garde les réglages en cours** et le signale par une bulle. Il suffit de corriger et d'enregistrer |
| fichier supprimé | ACAW garde les réglages en cours ; s'il n'y a qu'une paire d'AirPods, il recrée le fichier avec les délais par défaut |

## Dépannage

| Symptôme ou message | Que faire |
|---|---|
| « Recherche des AirPods… » | normal juste après l'ouverture de session : Windows ne liste pas encore les casques. ACAW réessaie toutes les 30 s, et à chaque ouverture du menu |
| « Aucun AirPods appairé à ce PC » | appairer les AirPods (menu → AirPods → Paramètres Bluetooth…) : ACAW les prend dès qu'il les voit |
| « Plusieurs AirPods sont appairés » | choisir les siens : menu → AirPods |
| `Configuration illisible (…)` | le fichier `config.json` a été abîmé : le corriger, ou choisir les AirPods dans le menu, qui le réécrit |
| `… n'est plus appairé à ce PC` | ré-appairer les AirPods, ou en choisir d'autres dans le menu. Si une seule autre paire est appairée, ACAW la prend tout seul |
| `Bluetooth du PC coupé` / `Le Bluetooth du PC est indisponible` | activer le Bluetooth dans les paramètres Windows |
| les AirPods ne viennent pas | boîtier fermé ou hors de portée ? `acaw status` dit s'ils sont éveillés ; sinon voir le journal |
| ils viennent pour une simple notification | augmenter `SoundConfirmDelay` dans `config.json` |
| ils repartent trop vite après une pause | augmenter `PauseDelay` |
| ACAW ne démarre pas avec Windows | ouvrir le menu de l'icône : si « Démarrer avec Windows » est décoché, le cocher. Vérifier que `ACAW.lnk` est dans le dossier Démarrage (Win + R, `shell:startup`) et qu'ACAW est « Activé » dans Paramètres → Applications → Démarrage. Le journal commence chaque lancement par une ligne « Lancement de … (Windows démarré il y a …) » ; si le journal lui-même est en panne, la raison est dans `%TEMP%\acaw-journal-en-panne.log` |
| le PC est resté muet (ne devrait jamais arriver) | relancer `acaw-tray.exe` : il rétablit au démarrage le son qu'il avait coupé ; sinon rétablir le son à la main et envoyer le journal |
| `Windows refuse l'accès au driver… (E_NOINTERFACE)` | supprimer les AirPods des paramètres Bluetooth, les ré-appairer, puis les choisir dans le menu |
| `Éveillés : non détectés` alors qu'ils sont portés | relancer `status` ; vérifier que le modèle dans `config.json` correspond à celui affiché par `setup` |

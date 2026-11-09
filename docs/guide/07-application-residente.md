# 7. L'application résidente

`acaw-tray.exe` tourne en permanence, sans fenêtre, avec une icône dans la barre des tâches. C'est elle
qui fait la bascule automatique. Ce guide explique comment elle est construite.

## Pourquoi pas un « service » Windows ?

Un service Windows tourne dans une session à part (la session 0), sans accès au son ni aux lecteurs
média de l'utilisateur ([ADR-0002](../adr/0002-modele-processus.md)). ACAW est donc une application
normale, lancée à l'ouverture de session, qui se comporte comme un service : pas de fenêtre, une icône.

Pour qu'aucune console noire n'apparaisse, le projet `Acaw.Tray` est de type **`WinExe`** (« exécutable
Windows ») au lieu de `Exe` (« exécutable console »). Il est donc séparé de la ligne de commande `acaw.exe`,
qui, elle, a besoin d'une console.

## Ports et adaptateurs

L'hôte (`Acaw.Core/Hosting/AcawHost.cs`) a besoin du monde extérieur : le casque, les lecteurs média, le
volume, l'écoute BLE. Plutôt que d'appeler Windows directement, il passe par des **interfaces** décrites
dans `Acaw.Core/Hosting/Ports.cs` :

```csharp
public interface IHeadsetPort        // le casque
{
    HeadsetState GetState();
    void Connect();
    void Disconnect();
    event Action<HeadsetState>? StateChanged;
}
```

Une **interface** est un contrat : « je sais faire ceci », sans dire comment. Deux familles de classes
le remplissent :

| Port | En vrai (`Acaw.Windows`) | Dans les tests (`Fakes.cs`) |
|---|---|---|
| `IHeadsetPort` | `WindowsHeadset` : reconnexion KS, notifications MMDevice | `FakeHeadset` : note les appels, simule Windows |
| `IMediaPort` | `MediaSessions` : contrôles média de Windows (SMTC) | `FakeMedia` |
| `ISoundMeter` | `AudioMeter` : volume crête des sessions audio | `FakeMeter` : un volume réglable |
| `IAirPodsRadar` | `AirPodsRadar` : écoute BLE | `FakeRadar` |

C'est ce qu'on appelle l'architecture **« ports et adaptateurs »**. Elle permet de tester l'hôte entier,
de bout en bout, sans AirPods, sans Bluetooth et sans attendre ([guide 8](08-strategie-de-test.md)).

## La file d'événements

Tout ce qui arrive est déposé dans **une seule file** (`Channel`), puis traité **un élément à la fois** :

```
 Windows (threads variés)                       Thread de l'hôte (un seul à la fois)
 ─────────────────────────                      ────────────────────────────────────
 casque connecté      ─┐                         ┌─► tracker audio ─┐
 lecteur en pause     ─┤                         │                  ▼
 annonce BLE          ─┼─► Channel (file) ──────►┤             machine à états ─► commandes
 clic sur le menu     ─┤                         │                  ▲              (Connect,
 battement 250 ms     ─┘                         └──────────────────┘               Disconnect…)
```

**Pourquoi une file ?** Windows prévient ACAW depuis plusieurs threads en même temps. Si chacun appelait
la machine à états directement, deux événements pourraient la modifier au même instant et la mettre
dans un état incohérent : c'est une **condition de course**. La file met tout le monde en rang : l'ordre
est conservé et la machine n'est jamais appelée deux fois à la fois.

**Le battement** : toutes les 250 ms, un minuteur ajoute un élément dans la file. À ce moment, l'hôte :
1. lit le volume crête et le passe au **tracker** (`AudioActivityTracker`), qui décide si « ça joue » ;
2. envoie un `Tick` à la machine, qui vérifie ses échéances (relâche dans 5 s, etc.).

**La protection contre les erreurs** : chaque élément est traité dans un `try … catch`. Une erreur
imprévue est notée dans le journal, et l'application continue.

## Le tracker audio

Il transforme deux signaux bruts en événements pour la machine :

| Signal | Source | Règle |
|---|---|---|
| un lecteur média joue (YouTube, Spotify…) | contrôles média de Windows | immédiat ; sa pause = `PlaybackPaused` (relâche en 5 s) |
| du son sans lecteur (jeu, Discord) | volume crête, hors sons système | compte seulement après **2 s** de son continu (une notification ne déclenche rien) ; fin après 2 s de silence = `SilenceDetected` (relâche en 30 s) |

## Le réveil après la veille

Au réveil, Windows prévient l'application (`SystemEvents.PowerModeChanged`). L'hôte relit alors
l'état réel du casque et des lecteurs, et relance l'écoute BLE. Si l'écoute BLE s'arrête toute seule
(Bluetooth coupé), elle est relancée toutes les 10 s jusqu'à ce que ça marche.

Par sécurité, l'hôte relit aussi l'état du casque toutes les 30 s, même sans notification de Windows.

## Pas de son dans la pièce pendant l'arrivée des AirPods

Entre le début d'une vidéo et l'arrivée des AirPods (~2 s), Windows envoie le son aux haut-parleurs. La
règle R7 de l'[ADR-0003](../adr/0003-regles-de-bascule.md) les coupe pendant ce temps :

```
lecture démarre ──► Reconnect + MuteSpeakers          (haut-parleurs coupés, la vidéo continue)
AirPods arrivés ──► 1 s plus tard : UnmuteSpeakers    (Windows a passé le son aux AirPods)
pas d'AirPods   ──► 6 s au plus tard : UnmuteSpeakers (le son revient sur les haut-parleurs)
```

Trois sécurités pour ne jamais laisser le PC muet :
1. la machine rétablit le son dans **tous** les cas (vérifié par les invariants : 2 000 séquences au hasard) ;
2. `SpeakerMute` ne rétablit que la sortie qu'il a lui-même coupée : si tu l'avais coupée toi-même, il n'y
   touche pas ;
3. avant de couper, il l'écrit dans `%LOCALAPPDATA%\ACAW\speakers-muted.txt`. Si ACAW s'arrête
   brutalement, le démarrage suivant lit ce fichier et rétablit le son.

## L'icône et le menu

`Acaw.Tray/TrayApp.cs` utilise **WinForms**, la bibliothèque d'interface historique de Windows :
- `NotifyIcon` est l'icône de la barre des tâches ;
- `ContextMenuStrip` est son menu du clic droit.

L'hôte signale chaque changement d'état depuis son propre thread, alors que WinForms exige qu'on ne
touche à l'interface que depuis le thread de l'interface. `BeginInvoke` sert à « poster » la mise à jour
vers ce thread.

| Couleur | État |
|---|---|
| 🟢 vert | AirPods sur ce PC |
| 🟠 orange | connexion ou relâche en cours |
| gris-bleu | AirPods ailleurs (iPhone) ou libres |
| gris clair | bascule automatique suspendue |

## Démarrer avec Windows

Le menu « Démarrer avec Windows » écrit **deux** valeurs `ACAW` dans `HKCU`, la partie du registre
propre à l'utilisateur (aucun droit administrateur n'est nécessaire) :

| Clé | Contenu | Rôle |
|---|---|---|
| `Software\Microsoft\Windows\CurrentVersion\Run` | le chemin de `acaw-tray.exe` | **quoi** lancer à l'ouverture de session |
| `Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` | 12 octets, le premier vaut `02` (activé) ou `03` (désactivé) | **si** Windows a le droit de le lancer : c'est l'interrupteur de la page Paramètres → Applications → Démarrage |

> **Pourquoi deux ?** Au début, ACAW n'écrivait que la première. Le 4 novembre 2026, on a constaté dans le
> journal de Windows (`Microsoft-Windows-Shell-Core/Operational`, événements 9705 à 9708) que la commande
> n'était **jamais lancée**, cinq démarrages de suite : Windows ne lançait que les programmes marqués
> `02` dans la seconde clé, et ACAW n'y figurait pas. La case du menu, elle, restait cochée.

Le code est dans `src/Acaw.Windows/StartupEntry.cs` :
- la case n'est cochée que si **les deux** valeurs disent oui, et si le chemin est bien celui du programme
  lancé (sinon, après un déplacement, Windows lancerait l'ancien) ;
- cocher la case réécrit les deux valeurs : c'est une demande explicite, qui prime sur une ancienne
  désactivation dans les Paramètres ;
- décocher la case supprime les deux valeurs, et rien d'autre.

Si tu désactives ACAW dans **Paramètres → Applications → Démarrage**, la case du menu se décoche toute
seule : les deux disent la même chose.

## Le journal

Tout est noté dans `%LOCALAPPDATA%\ACAW\acaw.log` (menu « Ouvrir le journal ») : événements, commandes,
erreurs. Exemple :

```
2026-11-02 13:40:02.115  Événement PlaybackStarted (Away → Switching)
2026-11-02 13:40:02.116  Commande Reconnect
2026-11-02 13:40:03.871  Événement EndpointConnected (Switching → OnWindows)
```

Ici, les AirPods sont arrivés 1,76 s après le début de la lecture. Le journal servira à mesurer les
temps réels en phase 3. Au-delà de 1 Mo, il est renommé `acaw.old.log` et un nouveau commence.

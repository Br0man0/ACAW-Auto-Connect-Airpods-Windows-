# ACAW : Auto Connect AirPods Windows

Bascule automatique des AirPods entre un PC **Windows** et un iPhone :
- quand le PC se met à jouer du son, les AirPods quittent l'iPhone et viennent sur le PC (~2 s) ;
- quand le PC se met en pause ou se tait, il les relâche, et l'iPhone les reprend.

Aucun driver, aucun droit administrateur, aucun accès réseau.

## Installation

Prérequis : Windows 10/11 (64 bits), Bluetooth activé, AirPods appairés au PC.

1. Télécharger `acaw-setup-X.Y.Z.exe` dans les **Releases** et l'exécuter (.NET inclus). L'installateur n'est
   pas signé : « Informations complémentaires → Exécuter quand même ».
2. ACAW se lance, démarre avec Windows et choisit tout seul les AirPods s'il n'y en a qu'une paire ; sinon,
   menu de l'icône → AirPods.

Pour des bascules rapides (~2 s au lieu de ~10 s), décocher **Micro des AirPods** dans le menu de l'icône quand
le micro ne sert pas pour un appel.

Une version portable (`acaw-X.Y.Z-portable.zip`) est aussi disponible : à dézipper puis lancer `acaw-tray.exe`.

## Compiler

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download) :

```bash
dotnet build ACAW.sln -c Release
```

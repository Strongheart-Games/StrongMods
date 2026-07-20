# PlayerSpawnedTraders

Placeable trader mannequins that grow into working traders.

* Adds a single craftable-menu block, "Placeable Trader", that selects between the five vanilla traders with the
  variant (`R`) menu: Rekt, Jen, Bob, Hugh, and Joel.
* Placing the block puts down a mannequin that marks the spot and shows the trader's facing direction; it is rotatable
  in 90 degree steps.
* The mannequin is a `PlantGrowing` block, so after a short delay it disappears and the matching trader spawn block
  takes its place. The trader itself may take up to another 30 seconds to appear.
* The spawned traders may not offer all — or any — of the quests a normal trader would; these are intended for
  trading only.
* Once placed, a trader cannot be moved.
* XML-only modlet; there is no DLL.

## Installation

* Copy the `PlayerSpawnedTraders/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/PlayerSpawnedTraders/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Install on the server *and* on every client, since the mod adds new blocks
  * EAC-friendly — no code, XML patches only
* All other deployments:
  * Deploy to host (in single-player this is your game)
* There are no configuration options for now

## Changelog

### 0.0.1

* Initial release
* Adds placeable mannequins for the five vanilla traders

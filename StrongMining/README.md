# StrongMining

Terrain and ore blocks that regenerate themselves when broken.

* XML-only modlet — there is no DLL.
* Adds "Strong" variants of nine vanilla terrain blocks: iron, lead, coal, potassium nitrate and oil shale ore, plus
  stone, dirt, sand and snow.
* Each variant extends its vanilla counterpart and sets `DowngradeBlock` to itself, so mining it yields the normal
  resources and leaves the block in place instead of depleting it.
* Each variant reuses the vanilla icon tinted gold (`ffda03`) so they are easy to tell apart in the creative menu.
* Ships the `part_tractor_depot` prefab part (25 x 3 x 25) built around these blocks, for use in world generation.
* The blocks are not craftable — place them from the creative menu or through a prefab.

## Installation

* Copy the `StrongMining/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongMining/ModInfo.xml`, otherwise the mod
  won't be loaded
* Dedicated servers:
  * Server-side only — the block definitions are synced to clients and the prefab part is only used during world
    generation
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; the block list lives in `Config/blocks.xml` if you want to add more

## Changelog

### 0.0.1

* Initial release
* Only works against 7DtD v3.x

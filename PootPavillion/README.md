# PootPavillion

Adds a craftable toilet that generates Tinkle and Dookie over time.

* XML-only modlet — there is no DLL.
* Crafted from 10 wood, 4 metal pipe and 30 clay lump.
* Uses the vanilla `PlantGrowing` mechanic (growth rate 20) to mature from the empty Poot Pavillion into the
  "Poot Pavillion (FULL)" stage. It needs no light and no fertile ground, so it works underground.
* Looting the full stage yields 1 Dookie and 1-5 Tinkle from a 5x5 container, after which the block downgrades back
  to the empty stage and starts again.
* Adds two resource items, Tinkle and Dookie. Both stack to 500, have no economic value and cannot be sold to traders.
* Tinkle is useful: 2 Tinkle cook into 1 jar of boiled water at a campfire (20 seconds). Dookie currently has no
  recipe of its own.

## Installation

* Copy the `PootPavillion/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/PootPavillion/ModInfo.xml`, otherwise the
  mod won't be loaded
* Dedicated servers:
  * Server-side only for the mechanics — block, item, recipe and loot XML are synced to clients
  * EAC-friendly
  * Clients without the mod installed will see raw localization keys instead of the block and item names and
    descriptions, so installing it on clients too is recommended
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; the blocks, items, recipes and loot table live under `Config/`

## Changelog

### 1.0.0

* Initial public release
* Only works against 7DtD v3.x
* The loot prompt text is not customised yet — the `LootView.Prompt` block property is missing in 7DtD 3.0

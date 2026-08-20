# ProjectZFixes

Fixes for critical issues in Project Z 3.x.

* Requires the Project Z overhaul mod, version 3.x. Without it this mod does nothing useful.
* Renames the Bitch mini-boss to Banshee and the Carrier boss to Patient Zero throughout their player-visible English
  localization, including entity names, buffs, perks, summon items and quests, objectives, story text, and weapon
  descriptions.
* Project Z boss loot containers now explicitly declare their inherited loot-container class. This makes them usable by
  mods that enumerate direct loot-container declarations, including AutoCollectLoot.

## Installation

* Copy the `ZZ_ProjectZFixes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/ZZ_ProjectZFixes/ModInfo.xml`, otherwise the
  mod won't be loaded
* Server-side only
* EAC friendly

The mod folder is prefixed with `ZZ_` to ensure it loads after Project Z itself.

## Changelog

### 2.2.4.4

* Complete the Banshee rename in its summon item and quest.
* Rename the Carrier boss to Patient Zero throughout the English localization in Project Z 3.1.2.

### 2.2.4.3

* Explicitly declare `EntityLootContainer` for Project Z boss loot containers that inherit it from `BossMasterLoot`.

### 2.2.4.2

* Initial release, targeting Project Z 2.2.4.1

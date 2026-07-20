# ProjectZFixes

Fixes for critical issues in Project Z version 2.2.4.1.

* Requires the Project Z overhaul mod, version 2.2.4.1. Without it this mod does nothing useful.
* Quest and event rewards no longer air-drop. `action_spawn_reward_*` spawns have `air_spawn` disabled and are
  placed 3 to 8 meters from the player, so rewards can no longer fall through terrain or land out of reach.
* Defender Z armor mods (`modRareArmorARResist*`) now grant +5 Hypothermal and +5 Hyperthermal resistance, since
  they occupy the thermal armor mod slot and would otherwise cost you all of your insulation.
  * Their recipes now also require one `modArmorInsulatedLinerT3` to match the added insulation.
* Scrapping is faster: a 10 second `ScrapTimeOverride` becomes 7 seconds and a 15 second override becomes 10. Still
  slow enough that an accidental scrap can be cancelled.
* Adds the missing English localization for the Banshee mini-boss: its entity name, its `BuffBitchAOE` and
  `BuffReducedProtectionAOE` buff strings, and the Banshee perk with all 10 of its rank descriptions.

## Installation

* Copy the `ZZ_ProjectZFixes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/ZZ_ProjectZFixes/ModInfo.xml`, otherwise
  the mod won't be loaded
* Must be installed on both the server and every client, since it changes items, recipes, item modifiers, and
  localization
* There are no configuration options

The mod folder is prefixed with `ZZ_` to ensure it loads after Project Z itself.

## Changelog

### 2.2.4.2

* Initial release, targeting Project Z 2.2.4.1

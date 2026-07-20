# AutoCollectLoot

Send loot bags directly to players rather than dropping them on the ground.

* When a tagged entity dies, its loot bag is converted into a single `AutoLoot_*` inventory item and handed straight
  to a player instead of spawning a bag in the world. Opening the item rolls the same loot list the original bag would
  have used.
* An auto-loot item is generated for every loot-container entity in the game, carrying over the bag's mesh, icon and
  tint where possible, so modded loot bags are covered as well as vanilla ones.
* The recipient is chosen by lottery: the player who landed the killing blow wins outright at a configurable chance,
  otherwise the winner is drawn from the killer's party members within the game's shared-kill range (or, if there was
  no player killer, from all players within that range).
* By default this only applies during blood moons. Horde-night zombies are always eligible; other entities get a
  5-minute marker buff that expires afterwards.
* If the winner's inventory is full, the item is spawned as a pickup assigned to them with a 20-minute lifetime rather
  than being lost.
* Bad-luck protection is not implemented yet, and the marker buff is not refreshed periodically during horde night.

## Dependencies

**`StrongMods` is required.** AutoCollectLoot's `items.xml` is generated with the `<foreach>` XML-patch templating
engine that `StrongMods` provides, and the mod will not work without it.

## Installation

* Install [`StrongMods`](../StrongMods/README.md) first
* Copy the `AutoCollectLoot/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/AutoCollectLoot/ModInfo.xml`, otherwise the
  mod won't be loaded
* Load this mod at the end of your load order — it enumerates loot containers defined by other mods, and `<foreach>`
  can only see mods that loaded before it
* This mod adds items and buffs, so it must be installed on both the server and every client — it is not server-side
  only
* Dedicated servers:
  * Install on the server and on every client
  * EAC must be disabled
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled

## Configuration

Settings live in `Config/worldglobal.xml`, under the `auto_collect_loot` property class, and are re-read whenever the
XML is (re)loaded:

* `enable` — master switch (default `true`)
* `enable_outside_blood_moons` — apply outside of blood moons too (default `false`)
* `killshot_bonus_lottery_chance` — chance the killer wins the bag outright, between 0 and 1 (default `0.5`)

The first two can also be toggled at runtime from the server console with the `autoloot` command:

```
autoloot                            # show current state
autoloot enable | disable
autoloot enableoutsidebloodmoon | disableoutsidebloodmoon
```

Runtime changes made with `autoloot` are not persisted and are overwritten the next time the XML is reloaded.

## Changelog

### 0.1.0

* Initial pre-release

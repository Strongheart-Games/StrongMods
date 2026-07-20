# AECInternationalMarketFixes

Fixes for the AEC International Market mod, plus a starter tutorial quest.

* Requires the AEC International Market mod (`AEC_InternationalMarket`). The patches are wrapped in a
  `mod_loaded('AEC_InternationalMarket')` conditional, so this mod does nothing useful without it.
* Adds an `aecIMTutorialBook` item, granted to every player on first spawn, that starts a repeatable tutorial quest
  (`aec_im_tutorial`).
  * The book is not granted when `StrongholdTweaks` is also loaded, since that mod hands out the starter items
    itself.
  * The quest walks the player through harvesting 100 zombie ears, crafting and placing an International Market
    terminal, requesting a Crypto Miner, and placing it. It rewards 5000 XP.
* Rebases the International Market's `airdrop_*` entities onto the vanilla `twitch_crate_template` and strips their
  extra properties, keeping only `UserSpawnType`, `Mesh`, `LootList`, and `LootListOnDeath`, so they behave like
  normal supply crates.
* Air drops are delivered with `SpawnContainer` instead of `SpawnEntity`, and land 4-8 meters from the requesting
  player instead of the mod's default distances.
* Prices Connexion Cards (`modelConnexionCard`) at an economic value of 500 with a bundle size of 1.
* Renames roughly 7,700 air drop request items to a consistent `zIM Airdrop Request for 1 <item>` form, so they
  group together and can all be found by searching for `zIM`.
* This is an XML-only modlet; there is no DLL.

## Installation

* Copy the `AECInternationalMarketFixes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e.
  `Mods/AECInternationalMarketFixes/ModInfo.xml`, otherwise the mod won't be loaded
* Load this mod *after* AEC International Market
* This mod adds items, entities, quests, and localization, so it must be installed on **both the server and every
  client** — it is not server-side only
* There are no configuration options for now

## Changelog

### 0.0.1

* Initial release

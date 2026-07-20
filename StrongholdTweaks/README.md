# StrongholdTweaks

Stronghold-specific game tweaks.

* XML-only modlet — there is no DLL. It is the server-specific balance and content pack for the Stronghold
  server, so most of it is opinionated rather than general-purpose.
* **Player and loot balance** — player health and stamina raised from 100 to 200, a rewritten starter kit, and
  almost every stackable item and block bumped to a stack size of 65000.
* **Spawning** — progressive biome difficulty: zombie max counts rise and respawn delays fall the harsher the
  biome gets, from roughly 2x in the pine forest to 4x in the wasteland.
* **New content** — StrongStone blocks that trigger a horde-spawn game event, 36 indestructible auto-replanting
  "StrongCrop" blocks with their seed recipes, a hybrid vehicle kit modifier, a stronghold map item, and a
  `quest_find_stronghold` quest with a matching challenge that unlocks bed fast travel.
* **Quality of life** — workstations and campfires can be rotated freely, read schematics are tinted green,
  blood rain runs on a real-world schedule three times a day, and the server-rules join dialog is made readable.
* Several patches are gated on other mods being loaded (`ProjectZ`, `Z_Bosses`, `PootPavillion`,
  `ChristmasCookbook`, and others) and do nothing without them.
* It is built to **load last** — the deployed folder name is prefixed `ZZZZZZZZZZ_` so its overrides win over
  the mods it adjusts.

## Installation

* Copy the `StrongholdTweaks/` directory into `Mods/`, renamed so it sorts last — the build deploys it as
  `Mods/ZZZZZZZZZZ_StrongholdTweaks`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e.
  `Mods/ZZZZZZZZZZ_StrongholdTweaks/ModInfo.xml`, otherwise the mod won't be loaded
* This modlet adds new blocks, items, entity classes, recipes, quests, challenges and localization, so it must
  be installed on **both the server and every client** — it is not server-side only
* Dedicated servers:
  * EAC-friendly (no DLL)
* All other deployments:
  * Deploy to host (in single-player this is your game)
* Configuration is the patch set itself, under `Config/` — edit the XML to change any of the values above
* `Saves/StrongMods/custom_chat_commands.xml` is *not* part of the mod folder: it is the chat-command
  definitions (`/horde`, `/stronghold`, `/bed`, `/resetdrone`, `/resetme`) read from the save game directory,
  and requires the mods that implement custom chat commands

## Changelog

### 13.0.0

* Current Stronghold server configuration
* Only works against 7DtD v3.x

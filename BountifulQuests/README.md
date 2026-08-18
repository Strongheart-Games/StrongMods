# BountifulQuests

Traders offer bigger, tunable quest lists. Players can take more than one quest from a trader and can reset the offered
list at any time.

* **Bigger offer list.** A trader offers a configurable number of quests per tier page — 4 to 64, against vanilla's
  fixed 7. The trader dialog and its response window are widened to match.
* **Tunable selection.** Per-quest-type weights and caps, a hard distance window, band preference, page ordering, and
  page-level repeat avoidance. Vanilla draws uniformly and cannot cap distance at all.
* Removes the vanilla requirement that a player have no active quest from a trader before that trader will offer more
  work, so quests can be stacked up from several traders at once.
* Moves the "reset quests" dialog option out of the trader's admin menu and into the normal player menu, so any player
  can refresh the offered quest list on demand.
* **Every default reproduces vanilla.** Installing the mod and changing nothing changes nothing.

`Docs/configuration.md` covers every setting and what vanilla does instead.

## Installation

* Copy the `BountifulQuests/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/BountifulQuests/ModInfo.xml`, otherwise the
  mod won't be loaded
* **Server-side only.** Install on the server; clients need nothing. The `dialogs.xml` and `XUi_InGame/windows.xml`
  files this mod patches are both server-synced — the server sends its patched copies to every joining client.
* Requires **StrongMods**
* EAC must be disabled

## Configuration

Settings live at `<save game folder>/StrongMods/BountifulQuests.xml`, written with the shipped defaults on first start.
They are read once at game awake, so changing them means restarting the server. See `Docs/configuration.md`.

## Changelog

### 3.0.0 (prototype, not released)

* Configurable offer count per tier page, 4 to 64, replacing vanilla's fixed 7
* Configurable quest-type weights and per-type caps
* Hard minimum and maximum quest distance, configurable band edges and band preference
* Configurable page ordering and page-level repeat avoidance
* Trader dialog widened to 24 response rows, up to 21 of them quests
* Corrected the installation note: this mod is server-side only, and always was

### 2.0.0

* Traders always offer jobs, even when the player is already holding a quest from that trader
* Quest reset moved from the trader admin dialog to the player dialog

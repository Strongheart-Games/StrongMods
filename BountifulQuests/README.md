# BountifulQuests

Players can take more than one quest from a trader and can reset the list of offered quests at any time.

* Removes the vanilla requirement that a player have no active quest from a trader before that trader will offer more
  work, so quests can be stacked up from several traders at once.
* Moves the "reset quests" dialog option out of the trader's admin menu and into the normal player menu, so any player
  can refresh the offered quest list on demand.
* Implemented entirely as a `dialogs.xml` patch; no quest, reward, or difficulty tuning is changed.
* The mod ships a DLL, but the Harmony patch inside it (`QuestEventManager.GetQuestList`) is currently an
  unimplemented stub and changes nothing.

## Installation

* Copy the `BountifulQuests/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/BountifulQuests/ModInfo.xml`, otherwise the
  mod won't be loaded
* This mod patches trader dialog XML, so it must be installed on both the server and every client — it is not
  server-side only
* Dedicated servers:
  * Install on the server and on every client
  * EAC must be disabled
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now

## Changelog

### 2.0.0

* Traders always offer jobs, even when the player is already holding a quest from that trader
* Quest reset moved from the trader admin dialog to the player dialog

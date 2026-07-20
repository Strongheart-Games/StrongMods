# QuestUnlockFixes

Fix `UnlockQuestPOI()` that prevents players from fully logging out.

* Vanilla `QuestEventManager.QuestUnlockPOI` dereferences the prefab it looks up from the player's position
  before checking whether that lookup succeeded. When it returns nothing — for example when the POI is not
  loaded, or the position is outside any prefab — the resulting exception leaves the player stuck part-way
  through logging out.
* This mod reorders the null check ahead of the dereference via a transpiler, so the method exits cleanly
  instead of throwing.
* When the lookup does fail, it logs an error with the entity ID, the position and a stack trace, so the
  underlying cause is still visible rather than silently swallowed.
* Behaviour is otherwise unchanged: quest POI unlocking works exactly as vanilla when the prefab is found.

## Installation

* Copy the `QuestUnlockFixes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/QuestUnlockFixes/ModInfo.xml`,
  otherwise the mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now

## Changelog

### 1.0.0

* Initial public release
* Only works against 7DtD v3.x

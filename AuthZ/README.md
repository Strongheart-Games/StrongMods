# AuthZ

Server-side authorization checks that detect griefing, such as unauthorized damage to other players and their
vehicles.

* **Work in progress: detection and logging only.** The check runs, logs a `[AuthZ] PvE violation: ...` warning, and
  then lets the packet through unchanged. Nothing is blocked, reverted, or punished yet — rejecting the damage (and
  optionally banning the sender) is still a TODO.
* Inspects incoming `NetPackageDamageEntity` packets on the server and decides whether the sender was allowed to
  damage the target entity.
* Players may only damage themselves; damaging another player is a violation.
* Vehicles may be damaged by anyone attached to them (driver or passenger). An unoccupied vehicle may only be damaged
  by its owner.
* Only active when the server's `PlayerKillingMode` game preference is `NoKilling`. Other killing modes are not
  enforced yet.
* Damage to drones and robots is not checked yet. Anything else (blocks, zombies, animals) is always allowed through.

## Installation

* Copy the `AuthZ/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/AuthZ/ModInfo.xml`, otherwise the mod won't
  be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; behavior is driven by the server's existing `PlayerKillingMode` game
  preference

## Changelog

### 0.0.1

* Initial pre-release
* Detects and logs unauthorized damage to players and vehicles; does not yet prevent it

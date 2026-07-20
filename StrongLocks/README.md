# StrongLocks

All lockables default to locked when placed, including doors, vehicles, and crates.

* Any block placed or changed that is a composite tile entity with a lockable feature (doors, storage crates, safes,
  and the like) is locked immediately after placement.
* Blocks that are already locked are left alone; the mod never unlocks anything.
* Vehicles are locked as they spawn into the world — vanilla is the only player-owned entity type that does not
  auto-lock — and the lock state is synced to clients right away.
* No password is set for you; you still own the lock and set its code the usual way.
* This is a behavioural mod only: no new blocks, items, or recipes are added.

## Installation

* Copy the `StrongLocks/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongLocks/ModInfo.xml`, otherwise the mod
  won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now

## Changelog

### 0.0.1

* Initial release
* Only works against 7DtD v3.x

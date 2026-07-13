# AutoCloseDoors

Auto-close the main trader doors. Help prevent zombie ambushes in trader areas.

* For now only works with the main trader doors (gates and chain-link fence doors).
* Will not close if a player is within 10 meters of the door.

## Installation

* Copy the `AutoCloseDoors/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/AutoCloseDoors/ModInfo.xml`, otherwise the
  mod
  won't be loaded
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
* Compiled and tested against 7DtD v3.0.1 b4

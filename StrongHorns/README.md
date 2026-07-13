# StrongHorns

Horns open trader doors.

* Works with any Vanilla vehicle.
* Should work with custom vehicles that can honk; please report any that don't work.
* Each honk toggles the door state.
* Player must be within 15 meters of the target door.
* For now, this only works on the main trader doors (gates and chain-link fence doors).

## Installation

* Copy the `StrongHorns/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongHorns/ModInfo.xml`, otherwise the mod
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
* Compiled and tested against 7DtD v3.0.0 b259

### 1.0.1

* Don't open locked doors that the driver does not have permission to open, e.g. when the trader is closed
* Only works against 7DtD v3.x
* Compiled and tested against 7DtD v3.0.1 v4

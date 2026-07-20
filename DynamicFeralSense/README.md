# DynamicFeralSense

Set feral sense, which is normally global, on a per-biome basis.

* Scales the server's configured feral sense setting by a per-biome multiplier, so turning feral sense off in
  `serverconfig.xml` still disables it everywhere.
* The biome a player or zombie is standing on decides the multiplier:
  * Pine Forest: 0%
  * Burnt Forest: 25%
  * Desert: 50%
  * Snow: 75%
  * Wasteland: 100%
* Applies to both zombie sight distance and how far player noise carries.
* Any biome not in the list above falls back to 20% and logs an error once every 5 seconds.

## Installation

* Copy the `DynamicFeralSense/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/DynamicFeralSense/ModInfo.xml`, otherwise the
  mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; the biome multipliers are compiled in

## Changelog

### 1.0.0

* Initial public release
* Feral sense now ranges from 0-100% across the biomes rather than 20-100%

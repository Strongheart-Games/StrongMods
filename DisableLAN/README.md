# DisableLAN

Prevents the LAN announcer from binding to port 11000 and taking up resources.

* Skips creation of the game's LAN listener entirely, so nothing binds to UDP port 11000.
* The server still reports itself as registered, so startup proceeds normally.
* Intended for dedicated servers, where LAN discovery is useless and the bound port is just overhead — or a
  port conflict when running several instances on one host.
* Logs `[DisableLAN] Skipping LAN listener creation` once at startup so you can confirm it took effect.
* There is nothing to turn back on short of removing the mod: LAN discovery is off unconditionally.

## Installation

* Copy the `DisableLAN/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/DisableLAN/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
  * Not recommended outside a dedicated server — it disables LAN discovery of your game
* There are no configuration options for now

## Changelog

### 1.0.0

* Initial public release
* Only works against 7DtD v3.x

# Hades

Hades world, prefabs, and related config.

* Ships the "Hades S6" world along with its WalkerSim configuration (population density, per-biome wandering systems,
  and spawn behavior).
* Adds three custom POI prefabs: `stronghold_v_1_0`, `stronghold_v_2_0`, and `piddlys_hole`.
* Adds an RWG rule placing exactly one `stronghold_v_2_0` and one `piddlys_hole` in the forest biome. Note that
  `stronghold_v_1_0` ships as a prefab but is no longer placed by RWG.
* Raises the maximum count for every vanilla `trader_*` prefab to 8.
* Registers a "Hades S6" sandbox settings preset and makes it the default.
* XML and content only; there is no DLL.

## Installation

This is a world and content mod, so both the server and every connecting player need it — a client without the world's
prefabs cannot render the POIs, and a client without the world files cannot join a server running it.

* Copy the `Hades/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/Hades/ModInfo.xml`, otherwise the mod won't
  be loaded
* Install on the server *and* on every client
* Dedicated servers:
  * Set `GameWorld` to `Hades S6` in `serverconfig.xml`
  * Select the `Hades S6` sandbox preset (it is registered as the default)
  * EAC-friendly — no code, XML and content only
* All other deployments:
  * Deploy to host (in single-player this is your game) and to every client
  * Select the `Hades S6` world and preset when creating the game
* Note that only `Worlds/Hades S6/WalkerSim.xml` is kept under version control in this repo; the generated world files
  (heightmaps, prefab placements, and so on) live alongside it in the deployed mod folder. The build deliberately does
  not clean the output directory, so a build will not delete them
* Beyond the WalkerSim settings and the sandbox preset, there are no configuration options

## Changelog

### 6.0.0

* Stronghold 2.0 polish pass on the custom POIs
* `stronghold_v_1_0` removed from RWG placement
* Prefabs, world config, and sandbox preset brought under version control
* Build now only copies files that are newer, so locally-edited prefabs are not overwritten

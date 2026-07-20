# StrongUtils

A collection of modding and administration tools.

* **Server console commands** — `gracefulshutdown`/`gshutdown` (countdown announced in global chat, then shuts
  down), `denyall` (stop authorizing new connections), `reload` (reload configuration and push the updated XML
  to clients), `resetpois` (reset every instance of a named POI so it respawns with fresh loot and zombies),
  `teleportplayertoprefab`, `teleportplayertobed`, `ongamestartdone` (queue a command to run at game start) and
  `fasttravel`.
* **StrongZones** — rectangular world zones built from prefab properties, trader areas, POI difficulty tiers and
  a hot-reloadable `strong_zones.xml`. Zones can apply a buff, block hostile spawns (and punish intruding
  enemies), forbid land claims, or mark their chunks as never-resetting.
* **Loot and container tweaks** — loot containers respawn on their timer without ever needing to be opened; the
  `buff_no_loot` marker buff suppresses an entity's death drops; player-owned vending machines no longer inherit
  the trader reset interval.
* **Anti-grief and auditing** — every bulk block edit is logged, and a non-admin performing one is banned
  automatically; damage packets claiming to come from the wrong entity are flagged; a rolling per-player damage
  history is dumped when a player dies.
* **Reusable infrastructure for other mods** — `ConfigManager` (XML config files with defaults and optional
  `FileSystemWatcher` hot-reload), `KeyValueStore` (XML-backed persistence), `Chat` (whispers and global
  messages with BBCode), `StrongAudit`, and `ServerLifecycle` hooks.
* Also included: starting items granted per game mode from entity-class XML, and a finalizer that turns a
  corrupt sign/canvas into a warning naming the block and POI instead of a load failure.
* Some features are dormant in the current build — the fast-travel donation tiers and the wasteland spawn
  scaler are both commented out.

## Installation

* Copy the `StrongUtils/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongUtils/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Server-side only for the mechanics — the buffs in `Config/buffs.xml` are built entirely from stock game
    assets and are synced to clients
  * EAC-friendly
  * Clients without the mod installed will see raw localization keys instead of the buff names and
    descriptions, so installing it on clients too is recommended
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* Configuration lives in `<save game dir>/StrongMods/`:
  * `strong_zones.xml` — custom zone definitions; hot-reloaded when the file changes
  * `server_lifecycle_commands.xml` — commands queued by `ongamestartdone`, run once at game start
  * `Config/buffs.xml` and `Config/Localization.csv` ship the buffs the zone system relies on

## Changelog

### 0.2.0

* Console commands, StrongZones, loot/container tweaks, block-edit auditing, and the shared
  `ConfigManager` / `KeyValueStore` / `Chat` helpers
* Only works against 7DtD v3.x

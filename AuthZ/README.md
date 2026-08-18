# AuthZ

Server-side authorization checks on inbound network packets. AuthZ states what a well-behaved client never sends, then
detects and logs everything else.

* **Work in progress: detection and logging by default.** Every check runs, writes a `[AuthZ] violation ...` line, and
  lets the packet through. Dropping packets is opt-in per check, and no check ships in that mode.
* **19 invariants** across five families: sender binding, PvE ownership, connection lifecycle, field ranges, and
  editor traffic on a live server. `Docs/invariants.md` lists every one, what a violation means, and its
  false-positive risk.
* **Ownership covers players, vehicles, drones and turrets** through one shared rule, so "damaging another player" and
  "damaging another player's drone" are the same check.
* Violation lines name the **authenticated transport sender**, never anything the packet claimed, and are rate-limited
  so one misbehaving client cannot fill a disk.
* The PvE family is active only when the server's `PlayerKillingMode` is `NoKilling`. Other killing modes leave it
  inert.

## Installation

* Copy the `AuthZ/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/AuthZ/ModInfo.xml`, otherwise the mod won't
  be loaded
* Requires **StrongMods**
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled

EAC does not gate any check. It changes what a violation *means*: with EAC on, a packet no stock client can produce is
much stronger evidence than the same packet on an EAC-off server full of client mods.

## Configuration

Settings live at `<save game folder>/StrongMods/AuthZ.xml`, written with the shipped defaults on first start. They are
read once at mod load, so changing them means restarting the server. `Docs/invariants.md` documents every id.

Setting a check to `block` makes it drop packets. Confirm it against your own logs first.

## Changelog

### 0.1.0 (prototype, not released)

* Replaced the single hard-coded damage check with a general invariant engine: registry, per-check modes, rate-limited
  structured logging, and a Harmony multi-target patch sized to the checks actually enabled
* 19 invariants across five families
* Ownership now covers drones and turrets, not only vehicles
* Fixed two false positives in the damage check: vehicle passengers, and crossplay identity
* Per-check `off` / `log` / `block` modes in `AuthZ.xml`

### 0.0.1

* Initial pre-release
* Detected and logged unauthorized damage to players and vehicles; did not prevent it

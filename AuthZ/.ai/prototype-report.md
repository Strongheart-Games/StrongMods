# AuthZ invariant engine: overnight prototype

Built 2026-08-18, unattended. Companion to `netpackage-invariants.md`, which carries the research; this file records
what was built, what was verified, and what is left.

**Prototype.** Everything builds and passes the repo suite. Nothing has run against a live server.

## Handling

This is defensive work: it describes what a well-behaved client never sends so a server operator can detect the rest.
It is written as detection rules, not attack recipes, and it stays clear of the vanilla-engine findings that #74 routes
to private, pre-disclosure handling. Where a rule exists because a vanilla check is missing, the note says which check
is missing and stops there. Nothing here belongs in public issue text.

## What was measured

A source analyzer scanned all top-level `NetPackage*.cs` decompiled from
`packages/7dtd.assemblies.game/3.1.0.14/7DaysToDie_Data/Managed/Assembly-CSharp.dll` with `ilspycmd` 11.0.0.9375. It
read each override of `PackageDirection` and `AllowedBeforeAuth`, then searched `ProcessPackage` and `ShouldProcess`
for calls to the two sender-validation helpers. The disposable analyzer and decompile were not retained.

| Measure                                                              | Count |
|----------------------------------------------------------------------|-------|
| `NetPackage` subclasses                                               | 190   |
| Declared `ToClient` — the receive gate rejects them from a client     | 66    |
| **Server-reachable** — `ToServer`, or left at the `Both` default      | **124** |
| Server-reachable that call `ValidEntityIdForSender`                   | 25    |
| Server-reachable that call `ValidUserIdForSender`                     | 3     |

91 of the 124 are reachable because `Both` is the base-class default and they never overrode it, not because anyone
decided they should be. That is the gap the invariants fill.

## What was built

| Piece                            | Role                                                                        |
|----------------------------------|------------------------------------------------------------------------------|
| `Invariant.cs`, `InvariantMode.cs` | One checkable statement; separately, what an operator does when it fails.   |
| `InvariantEngine.cs`             | Registry, patch-target resolution, per-packet dispatch.                      |
| `NetPackageGuard.cs`             | The single Harmony multi-target patch.                                       |
| `Ownership.cs`                   | Who a world entity belongs to, across all four vanilla mechanisms.           |
| `ViolationLog.cs`                | Rate-limited structured logging with per-client tallies.                     |
| `Settings.cs`                    | Per-invariant modes, game-free so it can be exercised off-game.              |
| `Invariants/*.cs`                | 19 invariants in five families.                                              |

`PveEnforcer.cs` is gone; its rule is now `pve.damage`, with the two false positives below fixed.

### The seam

`ConnectionManager.ProcessPackages` is the single point every inbound packet passes, but it is a loop over a list it
fills itself — a prefix cannot see the packets and a transpiler over it would be brittle. The engine instead patches
`ProcessPackage` on each package type that has an **enabled** invariant, through Harmony's `TargetMethods()`. Four
enabled rules patch four methods, not 124. `NetPackage.Sender` is already set by then, because
`NetPackageManager.ParsePackage` assigns it before `read()`.

### Two false positives fixed

The shipped 0.0.1 damage check had both:

* **Vehicle passengers.** It allowed only the owner to damage an unoccupied vehicle, and checked attachment before
  that — but a passenger the owner let in is not necessarily on the allowed-user list. Attachment is now checked
  first, for any seat.
* **Crossplay identity.** It compared `GetOwner()` against `ClientInfo.PlatformId` only. `ClientInfo.InternalId` is
  `CrossplatformId ?? PlatformId`, so a crossplay player acting on their own vehicle read as a stranger. Both ids are
  compared now, plus `ILockable.IsUserAllowed`.

## Verified

* `dotnet build AuthZ/AuthZ.csproj -c Debug` — clean, no warnings.
* `dotnet test StrongMods.sln -c Debug` — 403 passed, 0 failed. The suite resolves `NetPackageGuard.TargetMethods()`
  against **both** `V3.1.0-b14` and `V3.0.1-b4`, so every one of the 19 packages named exists, and its
  `ProcessPackage` resolves, in both builds.
* 22 disposable harness checks over the game-free pieces: settings parsing and fallback, id uniqueness and shape,
  every invariant class registered with the engine, and the shipped settings template naming exactly the set the
  sources declare. The harness was not retained; #130 tracks graduation into `Tests/ModLogic`.

## Not verified

Nothing has run against a server. Specifically untested:

* That any invariant ever fires, on honest traffic or dishonest.
* The **false-positive rate on honest traffic**, which is the number that decides whether any rule may be promoted to
  `block`. This is the whole reason nothing ships in block mode.
* The per-packet cost of the prefix on a busy server.
* That the settings file round-trips through the save folder.
* `Ownership.Resolve` against real drones and turrets. It is read from the entity classes, not observed.

## Left undone

* **Family C (plausibility)** and **family E (rate)** are catalogued and not built. Both need thresholds that come
  from a real server's numbers.
* The disposable harness was not retained. `Tests/Fixtures/ModLogicHost.cs` executes mod logic headlessly against the
  game assemblies with a Unity stub — the proper home for these checks, and for `Ownership` tests that a
  source-scanning harness cannot do. #130 tracks that work.
* No console command to dump the current tallies. `ViolationLog.TotalFor` exists and nothing calls it.

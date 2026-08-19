# NetPackage invariants the server can check

Research note for the AuthZ invariant engine. Written 2026-08-18 against game tree `V3.1.0-b14`
(`packages/7dtd.assemblies.game/3.1.0.14`), decompiled with `ilspycmd` 11.0.0.9375.

## Handling note

This work is **defensive**: it describes what a well-behaved client never sends, so a server operator can detect and
log the rest. It is deliberately written as detection rules rather than as attack recipes, and it stays clear of the
vanilla-engine findings that #74 routes to private, pre-disclosure handling. Where a rule exists because a vanilla
check is missing, the note says which check is missing and stops there.

## The shape of the problem

`NetPackageManager.ParsePackage` sets `NetPackage.Sender` from the transport before `read()` runs, so **every**
server-side handler already knows, for free, which authenticated client sent the packet. The question each invariant
asks is the same one: *does what this packet claims agree with what the server already knows about its sender?*

Vanilla answers that question in two helpers on the base class:

| Helper                      | What it proves                                                    |
|-----------------------------|-------------------------------------------------------------------|
| `ValidEntityIdForSender`    | The named entity is the sender's own player, or the vehicle it rides |
| `ValidUserIdForSender`      | The named platform user is the sender's own                        |

Both log a warning and return false. They are correct. They are just not called very often.

## Measured surface

A source analyzer scanned all top-level `NetPackage*.cs` decompiled from the named package tree. It read each override
of `PackageDirection` and `AllowedBeforeAuth`, then searched `ProcessPackage` and `ShouldProcess` for calls to the two
sender-validation helpers. The disposable analyzer and decompile were not retained.

| Measure                                                             | Count |
|---------------------------------------------------------------------|-------|
| `NetPackage` subclasses                                              | 190   |
| Declared `ToClient` (the receive gate rejects them from a client)    | 66    |
| **Server-reachable** — declared `ToServer` or left at the `Both` default | **124** |
| Server-reachable that call `ValidEntityIdForSender`                  | 25    |
| Server-reachable that call `ValidUserIdForSender`                    | 3     |
| Allowed before authentication                                        | 10    |

`Both` is the base-class default, and `ConnectionManager.ProcessPackages` only rejects a package whose declared
direction *equals* the disallowed direction. So a package that is conceptually server-to-client but never overrode
`PackageDirection` is still accepted from a client. 91 of the 124 are in that position — reachable by default rather
than by decision.

That is the gap the invariants fill. **This is not a list of vanilla bugs.** Most of these packages are harmless, or
are guarded further down the call path. The list is a list of *places where the server has enough information to check
and does not*, which is exactly where a detection rule pays for itself.

## Invariant families

Seven families, ordered by how much they are worth relative to what they cost.

### A — Sender binding

*The entity a packet names is the sender's own, or one the sender legitimately drives.*

The broadest family and the cheapest to check: it is one comparison against `Sender.entityId`. Vanilla already has the
helper; these are the packages that do not call it.

| Package                              | Claim a client makes                              | Missing check                        |
|--------------------------------------|---------------------------------------------------|--------------------------------------|
| `NetPackageEntityAddExpServer`       | Entity X gains N experience                        | X is the sender                      |
| `NetPackageEntitySetSkillLevelServer`| Entity X's skill S is now level L                  | X is the sender; L within the skill's max |
| `NetPackageEntityAddScoreServer`     | Entity X's kill and score counters change          | X is the sender                      |
| `NetPackageAddRemoveBuff`            | Entity X gains or loses buff B for D seconds       | X is the sender or a valid target of B |
| `NetPackageEntityAddVelocity`        | Entity X is shoved by vector V                     | X is the sender                      |
| `NetPackageItemActionEffects`        | Entity X fired item slot I                         | X is the sender                      |
| `NetPackageEmitSmell`                | Entity X emits smell S                             | X is the sender                      |
| `NetPackagePlayerLaserSight`         | Entity X's laser is on, at position P              | X is the sender                      |
| `NetPackageSetAttackTarget`          | Entity X now attacks entity T                      | X is the sender's own AI entity      |
| `NetPackageChat`                     | This message came from entity X, as sender kind K  | X is the sender; K is not `Server`   |

`NetPackageChat` deserves its own line. `GameManager.ChatMessageServer` re-derives the display name from the
*client-supplied* `senderEntityId` before broadcasting, while the server log line records the real platform id. So the
log is trustworthy and the chat window is not. `msgSender` is client-supplied too and `EMessageSender.Server` is a
legal value for it.

### B — Ownership, the PvE rule

*Under a no-killing rule, nothing a player sends may harm another player or anything that player owns.*

This is the family the existing `PveEnforcer` prototypes for damage, generalised in two directions: to more packages,
and past vehicles to every owned entity.

Ownership resolves through three vanilla mechanisms, and a check that uses all three covers every player-owned entity
in the game:

| Mechanism                      | Where                                        | Covers                       |
|--------------------------------|----------------------------------------------|------------------------------|
| `ILockable.GetOwner()` / `IsUserAllowed()` / `GetUsers()` | `EntityVehicle`, `EntityDrone` | vehicles, drones             |
| `Entity.belongsPlayerId`       | base `Entity`, set by `EntityFactory`        | turrets, drones, anything spawned for a player |
| `EntityTurret.OwnerID`         | `EntityTurret`                               | turrets, when unloaded and re-bound |
| attachment                     | `EntityVehicle.GetAttached(slot)`            | anyone riding, driver or passenger |

Packages in scope: `NetPackageDamageEntity` (covered today), plus `NetPackageAddRemoveBuff`,
`NetPackageEntityAddVelocity`, `NetPackageExplosionInitiate`, `NetPackageSetAttackTarget`, and
`NetPackageEntitySetPartActive`.

The important correction to the current prototype: it treats an unoccupied vehicle as damageable by its owner only,
via `GetOwner().Equals(client.PlatformId)`. `ClientInfo.InternalId` is `CrossplatformId ?? PlatformId`, and
`ILockable.IsUserAllowed` covers the lock's allowed-user list. A crossplay player, or a passenger a vehicle owner
explicitly allowed, is a **false positive** today.

### C — Plausibility

*A client's claim about the world agrees with what the server already believes.*

More expensive, and the only family with a real false-positive rate, because latency and lag compensation make small
disagreements normal.

* **Reach.** `NetPackageDamageEntity` names a target the server places far outside any weapon's range of the sender.
* **Explosion origin.** `NetPackageExplosionInitiate` carries a fully client-authored `ExplosionData` blob *and* its
  world position, and the server passes both to `ExplosionServer`. A position far from the sender is checkable; the
  blob's contents are checkable against the explosive item the packet names.
* **Interaction distance.** Block, container and tile-entity packets naming a position the sender cannot reach.

Thresholds have to be generous, and every rule here should ship in log-only mode until a real server has produced a
week of numbers. That is the honest reason this family is third and not first.

### D — Lifecycle phase

*A packet arrives at a point in the connection where a real client would never send it.*

For V3.1.0-b14, the bootstrap order was established by finding every construction/send site for the connection
packages in `Assembly-CSharp.dll`, then following the enclosing control flow and receiver callback. Rules follow
directly: a second `NetPackageRequestToEnterGame` on a connection that already entered, a
`NetPackageRequestToSpawnPlayer` from a client already bound to an entity, a `NetPackagePlayerLogin` after
`loginDone`. These are cheap — one bit of per-connection state each — and they have essentially no false-positive rate,
because a real client's send sites are all one-shot.

### E — Rate

*A packet arrives far more often than any real client sends it.*

The generic backstop, and the only family that catches a packet type nobody wrote a rule for. Per client, per package
type, a rolling count with a per-type ceiling. Legitimate rates vary hugely — `NetPackageEntityRelPosAndRot` is
per-frame, `NetPackageRequestToSpawnPlayer` is once — so the ceiling has to be per-type and the shipped defaults have
to come from observation, not from guessing.

### F — Domain

*A numeric or string field is outside the range the game itself can produce.*

Cheapest of all, and entirely local — no world state needed:

* a skill level above that skill's configured maximum;
* an experience delta larger than any single vanilla award;
* a buff name that is not in `BuffManager`;
* a negative or absurd count where the game only writes small positives;
* an entity or slot index outside its container.

### G — Authority

*An operation restricted to admins came from a client the server does not consider an admin.*

`NetPackageConsoleCmdServer` is the obvious member. Several editor-facing packages
(`NetPackageEditorAddVolumeFromClient`, `NetPackageEditorUpdateVolume`) are also server-reachable and are worth
asserting on for a production server that never expects world-editor traffic at all.

## What to build first

The families are not equally worth it. Ranked by (detection value) / (cost + false-positive risk):

1. **A and F** — pure local comparisons, no world state, no tuning, essentially zero false positives.
2. **D** — one bit of state per connection, no tuning.
3. **B** — needs the ownership resolver, which is the one genuinely new piece of logic, and fixes a false positive
   already in the shipped prototype.
4. **G** — small and obvious, but the packages involved are rare.
5. **E** — high value as a backstop, but the defaults are worthless until observed.
6. **C** — highest false-positive risk; log-only until a real server produces numbers.

## The seam

`ConnectionManager.ProcessPackages` is the single point every inbound package passes through, but it is a loop over a
list it fills itself, so a prefix cannot see the packages and a transpiler would be fragile.

The engine instead uses a Harmony **multi-target patch**: `TargetMethods()` returns the declared `ProcessPackage` of
every package type that has a registered invariant, and one shared prefix dispatches. This keeps the patch surface
proportional to the rules actually enabled — a server running four invariants patches four methods, not 124 — and it
gets `Sender` for free, because parsing sets it before the handler runs.

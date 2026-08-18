# The invariants AuthZ checks

An invariant is a statement about an inbound packet that a well-behaved client never violates. Each one has a mode set
in `<save game folder>/StrongMods/AuthZ.xml`:

| Mode    | What happens                                                                   |
|---------|--------------------------------------------------------------------------------|
| `off`   | Not checked. The engine does not even patch the package, so it costs nothing.   |
| `log`   | Checked; a violation line is written; the packet goes through. **The default.** |
| `block` | Checked; a violation line is written; the packet is dropped.                    |

**Nothing blocks unless you say so.** Read your own logs first. Every rule below is stated with its false-positive
risk, and the ones with real risk say so plainly.

## Reading a violation line

```
WRN [AuthZ] violation pve.buff | client=Bob/Steam_76561198... | package=NetPackageAddRemoveBuff
    | expected=under NoKilling, a client does not buff or debuff another player or an entity they own
    | observed=tried to buff or debuff EntityDrone owned by Steam_76561198... (entity 4210)
```

`client` is the **authenticated transport sender**, not anything the packet claimed — that is the whole point. Lines
are capped at 3 per client per invariant per minute; the rest of the window is summarised in one line when it closes.

## Family A — the entity a packet names is the sender's own

One integer comparison against the connection's own entity id. No world lookup, no threshold, no false positives: a
real client only ever names itself in these packets. Vanilla has a helper for exactly this
(`NetPackage.ValidEntityIdForSender`); these are the packages that do not call it.

| Id                   | Package                                | What a violation means                                    |
|----------------------|----------------------------------------|-----------------------------------------------------------|
| `exp.self-only`      | `NetPackageEntityAddExpServer`         | Experience awarded to a player other than the sender      |
| `skill.self-only`    | `NetPackageEntitySetSkillLevelServer`  | Another player's skill level set                          |
| `score.self-only`    | `NetPackageEntityAddScoreServer`       | Another player's kill and score counters moved            |
| `itemaction.self-only` | `NetPackageItemActionEffects`        | An item action attributed to another player               |
| `smell.self-only`    | `NetPackageEmitSmell`                  | A smell emitted as another player — smell attracts zombies |
| `lasersight.self-only` | `NetPackagePlayerLaserSight`         | Another player's laser sight driven                       |
| `chat.identity`      | `NetPackageChat`                       | A message wearing another player's name, or the server's  |

`chat.identity` is worth understanding. `GameManager.ChatMessageServer` re-derives the display name from the
*client-supplied* sender entity id before broadcasting, so a wrong id puts somebody else's name on the message. The
server's own log line records the real platform id, which is why the server log stays trustworthy when the chat window
does not. `msgSender` is client-supplied as well, and `Server` is a legal value for it.

**False-positive risk: none known.** These are the first rules to promote to `block`.

## Family B — under a no-killing rule, no harm to another player or what they own

Only active when the server's `PlayerKillingMode` is `NoKilling`. In any other killing mode the whole family is inert,
because player harm is allowed by definition.

| Id                 | Package                        | What a violation means                                   |
|--------------------|--------------------------------|----------------------------------------------------------|
| `pve.damage`       | `NetPackageDamageEntity`       | Damage aimed at another player or an entity they own     |
| `pve.buff`         | `NetPackageAddRemoveBuff`      | A buff or debuff applied to another player's entity      |
| `pve.velocity`     | `NetPackageEntityAddVelocity`  | Another player's entity shoved                           |
| `pve.attack-target`| `NetPackageSetAttackTarget`    | AI commanded by, or pointed at, somebody who is not the sender |

"Owns" resolves through one shared rule that covers players, **vehicles, drones and turrets**:

| Mechanism                                                   | Covers                                     |
|-------------------------------------------------------------|--------------------------------------------|
| `ILockable.GetOwner()` / `IsUserAllowed()`                   | vehicles, drones                           |
| `Entity.belongsPlayerId`                                     | turrets, drones, anything spawned for a player |
| `EntityTurret.OwnerID`                                       | turrets re-bound after a reload            |
| attachment (`EntityVehicle.GetAttached`)                     | anyone riding, driver or passenger         |

Two deliberate allowances, both of which were false positives in the earlier prototype:

* **Passengers.** Anyone in a seat may act on the vehicle they are in, checked before the lock, because a passenger
  the owner let in is not necessarily on the allowed-user list.
* **Crossplay identity.** Ownership compares `PlatformId` **and** `CrossplatformId`. Comparing only `PlatformId`
  misreads a crossplay player as a stranger acting on their own vehicle.

**False-positive risk: low**, and concentrated in unowned-entity edge cases — an entity whose owner has not loaded, or
a vehicle whose owner record is missing. Both resolve to "allowed", so the rule stays quiet rather than guessing.

## Family D — a one-shot bootstrap packet arrives once

One bit of state per connection. These packages have a single send site each in the vanilla client, reached once.

| Id                            | Package                            | What a violation means                              |
|-------------------------------|------------------------------------|-----------------------------------------------------|
| `lifecycle.enter-game-once`   | `NetPackageRequestToEnterGame`     | The world-bootstrap coroutine asked for again       |
| `lifecycle.spawn-once`        | `NetPackageRequestToSpawnPlayer`   | A second spawn request on one connection            |
| `lifecycle.spawn-when-unbound`| `NetPackageRequestToSpawnPlayer`   | A spawn request from a connection already holding an entity |

Worth having beyond identity: each of these makes the server do real work — the enter-game handler resends
localization, every XML config, world info, decorations and spawn points — so a repeat is load the server did not have
to take.

The last two overlap on purpose. `spawn-once` counts; `spawn-when-unbound` asks the stronger question and survives a
game update that legitimately re-sends the request.

**False-positive risk: low.** A client reconnecting gets clean state on disconnect.

## Family F — a field is outside the range the game can produce

Local checks. No world lookup.

| Id               | Package                               | What a violation means                                |
|------------------|---------------------------------------|--------------------------------------------------------|
| `skill.range`    | `NetPackageEntitySetSkillLevelServer` | A skill level past that skill's own maximum, or an unknown skill name |
| `buff.known-name`| `NetPackageAddRemoveBuff`             | A buff name the loaded configuration does not define   |
| `exp.delta`      | `NetPackageEntityAddExpServer`        | One award above `thresholds/@max_experience_per_packet` |

`skill.range` catches what `skill.self-only` cannot: a client raising its *own* skill past the maximum. An unknown
skill name is flagged too, because the vanilla handler dereferences the lookup result without a null check.

`buff.known-name` is graded **suspicious**, not violation: an unknown buff silently does nothing, so it signals
somebody enumerating the surface rather than harm in progress. It fires on any mod that adds buffs the server does not
have loaded — set it to `off` if your server runs that way.

`exp.delta` ships generous (100000) because the game has no single constant to compare against: quest rewards, kill
experience and crafting experience all flow through one packet. **Find your own real maximum in the log before
tightening it.**

## Family G — world-editor traffic on a live server

| Id                     | Package                                | What a violation means               |
|------------------------|----------------------------------------|--------------------------------------|
| `editor.volume-add`    | `NetPackageEditorAddVolumeFromClient`  | Editor traffic reached a live server |
| `editor.volume-update` | `NetPackageEditorUpdateVolume`         | Editor traffic reached a live server |

These fire on **any** occurrence, with no threshold, because a production server never expects one.

**Set both to `off` if you edit your world live.**

## Families not yet implemented

Two of the seven families in `.ai/netpackage-invariants.md` are catalogued but not built:

* **C, plausibility** — reach checks on damage, explosion origin against the sender's position, interaction distance.
  The highest false-positive risk of any family, because latency and lag compensation make small disagreements
  normal. It needs thresholds that come from a real server's numbers, not from guessing.
* **E, rate** — per client, per package type, a rolling count against a per-type ceiling. The only family that would
  catch a package nobody wrote a rule for. Same problem: the defaults are worthless until observed.

Both want the same input, which is a week of logs from a real server.

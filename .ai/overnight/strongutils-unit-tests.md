# Overnight — StrongUtils infrastructure unit tests (#50 gap U1)

**Date:** 2026-08-17 (unattended session) · **Scope of every claim:** V3.1.0 b14, game unit, this machine.
**State:** all files untracked/uncommitted; suite green at **392 passed** (baseline was 269).

## What was built

| File | What it is |
|------|------------|
| `Tests/Fixtures/ModLogicHost.cs` | New fixture: a host that **executes** a mod's own logic with no game running. |
| `Tests/ModLogic/ModLogicCollection.cs` | xunit collection that serializes mod-logic tests (they share process-wide statics). |
| `Tests/ModLogic/ConfigManagerTests.cs` | 19 tests — StrongUtils `ConfigManager`. |
| `Tests/ModLogic/PlayerDamageTests.cs` | 11 tests — StrongUtils `PlayerDamage` ring buffer. |
| `Tests/ModLogic/ServerLifecycleCommandsTests.cs` | 10 tests — StrongUtils `ServerLifecycleCommands` queue. |
| `Tests/ModLogic/XmlKeyValueStoreTests.cs` | 31 tests — StrongUtils `XmlKeyValueStore`. |
| `Tests/ModLogic/StrongZoneTests.cs` | 52 tests — `StrongZone` geometry/tags/XML and the `StrongZones` zone diff. |

Verified with `dotnet build StrongMods.sln -c Debug` (0 warnings, 0 errors) and
`dotnet test StrongMods.sln -c Debug`.

## The finding that shaped the design: the stub does not scale, so there are two universes

The seed doc's gap **U1** assumed "expanded game-type stubs". Measured, that assumption does not hold:
`Tests/Stubs/UnityStubs.cs` declares 13 CoreModule types, and **Assembly-CSharp references 225 CoreModule
types the stub does not have** (measured with Mono.Cecil over the declared tree's `Assembly-CSharp.dll`:
count of distinct `TypeReference`s scoped to `UnityEngine.CoreModule` and absent from the stub). `EntityPlayer`
will not even load against the stub — it dies on `UnityEngine.Vector3`. Mirroring CoreModule is not a
two-lines-and-rerun job, and struct layouts would have to be right for real execution.

So `ModLogicHost` offers **two universes**, and the choice is forced by which of two things the code needs:

| Universe | `UnityEngine.CoreModule` | `Log` works | Entity/Unity-shaped game types load | Use for |
|----------|--------------------------|-------------|--------------------------------------|---------|
| stubbed (default) | clean-room stub | **yes** | no | logic that logs: `ConfigManager`, `ServerLifecycleCommands` |
| real (`stubUnity: false`) | the game's own | **no** (initializer throws headlessly) | **yes** | logic over entities that does not log on the path under test: `PlayerDamage` |

This is a real, permanent trade, not a temporary gap — it is the same trade `PatcherHost` vs `GameEngineHost`
already makes, now available for executing *mod* code. It is worth recording in the #50 inventory: **U1 splits
into U1a (stubbed-universe logic, cheap, done here) and U1b (entity logic, works today via the real-Unity
universe as long as the path under test does not log).** Only logic that needs *both* Unity types and logging
is genuinely blocked, and that is a much smaller set than the seed implies.

Planting game objects works without any seam: `RuntimeHelpers.GetUninitializedObject` gives an `EntityPlayer`
whose `entityId` a test can set, and the server/client gate is reachable by planting
`ConnectionManager.Instance` with a `ProtocolManager` whose `CurrentMode` the test picks (read from IL:
`ConnectionManager.get_IsClient` is `protocolManager.IsClient`, which is `CurrentMode == NetworkType.Client`).
**No `InternalsVisibleTo` and no per-mod refactor was needed for any of the three subjects.**

## What is now asserted

**`ConfigManager`** — directory creation; double-`Init` rejection; default contents written only when the file
is absent; existing file left alone; intermediate directories created; absolute-path and null-contents
rejection; **registration-key normalization** (`Sub/Case.xml`, `sub\case.xml`, `SUB/CASE.XML` are one file);
write/read round-trip; unregistered-file rejection on write and append; append preserves the existing root;
remove deletes the deep-equal child, removes exactly one of two duplicates, and is a no-op when absent;
`Dispose` releases the singleton so `Init` can run again.

**`PlayerDamage`** — empty history for an unknown player; null player ignored; oldest-first ordering; the
buffer holds exactly 20; **past 20 the oldest is dropped** (asserted as the exact window 6..25 after 25
events); per-player isolation; two objects with the same `entityId` share one history; clear drops only that
player; clear of an unknown player and of null are no-ops; **nothing is recorded when `IsClient`**; the
recorded event carries source/strength/crit/impulse unchanged.

**`ServerLifecycleCommands`** — the queue file is created at `Init`; an added command lands as
`<on_game_start_done command="…"/>`; insertion order is preserved; empty queue loads nothing; malformed
entries (no `command`, empty `command`, wrong attribute) are skipped; another element name is not loaded;
**running the queue drains it** (the one-shot property that outlives a restart); the drain leaves other
element names alone; duplicate commands are all drained; running an empty queue is a no-op.

**`XmlKeyValueStore`** — empty start from a missing file; null path rejected; absent key returns the default
and null raw/tag; all six supported types round-trip and record their type tag; an unparsable value falls back
to the default; overwrite replaces rather than duplicates; blank keys rejected on `Set` and `TestAndSet`; keys
are case-sensitive; remove and clear; **test-and-set** applies on a match, declines on a value mismatch, on an
absent key, and on a *type* mismatch (an int 1 is not the string "1", though both are raw "1" on disk);
values survive a reopen; the on-disk shape is one `<Entry key type value/>` per key under `<Store>`; removing
the last key leaves an empty `<Store>`; `Reload` replaces memory with disk; a blank-key entry is dropped on
load; `VarChanged` raises Created / Updated / Deleted with the right payload, and a declined test-and-set
raises nothing.

**`StrongZone` and the zone diff** — corners normalize whichever order they arrive in, negatives included;
`Center` is the midpoint (odd widths land on a half); `Radius` is the circumscribing one (center to corner,
verified against a 6×8 box giving 5); a zero-size zone is a single point; **containment is inclusive on all
four edges** and ignores height; a tagless zone has every flag off; each of the five known tags raises exactly
its own flag; tag matching is case-sensitive; an unknown tag raises nothing and is still kept; `FromXml`
parses corners and a trimmed comma-separated `<tags>` child, treats a missing/empty/whitespace `<tags>` as
none, and rejects each of eight malformed shapes with a message naming the offending attribute; `ToXml` writes
the *normalized* corners and omits `<tags>` when there are none; name, corners and tags survive a
write-then-read.
The **diff** (`StrongZones.FindZoneChanges`, private static, reached by reflection): a newly-containing zone
is added; a held zone still containing is neither; a zone no longer offered is removed; **a candidate that
does not contain the position is not added**; **a held zone still offered but no longer containing the
position IS removed** (this is what makes walking out of a zone register as a leave); overlapping zones are
each reported; one zone can be added while another is removed; two empty sides report nothing.

## Deliberately not asserted, and why

- **A command actually reaching the console.** `SdtdConsole.Instance` needs a running server — gap R1. The
  drain tests still hold because the mod swallows each command's failure in its own `try`/`catch`, which is
  itself the behavior under test (the queue drains whether or not the console was there).
- **`PlayerDamage.HandleEntityKilled` / `HandlePlayerDisconnected` / `ValidateDamageEntityPackage`.** All three
  log, and all three need `GameManager.Instance.World`. Real-Unity universe cannot log; stubbed universe
  cannot load the entity types. This is the genuine U1b∩logging blocker named above.

## Defects and rough edges found while reading the source

Read from source, not run. None is a regression; all three are worth an issue if the owner agrees.

1. **`ConfigManager.ReadConfigFile` does not reject a rooted path** (`StrongUtils/ConfigManager.cs:83`).
   `RegisterConfigFile`, `WriteConfigFile`, `AppendConfig` and `RemoveConfig` all guard with
   `Path.IsPathRooted`; `ReadConfigFile` does not, and `Path.Combine(dir, "C:\\x.xml")` returns the rooted
   path — so a read escapes the config directory. It also skips the registration check the three writers
   apply. Low severity today (no caller passes a rooted name), but it is an inconsistent guard on the one
   method that is not guarded.
2. **`ConfigManager.Dispose` is not `IDisposable`** (`StrongUtils/ConfigManager.cs:167`). It has the shape and
   the name but not the interface, so `using` cannot be used on it and no analyzer will notice a leak of the
   `FileSystemWatcher`s.
3. **`XmlKeyValueStore` persists numbers in the current culture** — a **measured** silent-corruption bug
   with its own report: [kvstore-culture-bug.md](kvstore-culture-bug.md). `3.5f` written under `de-DE` reads
   back as `35` under `en-US`. Dormant today (the store has no live callers), and cheapest to fix while it
   still is. That report also lists the store's unguarded `Load()` and the `Clear()`/`Remove()` inconsistency
   in what `VarChanged` carries.
4. **A `buff`-tagged zone declared in `strong_zones.xml` grants no buff.** `StrongZone.FromXml`
   (`StrongUtils/StrongZones.cs:507`) never reads or passes a buff name — the constructor's `buffName`
   parameter is only ever filled by the *prefab* path (`TryGeneratePrefabZones`, line 375). So an XML zone
   tagged `buff` gets `Buff == true` and `BuffName == null`, and `BuffManager.OnPlayerEntered` (line 588)
   returns early on exactly that combination. The zone is silently inert. `ToXml` (line 559) has the mirror
   hole: it writes no buff name, so a round trip loses one that was set. Both are one attribute each to fix,
   and this is the highest-value of the source-read findings.
5. **`StrongZone.FromXml` reports a coordinate typo differently from every other malformation.** Missing or
   wrongly-shaped attributes throw `ArgumentException` with a message naming the attribute; a non-numeric
   coordinate falls through to a bare `int.Parse` and throws `FormatException` with the framework's generic
   text. Deliberately not pinned by a test, so a fix does not have to update one.
6. **`PlayerDamage.RecordDamage` mutates inside a `ConcurrentDictionary.AddOrUpdate` update factory**
   (`StrongUtils/PlayerDamage.cs:31`). The factory has a side effect (it enqueues), and `AddOrUpdate` may
   invoke the factory more than once under contention. In practice it cannot double-enqueue here — every
   writer returns the *same* queue reference, so the internal `TryUpdate` comparison never fails — so this is
   a hazard, **not** a live bug. Noted so a future edit that starts returning a new queue does not silently
   introduce one.

## Reusable for the rest of #50

`ModLogicHost` is mod-agnostic: `ModLogicHost.For("<ModName>")` loads any code mod's DLL. The next subjects it
should reach with no new machinery are `StrongZones` geometry/parse/diff, `ChatCommandHelper`,
`CustomChatCommands.Init` parsing, and the `DynamicFeralSense` / `DynamicLandClaimCount` multiplier tables —
each is either stubbed-universe (logs, no entities) or real-Unity (entities, no logging on the path).

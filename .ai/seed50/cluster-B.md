# Cluster B — Harmony behavior code mods (seed for #50)

Scope: V3.1.0 (b14). Paper analysis — read each mod's `.cs` sources and the existing `Tests/` IL harness; nothing booted.
Tags: [T1] server-side/telnet · [T2] client→server protocol/ownership · [IL] patch-target/static · [UNIT] off-game
logic · [LINT] XML/CSV structure · [IN-GAME] needs #49 runner or a real client.

**Standing fact that shapes every [IL] row below.** `Tests/SmokeTests.cs` + `Tests/TargetResolver.cs` already resolve
*every* `[HarmonyPatch]` target in every mod against every declared tree, in CI, with near-miss diagnostics. So "the
outer patch target resolves" is **EXISTING** for all seven mods — I mark it once per mod and don't invent a gap for it.
What the resolver does **not** do: it never applies a transpiler or inspects the target method's *body*. It proves
`PlayerStealth.TickServer` exists; it says nothing about whether the `EAIManager.CalcSenseScale()` call the transpiler
matches *inside* it still exists. That blind spot is this cluster's headline gap (two mods here are transpilers).

Two current-code facts also shape which behavior tests are worth writing now (read from source, not README):
- **AuthZ enforcement is a no-op stub** — the prefix logs a PvE violation and still `return true` (TODO to reject).
- **StrongBoxes sort is a no-op stub** — `SortBoxes.Transfer()` has an empty body; closing a "sort" box moves nothing.

---

### AuthZ  (shape: code; side: server)
Prefix on `NetPackageDamageEntity.ProcessPackage`: server-side only (`return true` if `IsClient`). When
`PlayerKillingMode == NoKilling` and the damage is unauthorized, it **logs a warning and still processes it** (rejection
is a TODO). Authorization: player target must be the sender's own `entityId`; vehicle target is allowed if the sender is
an attached occupant, else (no occupants) if `vehicle.GetOwner().Equals(client.PlatformId)`.
- [IL] `NetPackageDamageEntity.ProcessPackage` + `.entityId`/`.Sender`, `EntityVehicle.GetAttached/GetAttachMaxCount/GetOwner`, `EnumPlayerKillingMode.NoKilling` resolve. Tool: EXISTING (smoke suite; field/enum refs compile-checked).
- [UNIT] Authorization logic: ownerless vehicle whose `GetOwner()` != sender `PlatformId` and no attached occupant → unauthorized; sender-as-occupant → authorized. Tool: GAP — `IsAuthorizedEntityDamage` is private and reads `GameManager.Instance.World`; needs a seam + a stub `EntityVehicle`/`EntityPlayer` so the branch table is testable off-game.
- [T2] Mode gate: `PlayerKillingMode != NoKilling` → prefix is pass-through (no auth check runs). Tool: GAP — inject a `NetPackageDamageEntity` carrying a real authenticated `Sender` and assert the packet is processed unchanged. T2 supplies the authenticated `Sender`/`PlatformId`, so the **vehicle-owner** branch is reachable; but…
- [IN-GAME] The player-target branch (`targetEntityId == client.entityId`) and the occupant branch need a **spawned player entity with an `entityId`** — beyond T2 (no spawn). Why not headless: requires a real client-backed `EntityPlayer` in the world.
Notable gaps: (a) a seam to unit-test the authorization decision without a live world; (b) an authenticated-sender damage-packet injection harness (T2 extension) that can also assert accept-vs-reject; (c) spawned-player identity for the self/occupant branches.

### AutoCloseDoors  (shape: code+xml; side: server)
Postfix on `TEFeatureDoor.UpdateTick`: on the server only (`!world.IsRemote()`), closes a door that is open, non-child,
carries the `auto_close` tag, sits `IsWithinTraderArea`, and has **no** player alive within 10 m. `Config/blocks.xml`
applies the tag.
- [IL] `TEFeatureDoor.UpdateTick(World)` + `isOpen/SetOpen/ToWorldPos`, `blockValue.ischild`, `World.IsPlayerAliveAndNear/IsWithinTraderArea` resolve. Tool: EXISTING (smoke).
- [T1] An open `auto_close` door in a trader area, no player within 10 m, one tick → door becomes closed. Tool: GAP — a telnet-scriptable way to place/open a `TEFeatureDoor` in a trader area and read back `isOpen` (world-scan-safe rung + tile-state read).
- [IN-GAME] Proximity guard: identical setup but a player standing within 10 m → door stays open. Why not headless: needs an entity positioned at a controlled world spot near the door (T2 can't spawn; T1 has no player body).
- [LINT] `blocks.xml` well-formed. Tool: EXISTING (build lint). "Tag actually lands on the intended trader-door blocks after patching" is a patch-eval/[T1] question, not well-formedness.
Notable gaps: tile-state place/open/read primitive (T1); positioned-player proximity (IN-GAME).

### StrongLocks  (shape: code; side: server) — the clean Tier-1 exemplar
Two patches. `GameManager.ChangeBlocks`: prefix records each newly-placed `BlockCompositeTileEntity` carrying a
`TEFeatureLockable` (skipping changes equal to the current block); postfix sets those tile entities locked if not
already. `World.SpawnEntityInWorld`: postfix locks any `EntityVehicle` and syncs security data.
- [IL] `GameManager.ChangeBlocks`, `World.SpawnEntityInWorld` + `TEFeatureLockable.IsLocked/SetLocked`, `EntityVehicle.SetLocked/SendSyncData`, `cSyncInteractAndSecurity` resolve. Tool: EXISTING (smoke).
- [T1] Place a lockable block (crate/door) via `ChangeBlocks` → its `TEFeatureLockable.IsLocked()` is true. Tool: GAP — the canonical place-block-and-read-tile-state primitive over telnet. (This is the exemplar test the cluster keeps needing.)
- [T1] Spawn a vehicle via `SpawnEntityInWorld` (`spawnentity` console cmd) → `EntityVehicle.IsLocked()` is true. Tool: GAP — spawn-entity-and-read-entity-flag primitive.
- [T1] No-op guard: re-applying the identical `BlockValue` (change equals current block) → the prefix skips it, so an already-unlocked-by-design block isn't force-locked. Tool: same place/read primitive; guards the `Equals(currentBlock)` branch.
Notable gaps: place-block-and-read-TileEntity-lock-state (T1); spawn-entity-and-read-`IsLocked` (T1). These two are the primitives most of the cluster reuses.

### StrongHorns  (shape: code+xml; side: server)
Two `Server.Play` overload prefixes (pinned by exact `argumentTypes`) detect when the sound played on an `EntityVehicle`
equals its `GetHornSoundName()` → fire `OnHonk`. `OpenDoors` (registered on `GameAwake`) toggles the nearest
`honk_to_open`, trader-area door within 15 blocks, found via `NearbyBlockFinder` (which indexes qualifying blocks on
`Block.OnBlockLoaded`). Locked door: only the allow-listed attached driver opens it, else the locked sound plays.
`Config/blocks.xml` supplies the tag.
- [IL] Both `Server.Play` overloads (distinct 5-arg signatures) + `Block.OnBlockLoaded` resolve. Tool: EXISTING (smoke) — high value because overload-signature drift on `Server.Play` is a realistic breakage the pinned `argumentTypes` catch.
- [UNIT] `NearbyBlockFinder.ForeachNearbyBlock`: given tracked blocks at known positions, returns the single closest within `maxDistance` and nothing beyond it (and nothing when the category is empty). Tool: GAP — pure distance/chunk-key geometry, but wired through `Vector3i`, `World.toChunkXZ`, `WorldChunkCache.MakeChunkKey`; needs those in the off-game stub set.
- [T1] Honk near an unlocked `honk_to_open` trader door within 15 → door toggles open. Tool: GAP — spawn a vehicle, emit its horn sound (drive `Server.Play`), read door `isOpen` (combines the spawn primitive + tile-state read).
- [IN-GAME] Locked door, driver not on the allow-list → door stays shut, locked sound plays. Why not headless: needs an authenticated **attached** `EntityPlayer` (driver) with `PersistentPlayerData` and lock allow-list state.
Notable gaps: game-type stubs (`Vector3i`/`WorldChunkCache`) for the finder (UNIT); vehicle-spawn + horn-emit + door-read chain (T1); attached-driver ownership (IN-GAME).

### StrongBoxes  (shape: code; side: server)
Prefix on `TEFeatureStorage.OnUnlockedServer`: reads the parent composite's `TEFeatureSignable` authored text
(lowercased) as a label and invokes every registered listener whose predicate matches. `SortBoxes` registers
label `== "sort"`; its `OnClose` gathers adjacent-chunk lootables within 15 blocks — **but `Transfer()` is an empty stub,
so nothing actually moves today.**
- [IL] `TEFeatureStorage.OnUnlockedServer` + `TEFeatureSignable.GetAuthoredText().Text`, `TileEntityComposite.Parent`, `TryGetSelfOrFeature`, `ITileEntityLootable` resolve. Tool: EXISTING (smoke).
- [UNIT] Label dispatch: sign text `"Sort"`/`"SORT"` matches the sort listener case-insensitively; other labels match no listener. Tool: GAP — `OnUnlock`/`IsSortBox` are private and go through a live `TEFeatureStorage`/`Signable`; needs a seam so listener routing is testable on a plain string.
- [UNIT] `CalculateAdjacentChunkKeys(pos)` returns the 9 surrounding chunk keys. Tool: GAP — same `WorldChunkCache`/`Vector3i` stub need as StrongHorns.
- [T1] Documents the stub: close a box labeled "sort" adjacent to a filled box → **no items transfer** (asserts current no-op reality; flip the assertion when `Transfer` is implemented). Tool: GAP — place two boxes with a sign + inventory, trigger unlock/close, read both inventories (tile-state place/read + inventory read).
Notable gaps: seam for label-dispatch UNIT test; chunk-key/`Vector3i` stubs; tile-state + inventory place/read (T1). Flag for parent: `Transfer` is unimplemented.

### DynamicFeralSense  (shape: code; side: server)
Two **transpilers** replace the `EAIManager.CalcSenseScale()` call inside `PlayerStealth.TickServer` and
`EntityAlive.GetSeeDistance` with a per-biome multiplier of that base scale. `CalcSenseMultiplierForBiome`: PineForest
0×, burnt_forest 0.25×, Desert 0.5×, Snow 0.75×, Wasteland 1×, unknown 0.2× (with a 5-s-throttled error log); null biome
or base 0 → passthrough.
- [UNIT] `CalcSenseMultiplierForBiome`: each `BiomeType` yields the documented multiple of the base; null biome → base; base 0 → 0; unknown biome → 0.2× and logs once per 5 s. Tool: GAP — pure switch, highest-value logic in this mod, but `EAIManager.CalcSenseScale()` is *called inside* (reads game stats) rather than injected; needs an injectable/stubbable base scale + `BiomeDefinition.BiomeType` in the stub set.
- [IL] Outer targets `PlayerStealth.TickServer`, `EntityAlive.GetSeeDistance` resolve. Tool: EXISTING (smoke).
- [IL] **Transpiler match point**: `EAIManager.CalcSenseScale()` is still called, exactly once and matchable, inside each target's IL body so `MatchStartForward`/`ThrowIfInvalid` won't blow up at `PatchAll`. Tool: GAP — apply the transpiler (or statically scan the target's IL) against both units and assert the match succeeds and the splice is well-formed. **The smoke suite cannot see this.**
- [IN-GAME] Behavior: an entity in PineForest sees at 0× sense vs full in Wasteland. Why not headless: needs a spawned AI entity positioned in a known biome and its `GetSeeDistance` read under live AI.
Notable gaps: transpiler-application/IL-body test (headline); injectable base-scale seam + biome enum stubs for the UNIT test.

### DynamicLandClaimCount  (shape: code+xml; side: server)
Transpiler on `PersistentPlayerList.RemoveExtraLandClaims` swaps the global `GameStats.GetInt(LandClaimCount)` read for a
per-player `GetLandClaimCount(persistentPlayerData)`; **the source comment records that the intended
`LoadsConstant(EnumGameStats.LandClaimCount)` match "on linux this doesn't match; bug?"**, so it matches
`GameStats.GetInt` + `StoresLocal`, then `Advance(-1)`/`RemoveInstructions(2)` — platform-sensitive splicing.
Postfixes on `Add/RemoveLandProtectionBlock` (whisper count) and `WorldEnvironment.OnXMLChanged` (load cvars/op); a
`/claims` chat handler. `GetLandClaimCount` adds or overrides the base by the player's CVar values.
`Config/worldglobal.xml` + `Config/Localization.csv` carry the settings and message.
- [UNIT] `GetLandClaimCount(player, count, cvar)`: Add op → `count + cvar`; Override → `cvar`; missing cvar or negative CVar → base `count`. Plus `OnXMLChanged` parsing (comma-split cvars, enum op). Tool: GAP — pure logic, but reads `EntityPlayer.Buffs`/`GetCVar` and `DynamicProperties`; needs those stubbed. Highest-value logic in this mod.
- [IL] Outer targets `PersistentPlayerList.RemoveExtraLandClaims`, `WorldEnvironment.OnXMLChanged`, `PersistentPlayerData.Add/RemoveLandProtectionBlock` resolve. Tool: EXISTING (smoke).
- [IL] **Transpiler match point** (doubly important given the recorded Linux discrepancy): apply the transpiler against `RemoveExtraLandClaims`' real IL on **both units** and assert the `GameStats.GetInt` + `StoresLocal` match is found and exactly two instructions are removed/replaced. Tool: GAP — same transpiler-application gap as DFS; this one has a known cross-platform fragility, so per-unit coverage is the point.
- [T1]/[IN-GAME] Behavior: a persistent player with the configured CVar set retains the per-player claim count through `RemoveExtraLandClaims`, and `/claims` whispers correct `{used}`/`{total}`. Tool: GAP — a telnet-scriptable *named persistent player* with CVars and placed land-claim blocks; reading the whisper needs a client → IN-GAME.
- [LINT] `worldglobal.xml` well-formed (EXISTING build lint). `Localization.csv` structure (headers, key `dynamic_land_claim_count_message`) is **not** covered — XML lint ignores CSV. Tool: GAP — CSV/localization structural lint (small).
Notable gaps: transpiler-application test on both units (headline, platform-sensitive); `EntityPlayer.Buffs` stubs for the UNIT test; named-persistent-player + claim-block harness (T1/IN-GAME); CSV lint.

---

## Distinct tool gaps this cluster exposes

1. **Transpiler match-point / IL-body verification (headline).** The IL suite resolves only the outer `[HarmonyPatch]`
   target, never that a transpiler's internal `CodeMatch` sequence still exists in the body. DFS (`CalcSenseScale` call
   site ×2) and DLCC (`GameStats.GetInt` + the `LoadsConstant`/`StoresLocal` splice, *already flagged platform-sensitive
   in-source*) both fail at `PatchAll` runtime, invisibly to CI. Requirement: a test that applies each transpiler (or
   statically scans the target method's IL) against **both units** and asserts the match succeeds and splices exactly.
2. **Off-game logic seams + expanded game-type stubs (UNIT).** Pure logic is trapped behind private members and game
   types: DFS biome multipliers (needs injectable base scale + `BiomeDefinition.BiomeType`), DLCC add/override math
   (`EntityPlayer.Buffs`/`GetCVar`, `DynamicProperties`), StrongHorns `NearbyBlockFinder` + StrongBoxes chunk-key math
   (`Vector3i`, `World.toChunkXZ`, `WorldChunkCache`), StrongBoxes label dispatch, AuthZ authorization decision.
   Requirement: small seams (extract/inject) plus these types in the UnityEngine/game stub set.
3. **Place-block-and-read-TileEntity-state primitive (T1).** Place/open/label a `TEFeature*` block at a controlled world
   position (trader area where relevant) and read back feature state (`IsLocked`, `isOpen`, inventory). Needed by
   StrongLocks (the exemplar), AutoCloseDoors, StrongBoxes, StrongHorns. This is the canonical world-scan-safe-rung gap.
4. **Spawn-entity-and-read-flag primitive (T1).** Spawn an `EntityVehicle` and read `IsLocked`/ownership. StrongLocks
   vehicle patch and StrongHorns need it.
5. **Authenticated-sender packet + ownership harness (T2 extension) → spawned/attached player (IN-GAME).** AuthZ's
   vehicle-owner branch is reachable from a real `Sender.PlatformId` (T2), but its self/occupant branches and
   StrongHorns' allow-listed attached driver need a **spawned player entity with an `entityId`** — beyond current T2.
6. **Positioned-entity proximity (T1/IN-GAME).** AutoCloseDoors' 10 m player-suppression and DFS's biome behavior need an
   entity (player or AI) at a controlled world position — T2 can't spawn; T1 has no body.
7. **Named-persistent-player harness (DLCC).** A telnet-scriptable persistent player carrying CVars and land-claim blocks,
   to exercise per-player claim retention.
8. **CSV/localization structural lint (minor).** DLCC's `Localization.csv` is outside the XML well-formedness lint.

Parent note: AuthZ enforcement (`return false` rejection) and StrongBoxes `Transfer()` are unimplemented stubs today —
behavior tests written now must assert the current no-op reality, or be written as expected-to-fail against the intended
behavior.

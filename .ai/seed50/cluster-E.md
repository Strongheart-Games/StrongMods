# Cluster E — StrongUtils, StrongholdTweaks, Hades, StrongholdSaves (#50)

Scope: V3.1.0 (b14). Paper analysis — read from source/XML/build files; nothing booted. Test ideas first, then
tool mapping. Tags: [T1] server telnet, [T2] client→server protocol, [IL] patch-target resolution, [UNIT] off-game
logic, [LINT] XML well-formedness/structure, [IN-GAME] needs #49 or a real client.

**Two existing tools bear heavily on this cluster and reframe several "gaps":**
- **[IL] TargetResolver** (`Tests/TargetResolver.cs`) already resolves *every* `[HarmonyPatch]` in the solution
  against both units on every CI run. So "patch target X still exists" is **already covered** for every StrongUtils
  patch below — I note it once per patch and do not re-file it as a gap.
- **[patcher-replay] PatchPipeline / PatchApplicationTests** (`Tests/Fixtures/PatchPipeline.cs`,
  `Tests/Patcher/PatchApplicationTests.cs`) already applies every mod's real `Config\*.xml` onto the unit's real
  vanilla XML headlessly and **fails on any error/warning** (an xpath that matches nothing warns). This is the
  existing home for "a game update moved a StrongholdTweaks xpath target" — largely NOT a gap. Its blind spots
  (foreign-mod-conditional blocks; no post-patch *value* assertion) are the real StrongholdTweaks gaps.

---

## BUCKET 1 — StrongUtils (shape: code; side: server)

Foundational admin/modding grab-bag. `Initializer.InitMod` does `PatchAll` + registers ModEvents handlers
(`GameAwake`→ConfigManager/ServerLifecycleCommands init, `GameStartDone`→StrongZones init, `PlayerDisconnected`/
`EntityKilled`→PlayerDamage, `PlayerSpawnedInWorld`→BackpackItems). Every feature is server-guarded
(`ConnectionManager.Instance.IsClient` early-returns). Depends on StrongMods (ModInfo) but no ProjectReference.

### Feature 1a — Server console commands (shape: code; side: server)
Eight `ConsoleCmdAbstract` subclasses in `Commands/`, auto-discovered by the game. All emit output via
`SdtdConsole.Instance.Output`, so all are telnet-observable.

- [T1] `denyall enable` then `denyall status` → telnet output reports `Enabled: True` and the reason/message; after
  `denyall disable`, `Enabled: False`. Tool: existing Tier-1 (pure command→console-output round trip, no world state).
- [T1] `denyall enable`, then a Tier-2 client attempts to connect → connection is refused with the kick message
  (exercises `ServerStateAuthorizer_Authorize_Patch` reading `DenyAll.IsEnabled()`). Tool: Tier-1 server + **Tier-2
  client as the connection driver** — this is the one command whose real assertion is a client→server outcome.
- [T1] `gracefulshutdown start 2` → a `[00ff00]...` countdown line is broadcast to global chat immediately, and
  `gracefulshutdown cancel` emits the cancel broadcast and stops the coroutine. Tool: Tier-1 (assert on chat/console
  output); real shutdown-after-N-minutes is time-bound, assert only the first tick + cancel.
- [T1] `ongamestartdone "<cmd>"` writes an `<on_game_start_done command="...">` row to
  `server_lifecycle_commands.xml`; on next `GameStartDone` the command runs once and the row is removed. Tool:
  Tier-1 across a restart, OR **[UNIT]** on `ServerLifecycleCommands` + `ConfigManager` directly (see 1e) — the
  file round-trip is pure and needs no world.
- [T1] `resetpois <name>` with a valid POI name → output `Reset N instance(s)`; with an unknown name → the "No POI
  instances found" tip. Tool: Tier-1, but the positive path needs a **known POI present in the test world at a known
  location** — see gap G1 (world-fixture with known prefabs).
- [T1] `teleportplayertobed <player>` with no bedroll → "Player has no bedroll"; with a bedroll → issues
  `teleportplayer` to the stored `BedrollPos`. Tool: Tier-1 **blocked on a real player entity** with a persistent
  bedroll — gap G2 (named persistent player fixture).
- [IL] every command that hard-references game internals resolves — `resetpois`→`World.ResetPOIS`,
  `QuestEventManager.manualResetTag`, `DynamicPrefabDecorator.allPrefabs`, `DynamicMeshManager.AddChunk`;
  `teleportplayertobed`→`PersistentPlayerData.HasBedrollPos/BedrollPos`. Tool: **GAP — these are plain API calls, not
  `[HarmonyPatch]` targets, so TargetResolver does not see them.** See gap G6.
- Note: `FastTravelCommand` is intentionally inert this season (`DeviceFlag.None`, `FastTravel.Init()` commented out
  in ServerLifecycle) — a test asserting it does nothing / that the KVStore client stays uninitialized is low value;
  skip.

Notable gaps: G1 (world with known POIs), G2 (named persistent player), G6 (non-Harmony game-API drift).

### Feature 1b — StrongZones (shape: code; side: server)
`StrongZones.cs`. Builds rectangular `StrongZone`s from three sources: prefab `StrongZones` DynamicProperty class,
`bTraderArea`/difficulty-tier "protected" prefabs, and hot-reloaded `strong_zones.xml`. Zones drive buffs
(BuffManager), hostile-spawn blocking + intruder-punish (NoHostileEnforcer), land-claim rejection (NoClaimsEnforcer),
and no-reset chunk protection (StrongZoneChunkProtector). Entity zone transitions come from a `World.TickEntity`
transpiler → `OnUpdateEntity`.

- [UNIT] `StrongZone` geometry: `new StrongZone(name, cornerXZ, oppositeCornerXZ)` normalizes Min/Max regardless of
  corner order, and `Contains(pos)` is true inside / on-edge and false just outside. Tool: **[UNIT] with the
  UnityEngine stub** — pure math (Vector2i/Vector3), no game types beyond stubs. Highest-value, cheapest zone test.
- [UNIT] `StrongZone.FromXml` parses `name`/`cornerXZ`/`oppositeCornerXZ`/`tags`, and throws
  `ArgumentException` on each missing/malformed attribute (bad `cornerXZ` arity, missing name). Tool: **[UNIT]** —
  `XElement` in, object out; the hot-reload config contract lives here.
- [UNIT] `FindZoneChanges(old,new,pos)` computes entered/left sets by reference identity (re-entering the same zone
  object yields no spurious enter). Tool: **[UNIT]** — pure list diff over StrongZone refs.
- [T1] a player walking into a `buff`-tagged zone gains `zone.BuffName`; on leaving the last such zone the buff is
  removed (BuffManager via enter/leave callbacks). Tool: Tier-1 **blocked** — needs a controllable player entity that
  can be moved between chunks and its `Buffs` read back. Gap G2 + G3 (read/assert a buff on an entity via telnet).
- [T1] land-claim placement inside a `no_claims` zone is stripped from the `ChangeBlocks` batch and the placer gets
  `buff_no_claims_violation` (NoClaimsEnforcer.RejectLandClaims, the `GameManager.ChangeBlocks` prefix). Tool: Tier-1
  **blocked** — needs "place a land-claim block as a named persistent player at a chosen position and read the
  resulting TileEntity + buff". Gap G2 + G4.
- [T1] a hostile spawn is suppressed inside a `no_hostiles` zone (`Chunk.CanMobsSpawnAtPos` prefix returns false via
  `FindZonesForPosition`). Tool: Tier-1 **blocked** — needs a zone loaded over a known chunk and a spawn attempt at a
  known position. Gap G1 + G5 (trigger/observe a spawn attempt at a position).
- [IL] the three transpiler/patch seams StrongZones depends on — `World.TickEntity`→`Entity.OnUpdateEntity`,
  `RegionFileManager.UpdateChunkProtectionLevels` (Clear() match + the `chunkProtectionLevels`/`groupProtectionLevels`/
  `chunkGroups` fields), `Prefab.ReadFromProperties`/ctor, `Chunk.CanMobsSpawnAtPos` — all resolve. Tool: existing IL
  for the `[HarmonyPatch]` classes; **GAP for the transpiler *internals*** (the CIL `Clear()` call, the field loads):
  TargetResolver confirms the *method* exists, not that the matched instruction pattern still exists. Gap G7
  (transpiler-match verification) — recurs across every transpiler in the repo.

Notable gaps: G1, G2, G3, G4, G5, G7. StrongZones is the densest source of the "real player/entity in a known world"
gaps.

### Feature 1c — Loot & container tweaks (shape: code; side: server)
Three independent Harmony patches.
- [T1] a loot container respawns on its `LootRespawnDays` timer without ever being opened
  (`TEFeatureStorage.UpdateTick` transpiler injects an early `Ret` after the `LootRespawnDays` GetInt). Tool: Tier-1
  **blocked** — time-and-world bound (needs a placed container, elapsed respawn timer). Realistically [IN-GAME] or a
  long Tier-1; the cheap proxy is [G7] transpiler-match verification that the `Ret` still lands after the right call.
- [T1] an entity with `buff_no_loot` drops nothing on death (`EntityAlive.dropItemOnDeath` prefix). Tool: Tier-1
  **blocked** — needs to spawn an entity, apply a buff, kill it, inspect drops. Gap G5 + G3. Cheap proxy: [IL]
  (already covered) confirms `dropItemOnDeath` exists.
- [UNIT/IL] player-owned vending machine keeps its own `resetInterval` instead of the trader interval
  (`TraderInfo.ResetInterval`/`ResetIntervalInTicks` getter prefixes gated on `PlayerOwned`). Tool: [IL] covers the
  getter targets; the branch logic (`PlayerOwned` → return own field) is trivially **[UNIT]** if `TraderInfo` were
  constructible with the stub — likely not, so [IL] + code review is the realistic ceiling.
- [IL] `LootCommandPatch` (`ConsoleCmdLoot.ContainerList` transpiler fixing a vanilla NRE) resolves, and its
  ldnull/ldc.i4.0/`SpawnLootItemsFromList` match still holds. Tool: [IL] for the target; Gap G7 for the match.

Notable gaps: G3, G5, G7. Behavior here is largely [IN-GAME] or covered only shallowly by [IL].

### Feature 1d — Anti-grief & auditing (shape: code; side: server)
`StrongAudit` (bulk-edit log + auto-ban) and `PlayerDamage` (damage history dump on death + spoofed-damage flag).
- [T1] a non-admin persistent player submitting a multi-block `ChangeBlocks` batch is banned 10 years and a global
  chat line is broadcast; an **admin** doing the same is logged but NOT banned (`playerEntity.IsAdmin` guard). Tool:
  Tier-1 **blocked** — needs a named persistent player at a known admin level driving a bulk block edit. Gap G2 + G4.
  High value: the admin-exemption branch is exactly the regression that would silently ban staff.
- [T1] a single-block edit (`_blocksToChange.Count <= 1`) is never audited/banned. Tool: same as above (boundary of
  the same path).
- [T2] a `NetPackageDamageEntity` whose `Sender.entityId` != the damaged player's `entityId` is flagged with a
  warning (`PlayerDamage.ValidateDamageEntityPackage`, the `ProcessPackage` prefix). Tool: **Tier-2** — this is the
  archetypal client→server-ownership test: craft a damage packet from a real authenticated Sender and assert the
  server flags the mismatch. **BUT** Tier-2 does not reach player *spawn* (no `entityId`), and the check keys on a
  damaged **EntityPlayer** that exists in the world — so it needs a spawned player entity as the *target*. Gap G8
  (Tier-2 needs a spawned entity to damage) — a precise, load-bearing limit of the current Tier-2 client.
- [UNIT] `PlayerDamage` ring buffer: recording > `MaxEvents` (20) events keeps only the most recent 20, `GetHistory`
  returns them oldest-first, `ClearHistory` empties, and lookups are keyed by entityId. Tool: **[UNIT]** —
  `RecordDamage` takes `EntityPlayer` but only reads `.entityId`; a stub player suffices. Cheapest, highest-value
  PlayerDamage test (the concurrency/trim logic is where bugs hide).
- [IL] `GameManager.ChangeBlocks`, `NetPackageDamageEntity.ProcessPackage`, `EntityPlayer.DamageEntity` targets
  resolve. Tool: existing [IL].

Notable gaps: G2, G4, G8 (Tier-2 spawned-target limit).

### Feature 1e — Reusable infrastructure (shape: code; side: server)
`ConfigManager`, `KeyValueStore/XmlKeyValueStore`, `Chat`, `ServerLifecycle(Commands)`. Pure .NET + `System.Xml.Linq`
+ `System.IO` — the strongest [UNIT] targets in the whole cluster (only `Log.Out` touches a game type, and that is
stubbable).
- [UNIT] `ConfigManager.RegisterConfigFile` creates the file with default contents when absent; a second registration
  of the same name is ignored with a warning; a rooted filename throws. Tool: **[UNIT]** against a temp dir.
- [UNIT] `ConfigManager` FileSystemWatcher hot-reload: editing a registered file on disk invokes the registered
  `Action<XElement>` with the new document (this is the mechanism StrongZones' `strong_zones.xml` reload rides on).
  Tool: **[UNIT]** — write to a temp file, await the callback. Watcher timing makes it mildly flaky; still off-game.
- [UNIT] `ConfigManager.AppendConfig`/`RemoveConfig` round-trip: append an element then `RemoveConfig` (matched by
  `XNode.DeepEquals`) restores the file; RemoveConfig on an unregistered file throws. Tool: **[UNIT]**. This is the
  exact path `ServerLifecycleCommands` uses.
- [UNIT] `XmlKeyValueStore`: typed `Set`/`Get<T>` round-trips through the XML file across a reopen; `TestAndSet`
  succeeds only on exact raw+type match and fails on type mismatch; `Remove`/`Clear` raise `VarChanged` with the
  right change type. Tool: **[UNIT]** against a temp file. (KVStore is dormant in-game this season but the class is
  fully unit-testable and is published infrastructure.)
- [UNIT] `ServerLifecycleCommands` end-to-end over a temp ConfigManager: `AddCommand` persists an
  `on_game_start_done` row; `OnGameStartDone` loads+executes+removes them. Tool: **[UNIT]** — only `ExecuteCommands`
  touches `SdtdConsole` (inject/stub), the load/remove is pure XML.

Notable gaps: **none** — this is the cleanest [UNIT] surface in the cluster and needs no new tool.

### Feature 1f — Misc (shape: code; side: server)
- [UNIT] `BackpackItemsOnEnterGame` XML parsing: given an entity class with a `BackpackItemsOnEnterGame` Dynamic
  property class, the per-game-mode items are parsed into `ItemStack`s and an unknown item name is skipped with an
  error. Tool: [UNIT] **only if** `ItemStack.FromString`/`EntityClass` are reachable with the stub — likely needs
  real game assemblies, so realistically Tier-1 with `NewGame`/`EnterMultiplayer` spawn → **blocked** on G2. [IL]
  covers `EntityAlive.CopyPropertiesFromEntityClass`.
- [T1] `SignDiagnostics`: a corrupt canvas/sign turns into a `[SignDiagnostics]` warning naming the block + POI
  instead of a load failure (`TEFeatureCanvas.SetBlockEntityData` Finalizer). Tool: Tier-1 **blocked** — needs a
  deliberately-corrupt canvas TileEntity in a known POI. [IN-GAME]/manual realistically; [IL] covers the target.

Notable gaps: G2.

---

## BUCKET 2 — StrongholdTweaks (shape: xml modlet; side: server-distributed)

15 `Config\*.xml` patch sets. Uses vanilla patch verbs plus repo extensions: `<ensure>` (StrongMods upsert),
`<csv>`, `<conditional>`/`<if cond="mod_loaded('X')">`, `<include filename=...>`. **The existing patcher-replay
already covers the core regression** (a moved xpath warns → test fails); I group domains by *residual* risk beyond
that.

### Group A — Vanilla-targeted structural edits (blocks, entityclasses, events, progression, worldglobal, gameevents)
Highest xpath-drift risk because they `set`/`remove`/`setattribute` deep into vanilla paths that game updates rename.
- [patcher-replay] each of these applies against real vanilla with no warning (e.g.
  `entityclasses` HealthMax/StaminaMax `set`, `progression` perkLockPicking passive_effect `setattribute`, `events`
  christmas/thanksgiving/halloween `setattribute`, `blocks` Workstation/Campfire rotation edits, `worldglobal`
  blood_rain remove+append). Tool: **existing PatchApplicationTests** — these are already guarded; the value is that
  a b15 update renaming any of these paths fails CI. Confirm each is exercised (none are foreign-mod gated → all run).
- [UNIT/patcher-value] post-patch *value* assertion: after patching, `playerMale` HealthMax == 200 and
  StaminaMax == 200; `perkLockPicking` LockPickTime == ".25,.75". Tool: **GAP G9 — the replay asserts clean-apply,
  not resulting document values.** A `<set>` that matches the wrong node, or a vanilla default that changed so the
  edit is now a no-op-but-clean, passes today. Extend the patcher harness to assert post-patch node values. High
  value for the health/stamina/lockpick edits where a silently-wrong value is a live balance regression.
- [T1] runtime effect: blood_rain schedule (`worldglobal` cron `0 4,13,20 * * *`) actually drives a blood-rain event;
  the extended holiday windows fire. Tool: Tier-1 possible but time/date bound → realistically **[IN-GAME]**.

### Group B — New-content additions (challenges, quests, items, item_modifiers, recipes)
Add new nodes under vanilla roots; low drift risk (append targets are stable roots) but high **internal-reference**
risk (a new recipe/quest referencing an item or key that must exist).
- [patcher-replay] all apply cleanly against vanilla (append to `/quests`, `/items`, `/recipes`, `/challenges`,
  `/item_modifiers`). Tool: existing.
- [LINT/GAP] cross-reference integrity: the Stronghold quests/challenges reference `*_key` localization keys
  (`Localization.csv`), buffs, and events (`challenge_reward_bed_tp`→gameevents `action_sequence`); the
  `mod_hybrid_vehicle_kit_strong` recipe/item_modifier pair must agree on name and reference real ingredients
  (`carBattery`, `resourceElectricParts`). Tool: **GAP G10 — a reference-integrity linter** (does every
  `title_key`/`buff`/`reward_event`/ingredient name resolve against vanilla+this-mod+Localization.csv?). Neither
  well-formedness lint (#41 planned) nor the apply-replay catches a dangling *reference* that is itself well-formed.
- [T1] a Stronghold quest can be granted and its objectives complete (quest_find_stronghold Goto→StatAwarded). Tool:
  Tier-1 **blocked** on a real player driving quest flow → **[IN-GAME]** (#49).

### Group C — Foreign-mod-conditional patches (items, entityclasses, loot, recipes, ui_display, items_xmas_cooking)
Wrapped in `<if cond="mod_loaded('ProjectZ'/'ChristmasCookbook'/'PootPavillion'/...)">`. **These are the
patcher-replay's blind spot** — `mod_loaded` is false in the vanilla-only replay, so the whole block is skipped and
never verified (this is exactly why `ProjectZFixes/*` sit in the replay's `ExpectedToLog`/skip list, and #61 is
cited in-code as the follow-up for testing against the mods they target).
- [patcher-replay-extended/GAP] `items_xmas_cooking.xml` (the largest single file, 129 lines of `set`/`append` on
  `xmas*` items) only applies when ChristmasCookbook is present, and its `<include>` from `items.xml` is likewise
  gated. Its xpaths are **never exercised today**. Tool: **GAP G11 — apply foreign-mod-conditional patches against a
  fixture that supplies the target mod's content** (or a recorded snapshot of it). Same requirement as repo issue
  #61; StrongholdTweaks is a heavy consumer.
- [LINT] well-formedness of all 15 files + Localization.csv. Tool: existing build-time XmlLint (runs already).

### Group D — Dormant/near-empty (spawning, loot's commented Opal blocks, items' commented Master Tool)
`spawning.xml` is effectively empty (all commented); much of `loot.xml`/`recipes.xml`/`items.xml` is commented-out
Season-6 content. Low value — a test asserting "spawning.xml is a clean no-op" is not worth writing. Note only:
commented XML is invisible to every tool, so re-enabling it later gets no automatic coverage.

Notable gaps: G9 (post-patch value assertion), G10 (reference-integrity linter), G11 (foreign-mod-conditional
patch verification / #61). The pure xpath-drift regression is **already covered** by PatchApplicationTests.

---

## BUCKET 3 — Hades (shape: overlay; side: server)

Deploys into `Mods\Hades\` beside ~400 MB of World-Editor-authored world binaries the repo does not manage.
`DeployRoot=$(ModsDir)\Hades`; MirrorOnDeploy scopes = `Config`, `ModInfo.xml`, `README.md`, `Worlds\Hades S6\
WalkerSim.xml`. Everything else (esp. `Prefabs\`, live-edited world binaries `.blocks.nim/.ins/.mesh/.tts`) deploys
protective-additive and is never deleted. This is the incident-hardened path (2026-07-30: an empty MirrorOnDeploy
vector once expanded to the whole root and deleted unmanaged files).

- [deploy-shape] protective-additive default: a file present at the deploy root but **absent from source and outside
  every MirrorOnDeploy scope** (e.g. a live-edited `Prefabs\POIs\foo.mesh`) survives a deploy untouched. Tool:
  **GAP G12 — a deploy-shape test harness (#42)** that runs `Deploy` against a scratch `-p:ModsDir` root seeded with
  fixture files and asserts survive/overwrite/delete outcomes. No existing tool exercises `Deploy` semantics.
- [deploy-shape] mirror scope deletes stale: a file under `Config\` at the destination that no longer exists in
  source **is** deleted on deploy (source-authoritative within the scope), while a stale file under `Prefabs\` is
  **not**. Tool: G12.
- [deploy-shape] the empty-vector regression guard: with `MirrorOnDeploy` empty, `Deploy` must mirror-scope **nothing**
  (not the whole root). Tool: G12 — this is the exact 2026-07-30 incident; a permanent regression test for it is the
  single highest-value overlay test in the repo.
- [deploy-shape] the single-file-inside-unmanaged-territory scope: `Worlds\Hades S6\WalkerSim.xml` is mirrored
  (overwritten/deleted-if-stale) while its sibling world binaries in the same dir are protected. Tool: G12 — verifies
  file-identity vs directory-identity partitioning (`_MirrorFileId` vs `_MirrorDir`).
- [deploy-shape/#37] the install-version check: deploying onto an install whose `Assembly-CSharp.dll` matches no
  declared `SdtdTestVersions` is refused (the `_VerifyInstallVersion` target, two-levels-up install probe). Tool: G12
  variant — exercisable by pointing DeployRoot at a fixture tree with a mismatched assembly hash.
- [LINT] `Config\rwgmixer.xml` + `Config\sandbox_overrides.xml` are well-formed. Tool: existing XmlLint (overlay
  imports XmlLint.targets — confirmed in Overlay.targets line 189).
- [IN-GAME] prefab/world integrity: the shipped POIs (`stronghold_v_1_0/2_0`, `piddlys_hole`) load, their `.xml`
  matches their `.blocks.nim`, and the world generates. Tool: **[IN-GAME]** (#49) — binary prefab/world integrity is
  not headless-checkable; not a new-tool gap, just out of headless scope.

Notable gaps: G12 (deploy-shape harness, #42) — the dominant Hades gap, recurs for StrongholdSaves.

---

## BUCKET 4 — StrongholdSaves (shape: overlay; side: server)

Overlays Stronghold save/world config into the game's `Saves\` tree. `DeployRoot=$(SdtdSavesDir)`;
MirrorOnDeploy = `StrongMods\custom_chat_commands.xml` (a single file); `OverlayContentExclude=README.md`. Ships
exactly one content file today (`StrongMods\custom_chat_commands.xml`). The Saves tree is pure runtime territory, so
protective-additive is the whole point — never clobber a live save.

- [deploy-shape] `README.md` is excluded from staging (`OverlayContentExclude`) so it never lands in `Saves\`. Tool:
  **GAP G12** (deploy-shape harness) — assert the staged content set excludes README.
- [deploy-shape] `custom_chat_commands.xml` is mirror-scoped: an updated source overwrites the destination, and a
  removed source element/file is deleted — but **only that file**; any other file the live save tree already holds is
  untouched. Tool: G12.
- [deploy-shape] the `_VerifyInstallVersion` skip path: DeployRoot points into `AppData\...\Saves` which has no
  `Assembly-CSharp.dll` two levels up, so the version check finds no assembly and **skips** (a saves tree has no
  version). Tool: G12 variant — assert the deploy is NOT refused for a save-tree destination. This is a distinct
  code path from Hades' assembly-present check and worth its own case.
- [LINT] `custom_chat_commands.xml` is well-formed. Tool: existing XmlLint.

Notable gaps: G12 (same harness as Hades; StrongholdSaves adds the "version-check-skips-when-no-assembly" case).

---

## Distinct tool gaps this cluster exposes (deduplicated)

| ID | Requirement | Recurs across |
|----|-------------|---------------|
| **G1** | A Tier-1 world fixture containing **known POIs at known positions** (for resetpois positive path, no-hostiles chunk placement). | resetpois, StrongZones no-hostiles |
| **G2** | A telnet-scriptable **named persistent player** in the world with settable properties (bedroll, admin level, position) and a real `entityId`. Tier-1 can spawn a server but not a controllable persistent player; Tier-2 explicitly does NOT reach spawn. | teleportplayertobed, StrongZones buffs/claims, StrongAudit ban/admin-exemption, BackpackItems |
| **G3** | Read back an **entity's buff set** over telnet (assert a buff was added/removed). | StrongZones buffs, no-hostiles violation buff, buff_no_loot |
| **G4** | **Place a block (esp. land-claim) as a named player at a chosen position and read the resulting TileEntity + the batch outcome.** | NoClaims land-claim rejection, StrongAudit bulk-edit ban |
| **G5** | **Trigger/observe a hostile spawn or an entity death at a known position** (drops, spawn suppression). | no-hostiles spawn block, buff_no_loot death drops |
| **G6** | Drift detection for **non-`[HarmonyPatch]` game-API calls** — direct method/field references in command & feature code (`World.ResetPOIS`, `DynamicPrefabDecorator.allPrefabs`, `PersistentPlayerData.BedrollPos`, `TraderInfo.resetInterval`, `EntityClass.list`). TargetResolver only sees Harmony targets; a game update renaming these compiles-breaks only if the mod is rebuilt against the new tree — no test asserts it. | most StrongUtils features |
| **G7** | **Transpiler match-point verification** — assert the CIL pattern a transpiler `MatchStartForward`/`MatchEndForward` keys on still exists in the target method (TargetResolver confirms the method exists, not that the instruction sequence does). | TouchlessLootContainers, LootCommandPatch, StrongZones (2 transpilers), BackpackItems is not one — repo-wide |
| **G8** | Tier-2 needs a **spawned entity to target** — the spoofed-damage check keys on a damaged EntityPlayer that exists in the world, but Tier-2 has no `entityId`/spawn. A precise extension of the Tier-2 client. | PlayerDamage spoofed-damage [T2] |
| **G9** | **Post-patch document *value* assertion** in the patcher-replay harness (today it only asserts clean-apply, so a well-formed edit that hits the wrong node or is a silent no-op passes). | StrongholdTweaks Group A value edits |
| **G10** | **XML reference-integrity linter** — resolve every `*_key`/buff/event/ingredient/recipe reference against vanilla + this mod + Localization.csv. Deeper than #41's planned well-formedness/schema lint. | StrongholdTweaks Groups A/B |
| **G11** | **Apply foreign-mod-conditional patches against a fixture supplying the target mod's content** (= repo issue #61). The vanilla-only replay skips every `mod_loaded('X')` block, so those xpaths are never verified. | StrongholdTweaks Group C (esp. items_xmas_cooking), and repo-wide (ProjectZFixes/AEC*) |
| **G12** | **A deploy-shape test harness (#42)** — run `Deploy` against a scratch root seeded with fixtures and assert overlay outcomes: protective-additive survival, mirror-scope stale deletion, the empty-MirrorOnDeploy-vector guard (2026-07-30 incident), file-vs-directory scope partitioning, and the #37 install-version check (present → verify/refuse; absent → skip). | Hades, StrongholdSaves (dominant gap for both) |

**Cross-cutting theme:** the code mod (StrongUtils) splits cleanly into a **large already-covered or cheaply-[UNIT]-able
core** (all infrastructure, StrongZone geometry/parsing, PlayerDamage ring buffer, ServerLifecycleCommands — needing
*no new tool*) and a **cluster of behavior tests all blocked on the same missing primitive: a real, controllable
player/entity in a known world driven over telnet** (G1–G5). The XML modlet's true gaps are not xpath-drift (covered)
but **value-assertion (G9), reference-integrity (G10), and foreign-mod-conditional verification (G11)**. The two
overlays converge entirely on **one** gap: the deploy-shape harness (G12, #42).

# #50 seed — test-idea inventory → tool-gap inventory (agent-seeded draft)

**Status: a seed for the owner's review, not a finished plan.** Produced 2026-08-17 by fanning five analysis passes
across all 28 mod projects (feature-decomposing the two big buckets) and synthesizing here. It follows #50's method:
the **test ideas came first, unconstrained by tooling**, then each was mapped to the tool it needs; every gap below is
derived from a demonstrated test need, not speculation.

- Per-mod / per-feature raw detail (subject → assertion, one line per test, tagged by tier): `.ai/seed50/cluster-*.md`
  (A = XML modlets; B = Harmony behavior; C = loot/quest/time; D = chat/foundation; E = StrongUtils + StrongholdTweaks
  + overlays). This doc is the synthesis on top of them.
- Scope of every claim: V3.1.0 (b14), read from source / XML / IL. Anything needing a running server or client is
  labelled, not assumed.

Tags used throughout: **[IL]** patch-target/static · **[UNIT]** logic tested with no game running (stub + fixtures) ·
**[LINT]** XML/CSV structure · **[T1]** server-side, telnet-drivable · **[T2]** client→server protocol/ownership ·
**[IN-GAME]** needs the #49 runner or a real client.

**"Seam"** below always means the same thing: a small code change that lets a test call a mod's decision logic
directly, with no game running. Three shapes — extract the calculation into its own method, pass the game state in as
a parameter instead of reading it inside, or make a private member `internal` and add `InternalsVisibleTo`. Most
[UNIT] gaps are seams: the logic already needs no game, but today nothing outside the class can reach it.

## The most important framing: two big regression classes are already covered

Before any gap: the existing `Tests/` harness already blankets the two **most common** ways a mod breaks, so neither is
a gap, and the test-idea list should not re-file them.

1. **Harmony patch-target drift** — `Tests/TargetResolver.cs` + `SmokeTests.cs` resolve *every* `[HarmonyPatch]` and
   `[PatchTargetManifest]` target against **both** units on every CI run, with near-miss diagnostics. Every code mod's
   outer targets are covered today.
2. **XML xpath-drift** — `Tests/Patcher/PatchApplicationTests.cs` replays every mod's real `Config\*.xml` onto the
   unit's real vanilla config headlessly and fails on any undeclared warning. A game update that moves an xpath target
   fails CI today.

This reframes the whole exercise: the genuine gaps are **not** "does the patch attach / apply" — they are (a) the
correctness of *logic* that needs no game to run, (b) what the patch *does or references* beyond attaching, (c)
transpiler *bodies* (as opposed to their outer method), and (d) *runtime behavior* needing a controllable player/world.
That is where the test ideas and the gaps concentrated.

## Consolidated test-idea list (highest-value, by mod/feature)

Compact form — the assertion in brief and its tag. Full subject→assertion detail per mod is in the cluster files.
"EXISTING" means a current tool already covers it; everything else needs the gap noted in the next section.

### Code mods — Harmony behavior (cluster B)
| Mod | Highest-value test(s) | Tags | Needs |
|-----|-----------------------|------|-------|
| StrongLocks | placed lockable → `IsLocked()`; spawned vehicle → `IsLocked()`; identical-block no-op guard | [T1] | R2, R3 |
| AuthZ | authorization branch table (owner/occupant/ownerless); mode-gate pass-through | [UNIT],[T2] | U1, T2a, R1 |
| AutoCloseDoors | open `auto_close` trader door, no player ≤10 m → closes; player near → stays | [T1],[IN-GAME] | R2, R6 |
| StrongHorns | `NearbyBlockFinder` closest-block geometry; honk → nearest trader door toggles | [UNIT],[T1] | U1, R2, R3 |
| StrongBoxes | label dispatch (case-insensitive) + chunk-key math; **sort is a no-op today** | [UNIT],[T1] | U1, R2 |
| DynamicFeralSense | per-biome multiplier table; **transpiler match-point survives** | [UNIT],[IL] | U1, **S1** |
| DynamicLandClaimCount | add/override CVar math; **transpiler match-point (Linux-fragile)**; per-player claim retention | [UNIT],[IL],[T1] | U1, **S1**, R1 |

### Code mods — loot/quest/time (cluster C)
| Mod | Highest-value test(s) | Tags | Needs |
|-----|-----------------------|------|-------|
| AutoCollectLoot | `TryGetLootItem` substitution map; kill → no world bag when enabled | [UNIT],[T1],[IN-GAME] | U3, R3/R5 |
| BloodRain | cron next-start + `min_game_day` gate; InitParty transpiler match | [UNIT],[IL] | **U2**, S1 |
| BountifulQuests | **C# is an empty no-op**; behavior is all `dialogs.xml` | [IL],[LINT],[IN-GAME] | (see audit) |
| QuestUnlockFixes | transpiler moves the null-guard ahead of the deref | [IL],[IN-GAME] | S1, #49 |
| LootDiagnostics | postfix logs on loot roll | [IL],[T1] | R5 |
| DisableLAN | **port 11000 not bound** (the cleanest behavioral negative) | [IL],[T1] | R5 (port observer) |

### Code mods — chat / foundation (cluster D)
| Mod | Highest-value test(s) | Tags | Needs |
|-----|-----------------------|------|-------|
| CustomChatCommands | `Init` parse (trigger/alias/minAdminLevel/malformed); requirements; substitution | [UNIT],[T1] | (mostly EXISTING unit) + U1 |
| ChatCommandHelper | `TryGetCommand`/`ParseList` edge cases; privileged-command cvar gate | [UNIT],[T1] | U1 (private seam), R1 |
| StrongFill | `[ServerOnlyClass]` binds `strong_fill` at load; fill neighbor geometry | [T1],[UNIT] | U1, R2 |
| StrongMods | **foreach spec + cache seam already covered**; gaps: cross-mod load-order visibility, malformed-patch resilience, eligibility filters, body-command variety | [UNIT] | **F1** (ordering seam) |

### XML modlets (cluster A) — mostly reference/effect, not attach
| Mod | Highest-value test(s) | Tags | Needs |
|-----|-----------------------|------|-------|
| StrongMining | **`Extends`-chain existence** on 9 vanilla parents (top guard); self-regen invariant | [UNIT],[T1] | **S3**, R2/R4 |
| PlayerSpawnedTraders | `insertAfter` target EXISTING; `Extends`/`SpawnClass`/`Next` refs; growth→spawn | [UNIT],[IN-GAME] | S3, S4, #49 |
| PootPavillion | appends EXISTING; **missing loc keys (bug)**; grow-and-loot cycle | [LINT],[T1] | S3, S4, R4 |
| ProgressiveBiomes | spawning.xml targets EXISTING (best-covered); PZ boss body; respawn effect | [UNIT],[T1] | S6, R5 |
| ProjectZFixes / AEC* | all fix bodies target absent base mods → **zero effect coverage today** | [UNIT] | **S6/#61** |

### Big buckets & overlays (cluster E)
- **StrongUtils** splits cleanly: infrastructure (`ConfigManager`, `XmlKeyValueStore`, `Chat`, `ServerLifecycleCommands`),
  `StrongZone` geometry/parse/diff, and the `PlayerDamage` ring buffer are **cheap [UNIT] wins needing no new tool —
  write these now**. Its behavior features (commands, zone buffs/claims, anti-grief ban) are [T1] blocked on a real
  player/world (R1–R5). The spoofed-damage check is the archetypal [T2], but needs a spawned target entity (T2a).
- **StrongholdTweaks** (15 XML domains): xpath-drift EXISTING via the replay. Real gaps are **post-patch value
  assertion (S5)**, **reference-integrity (S3)**, and **foreign-mod-conditional bodies (S6/#61)** — `items_xmas_cooking.xml`
  is never exercised today.
- **Hades / StrongholdSaves** (overlays): nearly all value is **deploy-shape (D1/#42)** — protective-additive survival,
  mirror-scope stale deletion, the empty-`MirrorOnDeploy`-vector guard (the 2026-07-30 data-loss incident, the single
  highest-value overlay test), file-vs-directory scope, and the #37 install-version check (Hades verifies; Saves must
  *skip*). Prefab/world-binary integrity is [IN-GAME].

## Tool-gap inventory — consolidated, mapped, ranked

Deduped across all clusters. **Ranked by breadth** (how many test ideas each unblocks — #50's ranking rule), with cost
and how it maps to the existing inventory. Cheap-and-broad first.

| # | Tool gap | Maps to inventory | Cost | Unblocks (breadth) |
|---|----------|-------------------|------|--------------------|
| **U1** | **Test seams + expanded game-type stubs** — make mod logic callable with no game running (Vector3i/WorldChunkCache, BiomeDefinition, EntityPlayer.Buffs/CVar, constructible ClientInfo/EntityPlayer, WorldBase double; small extract/inject seams; InternalsVisibleTo for private-static) | extends the existing stub + fixtures | low, but part per-mod refactor | **widest** — StrongZone, PlayerDamage, DFS, DLCC, StrongHorns, StrongBoxes, AuthZ, both chat mods, StrongFill |
| **S3** | **Post-patch XML reference/graph validator** (`Extends`/`SpawnClass`/`Next`, ingredients, prefab/model paths, entitygroup names, alt-block lists, loc keys) — deeper than #41's planned schema lint | new; complements #41 | low–med | every XML modlet; StrongMining, PlayerSpawnedTraders, StrongholdTweaks B, PootPavillion |
| **S1** | **Transpiler match-point / IL-body verification** — assert the CIL pattern a transpiler keys on still exists (TargetResolver sees the method, not the instruction sequence); run against **both** units | extends TargetResolver | med | every transpiler: DFS×2, DLCC (Linux-fragile), StrongZones×2, TouchlessLootContainers, LootCommandPatch, BloodRain InitParty |
| **R1+R2** | **Tier-1.5 world-control harness**: a telnet-scriptable **named persistent player** (real `entityId`, settable bedroll/admin/position/CVars) + **place-block-as-player and read TileEntity/inventory** | new layer above Tier-1; **depends on player-spawn reachability** (see strategic note) | high | the largest block of *behavior* tests — StrongLocks, StrongZones, StrongAudit, DLCC, AuthZ, AutoCloseDoors, StrongBoxes, StrongMining, chat mods |
| **S6** | **Foreign-mod base-fixture + `mod_loaded()` override** (= repo #61): stack a captured/minimal base-mod `Config` so fix-mod xpaths hit real targets and conditional bodies run | = #61 (recognized, unbuilt) | med | AEC*, ProjectZFixes, ProgressiveBiomes boss, StrongholdTweaks Group C |
| **S4** | **Localization.csv header + key-resolution linter** (header schema + every `*_key`/`display_name_key`/`DescriptionKey` resolves) | extends lint (#16/#41) | low | PootPavillion (measured bug), DLCC, PlayerSpawnedTraders, ProjectZ — inconsistent headers repo-wide |
| **D1** | **Deploy-shape test harness** (= #42): run `Deploy` against a scratch root and assert overlay outcomes incl. the empty-vector guard and the #37 version check present/skip | = #42 (planned) | med | Hades, StrongholdSaves (dominant for both) |
| **S2** | **Non-Harmony game-API drift** coverage — direct method/field refs + `ModEvents` handlers TargetResolver can't see | extends TargetResolver | low–med | most StrongUtils commands, CustomChatCommands, StrongFill |
| **S5** | **Post-patch document *value* assertion** in the replay (today asserts clean-apply only) | extends PatchApplicationTests | low | StrongholdTweaks Group A (health/stamina/lockpick balance edits) |
| **R3–R6** | Runtime **observers/actuators**: spawn-entity+read-flag, buff-set read, spawn/death trigger+observe, force-loot-roll + loot-drop-at-P, port-binding, GamePrefs/weather, proximity positioning | new, atop R1+R2 | high | StrongLocks vehicle, buff_no_loot, no-hostiles, DisableLAN, AutoCollectLoot, LootDiagnostics, AutoCloseDoors, DFS |
| **U2** | **Deterministic clock + timezone injection** | new seam (BloodRain refactor) | low | BloodRain scheduling (untestable until it exists) |
| **U3** | **Populating game-data tables with no game running** (`ItemClass.nameToItem`, `EntityClass.list`) | extends stub/fixtures | low–med | AutoCollectLoot loot-map, BackpackItems |
| **T2a** | **Tier-2 client reaches a spawned target entity** — the spoofed-damage/ownership tests need a real `entityId` in the world | extends the #82 Tier-2 client | med | AuthZ, StrongAudit spoofed-damage |
| **F1** | **Breadth-first coroutine / ordering seam** — a synthetic ordered multi-mod/multi-file set to test load-order visibility, phase-2 malformed-patch resilience, eligibility filters | extends the patcher fixtures | med | StrongMods (its headline guarantee, currently untested above the cache) |
| **#49** | **In-game runner** — real client render, full quest flow, live AI, spawn confirmation | = #49 (planned) | high | AEC/Stronghold quest flows, DFS biome behavior, POI name render, trader growth, remote loot collect |

## The strategic finding: one capability unblocks the most, and it spans both threads

**R1/R2/R3/T2a and #76-concern-C all converge on the same missing capability: a controllable, *spawned* player entity
driven headlessly.** The current Tier-2 client (this month's #82) reaches world entry but stops short of
`NetPackageRequestToSpawnPlayer` (no `entityId`); Tier-1 spawns a server but no controllable player. So the single
highest-leverage infrastructure investment is **player-spawn reachability** — and it pays off in *two* directions at
once:
- the security thread (concern C / `ValidEntityIdForSender`, already on the map as "spawn-request reachability"), and
- the largest block of gameplay **behavior** tests in this exercise (every [T1] test blocked on R1/R2).

If the owner wants one tool-gap issue to rank at the top by leverage, it is that one — it is the hinge between "we can
attach and apply" (done) and "we can assert behavior" (blocked). Everything in the **U** and **S** groups is cheaper and
independent of it, so the natural sequencing is: **write the free [UNIT] tests now (U1 + StrongUtils infrastructure) →
build the cheap static/lint extensions (S1, S3, S4, S5, S2) → then make the big bet on the world-control harness (R1+R2,
shared with the security thread) → then #49 for the rest.**

## Found in passing — not #50's job, but the audit surfaced real defects

A test-planning pass over the source became a partial audit. Four mods ship code that does not do what the README
implies (all read from source, not run) — flagged because a "behavior test" for these must assert the current no-op, or
be written expected-to-fail against the intended behavior:

- **AuthZ** — enforcement is a **stub**: on an unauthorized PvE hit it logs a warning and still `return true`. An
  anti-grief mod that only logs. (Highest concern.)
- **StrongBoxes** — `SortBoxes.Transfer()` has an **empty body**: closing a "sort" box moves nothing.
- **BountifulQuests** — the C# postfix is an empty `// TODO`; all behavior is `dialogs.xml`.
- **PootPavillion** — blocks reference `display_name_key="pootPavillion_Name"` / `pootPavillion_Done_Name`, but the
  shipped `Localization.csv` defines neither (and its header is non-standard).

Intentionally dormant (not bugs, noted so they aren't mistaken for gaps): StrongUtils `FastTravel`, `KeyValueStore`,
and `SpawnScaler` are commented out this season.

## Open questions for the owner
1. Rank priority: do you want the free [UNIT] wins scheduled first (fastest green), or the world-control harness
   (R1+R2) prioritized because it's the shared hinge with the security thread?
2. Should the four "found in passing" defects become their own `type:bug` issues now, or wait?
3. Which tool gaps map to *existing* issue numbers you want reused (#61 for S6, #42 for D1, #49 for the in-game runner,
   #41 for parts of S3/S4) vs. new issues?

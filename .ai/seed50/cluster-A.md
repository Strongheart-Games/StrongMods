# Cluster A — XML-patch / config-effect mods (seed for #50)

Scope: V3.1.0 (b14). Paper analysis only — read each mod's `Config/**` and the game assemblies/config where a patched
value's effect matters. No server booted.

## Existing tool this cluster leans on (know it before reading the ideas)

The repo already has a **headless XML-patch replay** — `Tests/Fixtures/PatchPipeline.cs` driven by
`Tests/Patcher/PatchApplicationTests.cs` on `PatcherHost` (UnityEngine-stub + fixtures, off-game). Per declared version
label it loads the unit's **real vanilla `Data/Config`**, then applies every mod's `Config\*.xml` with the game's own
`XmlPatcher.PatchXml`, and asserts each applies with no error/warning unless declared in `ExpectedToLog`. Under the
BRIEFING tiers this is **[UNIT]** (stub + fixtures). Two structural facts drive every mapping below:

1. It replays against **vanilla only**. Patches targeting a third-party base mod's content (AEC, Project Z) match
   nothing and are pre-declared as expected-to-warn — so they get **zero effect coverage** today (this is #61).
2. It evaluates `<conditional><if cond="mod_loaded('X')">`: with X not loaded, the whole body is **skipped, silently and
   cleanly**. Every `mod_loaded(...)`-wrapped body in this cluster is therefore untested by the current suite.
3. It proves a patch *applies*; it does **not** resolve what the applied XML *references* — `Extends`, `SpawnClass`,
   ingredient/item names, block `Class`, prefab/model asset paths, icon names, or `DescriptionKey`/`display_name_key`
   localization keys. A renamed vanilla item/class/prefab passes the apply test and still breaks at game load.

`[LINT]` today is build-time XML *well-formedness* only (`build/XmlLint.targets`); the structural/schema validator is
planned (#41).

---

### AECInternationalMarketFixes  (shape: xml modlet; side: both — adds items/entities/quests/localization)
Wrapped (except gameevents) in `mod_loaded('AEC_InternationalMarket')`: grants an `aecIMTutorialBook` on spawn via
`playerMale` `ItemsOnEnterGame` (suppressed if `StrongholdTweaks` loaded), adds the `aec_im_tutorial` repeatable quest,
rebases `airdrop_*` entities onto vanilla `twitch_crate_template` and strips their extra properties/non-`property`
children, and (unconditional, gameevents) retargets `ActionGive_*` airdrop spawns to `SpawnContainer` at 4-8 m. Prices
`modelConnexionCard` at 500 / bundle 1.
- [UNIT] gameevents `ActionGive_*` airdrop retarget: with an AEC-International-Market base-XML fixture present, assert the
  `min_distance`/`max_distance`→4/8 and `class`→`SpawnContainer` setattributes hit a real node. Tool: **GAP — third-party
  base-mod XML fixture + `mod_loaded` override (#61)**; current suite only records it as "absent from vanilla".
- [UNIT] entityclasses/items/quests conditional bodies: with `mod_loaded('AEC_InternationalMarket')` forced true and base
  XML stacked, assert `aecIMTutorialBook` item + `aec_im_tutorial` quest append and the `airdrop_*` `extends=twitch_crate_template`
  rebase apply. Tool: **GAP — same base-mod/`mod_loaded` fixture**; today these are skipped, so entirely uncovered.
- [UNIT] StrongholdTweaks exclusion: with both `AEC_InternationalMarket` and `StrongholdTweaks` "loaded", assert the
  `csv op="add"` of `aecIMTutorialBook` into `playerMale` `ItemsOnEnterGame` does NOT run. Tool: **GAP — per-mod
  `mod_loaded` fixture driving multiple flags**.
- [UNIT] reference existence: `twitch_crate_template` still exists in vanilla `entityclasses.xml` (the rebase extends it).
  Tool: **GAP — post-patch reference/`extends`-target validator** (§3 above).
- [IN-GAME] full tutorial-quest flow (fetch 100 `zombieEar`, craft/place `internationalMarket`, craft `questItem_CryptoMiner`,
  place `cryptoMiner`, 5000 XP). Why not headless: real player quest-objective progression; Tier-2 has no `entityId`/spawn.
Notable gaps: base-mod stacking + `mod_loaded` override fixture (#61); post-patch `extends`/reference validator.

### AECVehiclesFixes  (shape: xml modlet; side: server — README: server-only)
Single `setattribute`: gives every `vehicle[starts-with(@name,'AEC')]` whose `hornSound=''` the value `bicycle_horn`.
- [UNIT] against an AEC-Vehicles base-XML fixture, assert the xpath selects the AEC vehicles with an empty `hornSound`
  and sets `bicycle_horn`. Tool: **GAP — third-party base-mod XML fixture (#61)**; against vanilla it matches nothing and
  is pre-declared.
- [UNIT] guard-clause precision: assert the `[@value='']` predicate leaves an AEC vehicle that already has a horn
  untouched (regression: predicate dropped → clobbers real horns). Tool: **GAP — same base-mod fixture**.
- [UNIT] reference existence: `bicycle_horn` is a real sound event in the unit. Tool: **GAP — asset/sound-reference
  validator** (or [LINT] once #41 covers value vocabularies).
Notable gaps: base-mod fixture (#61); sound-asset reference validation.

### ProjectZFixes  (shape: xml modlet; side: both — items/recipes/item_modifiers/localization)
Requires Project Z 2.2.4.1. Disables `air_spawn` on `action_spawn_reward_*` and places rewards 3-8 m; adds +5 Hypo/+5
Hyperthermal to `modRareArmorARResist*` item_modifiers and one `modArmorInsulatedLinerT3` ingredient to their recipes;
speeds scrap (`ScrapTimeOverride` 10→7, 15→10); adds Banshee localization.
- [UNIT] all four patches (gameevents/items/item_modifiers/recipes) against a Project-Z base-XML fixture: assert each
  xpath hits real PZ content and the reward `air_spawn=false`/3-8 m, the two thermal `passive_effect`s, and the extra
  recipe ingredient apply. Tool: **GAP — third-party base-mod XML fixture (#61)**; all four are pre-declared as
  absent-from-vanilla today.
- [UNIT] `ScrapTimeOverride` value predicate: confirm `[@value=10]`/`[@value=15]` selects the intended PZ items and
  rewrites to 7/10 (regression: PZ changes its override values → patch silently no-ops). Tool: **GAP — PZ base-mod
  fixture**.
- [UNIT] reference existence: `modArmorInsulatedLinerT3` (recipe ingredient) resolves as a real item. Tool: **GAP —
  ingredient/item-name reference validator**.
- [LINT] `Localization.csv` header + Banshee-key resolution: the 30-row CSV's keys resolve and its header matches the
  game's expected schema. Tool: **GAP — Localization.csv schema/key-resolution linter** (see cluster-wide finding).
Notable gaps: base-mod fixture (#61); ingredient reference validator; localization linter.

### ProgressiveBiomes  (shape: xml modlet; side: server — README: server-only, EAC-friendly)
`spawning.xml` (unconditional) rewrites `maxcount`/`respawndelay` on vanilla `biome[@name=...]/spawn[@id=...]` across
pine_forest/burnt_forest/desert/snow/wasteland; `sandbox_overrides.xml` (unconditional) appends three
`sandbox_override` options; `entitygroups.xml` + the boss `spawn` appends are wrapped in
`mod_loaded('ProjectZ') or mod_loaded('Z_Bosses')`.
- [UNIT] spawning.xml target existence: every `biome[@name='X']/spawn[@id='Y']` the file `set`s resolves against vanilla
  `spawning.xml` (a renamed biome or spawn id → `set` warns). Tool: **EXISTING** — `PatchApplicationTests` catches this
  as a new undeclared warning (this is the cluster's best-covered mod).
- [UNIT] sandbox_overrides append targets `/sandbox_overrides` and each `option` name (`BiomeZombieRespawn`,
  `BiomeAnimalRespawn`, `BiomeEnemyDensity`) is a real sandbox option. Tool: **EXISTING** for the apply; **GAP** for the
  option-name being a valid game enum value (reference validator / #41).
- [UNIT] Project-Z conditional body: with `mod_loaded('ProjectZ')` forced true (+ PZ entitygroups fixture), assert the
  boss-group `remove`s hit real groups and the `Bosses*Night` appends + `boss01/boss02` spawns land, and every `e n="..."`
  references a real boss entityclass (`bossDevourer`, `animalBossGrace`, …). Tool: **GAP — `mod_loaded` override +
  PZ entity fixture (#61) and entitygroup entity-name reference validator**.
- [T1] respawn-rate effect: on a server with the mod, a wasteland `nz01` spawn point's effective `respawndelay` matches
  the patched value. Tool: **GAP — telnet-readable spawn-config introspection** (no console command exposes per-biome
  spawn timers today); otherwise [IN-GAME].
Notable gaps: `mod_loaded`/PZ fixture (#61); entitygroup entity-name + sandbox-option-name reference validation.

### PootPavillion  (shape: xml modlet; side: both — block/item/recipe/loot synced, loc recommended on clients)
Appends a two-stage `PlantGrowing`→`CompositeTileEntity` toilet: `pootPavillionStage0` grows (rate 20, no light/fertile)
into `pootPavillionStage2` (a `TEFeatureStorage` with `LootList=pootPavillionLoot`); looting yields 1 `resourceDookie` +
1-5 `resourceTinkle` then downgrades back; recipe 10 wood/4 pipe/30 clay; 2 Tinkle → boiled water at campfire.
- [UNIT] four appends apply to `/blocks`, `/items`, `/lootcontainers`, `/recipes`. Tool: **EXISTING** (`PatchApplicationTests`
  — but only proves the append landed, not the content).
- [UNIT] reference existence: block `Class`/`property class` values (`PlantGrowing`, `CompositeTileEntity`,
  `TEFeatureStorage`), the model `@:Entities/LootContainers/toiletCommercial01Prefab.prefab`, recipe ingredients
  (`resourceWood`/`resourceMetalPipe`/`resourceClayLump`) and `craft_area="campfire"` all still exist in the unit. Tool:
  **GAP — post-patch reference/asset validator** (a renamed vanilla class/prefab/item passes apply, breaks at load).
- [LINT] localization keys resolve: **CONFIRMED BUG (measured)** — blocks use `display_name_key="pootPavillion_Name"`
  and `pootPavillion_Done_Name`, but `Localization.csv` ships neither (it has `pootPavillionStage0`/`pootPavillionStage2`);
  also its header is the non-standard `Key,File,Type,UsedIn,English`. Tool: **GAP — Localization.csv header + key-resolution
  linter**; a `display_name_key`/`DescriptionKey`→CSV cross-check would fail today.
- [T1] grow-and-loot cycle: after `PlantGrowing` matures Stage0→Stage2, looting the `pootPavillionLoot` container yields
  Dookie + 1-5 Tinkle and the block downgrades to Stage0. Tool: **GAP — telnet-scriptable place-block + advance-growth +
  read-TileEntity-loot**; likely [IN-GAME] absent that.
Notable gaps: reference/asset validator; Localization.csv linter; block-placement + growth + TileEntity-read harness.

### StrongMining  (shape: xml modlet; side: server — block defs synced, prefab part world-gen only)
Appends nine "Strong" terrain blocks, each `Extends` a vanilla terrain/ore block (`terrOreIron`, `terrOreLead`,
`terrOreCoal`, `terrOrePotassiumNitrate`, `terrOreOilDeposit`, `terrStone`, `terrDirt`, `terrSand`, `terrSnow`) with
`DowngradeBlock` set to *itself* so mining yields resources but never depletes the block. Ships `part_tractor_depot`.
- [UNIT] append to `/blocks` applies. Tool: **EXISTING** (`PatchApplicationTests`) — but does NOT check `Extends`.
- [UNIT] `Extends`-target existence: all nine vanilla parents still exist in the unit's `blocks.xml` (a game update
  renaming `terrOrePotassiumNitrate` → the Strong variant silently fails to load). Tool: **GAP — `Extends`-chain
  reference validator** — the single highest-value regression guard for this mod, currently uncaught.
- [T1] self-regeneration invariant: mine a placed `terrOreIronStrong`, assert it drops iron AND the block remains
  (`DowngradeBlock`=self). Tool: **GAP — telnet place-as-persistent-player block + damage/harvest + read-back block state**
  (matches the BRIEFING's named requirement); otherwise [IN-GAME].
- [LINT] prefab part `Prefabs/Parts/part_tractor_depot.xml` well-formed and its block palette references only defined
  blocks. Tool: **EXISTING** for well-formedness; **GAP** for palette-block reference validation.
Notable gaps: `Extends`-chain reference validator (top priority here); block-place-and-harvest T1 harness; prefab-palette
reference validation.

### PlayerSpawnedTraders  (shape: xml modlet; side: both — README: install on server and clients, adds blocks)
`insertAfter` the vanilla `block[@name='spawnTrader']`: five `spawnTrader{Rekt,Jen,Bob,Hugh,Joel}` blocks (each
`Extends spawnTrader`, sets `SpawnClass=npcTrader*`), five `PlantGrowing` mannequin blocks (extend
`decoMannequinMale/Female`, `Next`→the matching spawn block, `GrowthRate 0.02`), and a `traderPlaceableVariantHelper`
multiblock (`PlaceAltBlockValue` cycling the five via the R-menu).
- [UNIT] `insertAfter` target existence: vanilla `block[@name='spawnTrader']` still exists (removed/renamed → insertAfter
  warns). Tool: **EXISTING** (`PatchApplicationTests`) — this mod's cheapest, real regression guard.
- [UNIT] reference existence: `Extends` parents (`spawnTrader`, `decoMannequinMale/Female`), `SpawnClass` values
  (`npcTraderRekt/Jen/Bob/Hugh/Joel`), and each `Next` chain (`traderRektPlaceable`→`spawnTraderRekt`) resolve. Tool:
  **GAP — `Extends`/`SpawnClass`/`Next` reference validator**; a renamed vanilla trader NPC passes apply, breaks at spawn.
- [UNIT] variant-helper wiring: `PlaceAltBlockValue` lists exactly the five defined `*Placeable` blocks (typo → a variant
  silently missing from the R-menu). Tool: **GAP — intra-mod block-name cross-reference check**.
- [LINT] `Localization.csv` header (`Key,Source,Type,english` — lowercase `english`) and the `*PlaceableDesc` keys
  resolve. Tool: **GAP — Localization.csv schema/key linter**.
- [T1/IN-GAME] mannequin→trader growth: place `traderJenPlaceable`, after growth assert the `spawnTraderJen` block
  replaced it and an `npcTraderJen` entity spawned. Tool: **GAP — place-block + advance-growth + entity-spawn read**;
  spawn/entity confirmation is [IN-GAME] (Tier-2 reaches no spawn).
Notable gaps: `Extends`/`SpawnClass`/`Next` reference validator; intra-mod alt-block cross-reference check;
Localization.csv linter; growth→spawn T1/in-game harness.

---

## Distinct tool gaps this cluster exposes (for the parent synthesis)

1. **Third-party base-mod XML fixture + `mod_loaded()` override (#61).** The single biggest gap. Every
   fix-mod (AEC International Market, AEC Vehicles, Project Z) and every `mod_loaded('X')`-wrapped body (AEC's
   items/quests/entityclasses, ProgressiveBiomes' boss groups) is *invisible* to the current vanilla-only replay — it
   either matches nothing (pre-declared) or is skipped. Requirement: a `PatchPipeline` mode that stacks a captured (or
   synthetic-minimal) base-mod `Config` alongside vanilla and lets a test assert `mod_loaded(...)` true, so the fix's
   xpath is proven to hit a real target and the guard predicates (`[@value='']`, `[@value=10]`) are exercised.

2. **Post-patch reference / asset / graph validator.** The replay proves a patch *applies* but never resolves what the
   applied XML *points at*: `Extends` chains (StrongMining, PlayerSpawnedTraders, AEC), `SpawnClass`/`Next`
   (PlayerSpawnedTraders), recipe ingredient + `craft_area` names (PootPavillion, ProjectZ), block `Class`/`property class`
   and prefab/model asset paths (PootPavillion), entitygroup `e n=""` entity names + sandbox-option names
   (ProgressiveBiomes), and intra-mod alt-block lists. This is broader than #41's well-formedness/schema — it is
   cross-file semantic resolution against the post-patch config graph. Highest-value cheap guard for the pure-vanilla
   mods (StrongMining's `Extends`, PlayerSpawnedTraders' `SpawnClass`).

3. **Localization.csv header + key-resolution linter.** Measured concrete bug: PootPavillion references
   `display_name_key="pootPavillion_Name"` with no such CSV key, and its CSV header is non-standard
   (`Key,File,Type,UsedIn,English`); PlayerSpawnedTraders uses lowercase `english`. Requirement: validate each
   `Localization.csv` header against the game's expected schema and cross-check every `display_name_key`/`DescriptionKey`/
   `name_key`/`offer_key`/`description_key` in the mod's XML resolves to a shipped or vanilla localization key.

4. **Telnet-scriptable block lifecycle harness ([T1] extension).** Several effect assertions need place-a-block-as-a-named-
   persistent-player, mutate it (mine/harvest, or advance `PlantGrowing`), and read back block/TileEntity/entity state:
   StrongMining's self-regeneration, PootPavillion's grow-and-loot, PlayerSpawnedTraders' mannequin→trader growth,
   ProgressiveBiomes' effective respawn timers. No current console command exposes this; without it these fall to
   [IN-GAME] (#49). Entity-spawn confirmation (trader NPC, ItemsOnEnterGame grant) is [IN-GAME] regardless — Tier-2
   reaches no spawn/`entityId`.

**Best-covered-today note:** ProgressiveBiomes `spawning.xml`, PlayerSpawnedTraders `insertAfter`, and the plain
`/blocks|items|...` appends are already guarded by the existing `PatchApplicationTests` for target-existence/apply — the
gaps above are what those tests *cannot* see.

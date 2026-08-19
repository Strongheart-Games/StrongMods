# Why server-side-only custom POIs only "sorta work": a characterization

**Scope of every claim below: 7 Days to Die `V3.1.0 (b14)`, dedicated server + game client, Windows.** Static claims are
read from the shipped assemblies with Mono.Cecil 0.11.6 (net40) under PowerShell 5.1, a `DefaultAssemblyResolver`
pointed at each `Managed/` dir — the technique of `StrongDev/.ai/headless-server-testing.md` and the sibling ledgers.
Basis assemblies: `packages/7dtd.assemblies.game/3.1.0.14/7DaysToDie_Data/Managed/Assembly-CSharp.dll` (client-side
rendering paths) and `packages/7dtd.assemblies.dedicatedserver/3.1.0.14/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll`
(what the server loads and streams). Empirical claims are from a lane-A Tier-1 headless run (`.scratch/tier1`, port
26910 / telnet 8191) with a staged custom POI mod. Findings that read compiled behavior can change with any game update.

## Authorization and scope

Authorized defensive/creative research on 7 Days to Die server-side modding, by the server owner (Strongheart /
the Stronghold community, server "Hades") against their own server and licensed game files. Purpose: characterize a
first-party feature (The Fun Pimps state server-side-only custom POIs are *intended* to work) so a later fix prototype
has a concrete target. Nothing here targets third-party servers or players. **This is a characterization, not a fix.**

## Evidence labels

Each finding is tagged **[measured]** (observed in a live Tier-1 run this session), **[IL]** (read from the shipped
assemblies), or **[inferred]** (reasoned from IL + architecture; not directly observed). The Tier-1/Tier-2 boundary
section states exactly what a server-only harness could and could not establish.

## Verdict

A server-side-only custom POI diverges from a client-installed one at **three client-side seams**, all rooted in the
same architectural fact: **the server streams a POI's *live voxel blocks* and its *instance metadata*, but never its
*pre-baked art assets or config text*.** The blocks and the "a POI is here, named X, bounded Y" record cross the wire;
the distant imposter mesh (`.mesh`) and the localized display name are resolved by the client **from its own local
install by prefab name** and simply are not there for a custom POI. Ranked by confidence:

| # | Failure mode | Mechanism (one line) | Confidence |
|---|--------------|----------------------|------------|
| 1 | **No distant imposter** — POI is invisible/absent at range, "pops in" only when its chunks stream close | Client `PrefabLODManager` resolves each POI's imposter `.mesh` from the **local** install by name; a custom POI has no local file | **[IL]** strong; client render itself is Tier-2+ |
| 2 | **Blank / raw-key POI name** — danger meter, quest text, compass label show the prefab key or nothing | POI display name is a localization key resolved from the client's **local** merged localization; server mod localization *has* a streaming path but did **not** merge on a Tier-1 dedicated boot | **[IL]** path exists; **[measured]** negative on merge; net client result Tier-2+ |
| 3 | **Custom-block POIs render wrong/missing blocks** (a POI built only from vanilla blocks is unaffected) | Blocks stream by numeric ID; server-only block additions shift/omit IDs the client cannot map | **[inferred]**, scoped out of the single POI shape tested |

What **works** and is not a failure mode: the POI's **blocks stream and render up close** (they are baked into region
chunk data and sent via `NetPackageChunk`), and **quest targeting/selection works server-side** (the server's
`DynamicPrefabDecorator` contains the custom POI). "Sorta works" = the building is really there and really solid when
you reach it; what is missing is everything the client was expected to draw or name *from its own copy of the mod*.

---

## What loads server-side vs what the client is missing

| Asset | Where it lives | Crosses the wire to a client? | Consequence for a server-only custom POI |
|-------|----------------|-------------------------------|-------------------------------------------|
| POI **voxel blocks** (`.tts` + `.blocks.nim`), once placed in the world | Baked into region files at world-gen; held server-side | **Yes** — as chunk data (`NetPackageChunk.Setup(Chunk,…)`) **[IL]** | Renders correctly **up close** (assuming vanilla blocks). This is the "it does work" half. |
| POI **instance record** (id, prefab name, position, bounds, rotation) | Server `DynamicPrefabDecorator` | **Yes** — server pushes `PrefabInstance` records via `NetPackagePOIAround`; client `ProcessPackage` adds them and calls `PrefabLODManager.UpdatePrefabsAround` **[IL]** | Client *knows* a POI named X sits at Y — enough for compass/quest logic, **not** enough to draw it at range. |
| POI **distant imposter** (`.mesh`) | Client resolves locally by name | **No** — resolved from the client's own `PathAbstractions.PrefabImpostersSearchPaths` **[IL]** | **Failure mode 1**: no local `.mesh` → no distant render. |
| POI **display name** (localization key = prefab name) | Client's merged localization dictionary | **Partially** — a `NetPackageLocalization` server→client patch path exists, but see mode 2 | **Failure mode 2**: name blank / raw key. |
| **Custom block definitions** (if the POI uses non-vanilla blocks) | Client's own `blocks.xml` (from its install) | **No** — block config is applied client-side from the client's install; only numeric IDs stream | **Failure mode 3**: ID mismatch → wrong/missing blocks. |
| **Sleeper volumes / loot / spawns** (`.xml` prefab properties) | Server-side | n/a — server-authoritative | Works; these are server logic, not client assets. |

---

## Failure mode 1 — no distant imposter (the core "sorta")

**Symptom.** The custom POI does not appear at a distance; it materializes only when the player is close enough that its
chunks stream in. Community reports describe exactly this ("POIs don't always appear until you're near them because the
player's computer doesn't have the POI's 'imposter' file and the server doesn't transmit it").

**Mechanism [IL].** Two distant-rendering systems exist in b14, and the custom-POI gap is specifically in the *first*:

1. **`PrefabLODManager` (the classic distant-POI / imposter path).** On the client:
   - `NetPackagePOIAround.ProcessPackage` (game assembly) builds a `Dictionary<int, PrefabInstance>` from the
     server's push and calls `PrefabLODManager.UpdatePrefabsAround(…)`.
   - `UpdatePrefabsAround` iterates each `PrefabInstance` and calls
     **`PrefabInstance.GetImposterLocation()` → `PathAbstractions.SearchDefinition.GetLocation(name, …)`**, then keys a
     `MeshPrefabSet` cache by the resolved **`AbstractedLocation.FullPath`** and loads the mesh from that path.
   - `GetLocation` searches the client's **local** `PrefabImpostersSearchPaths` (its own `Data/Prefabs/POIs/**` and
     locally-installed mod prefab folders). For a server-only custom POI, **no local file matches the prefab name**, so
     no mesh is loaded and no distant GameObject is built. The client holds the instance record but has nothing to draw.
2. **The `DynamicMesh` system (`DynamicMeshManager` / `NetPackageDynamicMesh`)** *does* stream regenerated voxel meshes
   server→client (bidirectional: server sends region mesh, client requests next via `NetPackageDynamicClientArrive`).
   This is **not** a rescue for a distant custom POI, for two measured reasons:
   - **`DynamicMeshLandClaimOnly = True`** [measured, from the run's `GamePref.` dump] — the dynamic mesh only
     regenerates inside player land-claim areas (+`DynamicMeshLandClaimBuffer=3` chunks). A POI out in the world, with no
     LCB near it, is never covered.
   - **`DynamicMeshUseImposters = False`** [measured] — even where dynamic mesh runs, it does not stand in for the
     prefab imposter.
   - A full-world regen (`newworldregen`, `DynamicMeshConsoleCmd`) exists but is a **manual console operation** [IL], not
     something a normal world start performs.

So the two systems have disjoint jobs: `PrefabLODManager` draws the pristine distant POI from a **local** `.mesh`;
`DynamicMesh` streams **player-modified** terrain inside LCBs. A server-only custom POI falls in the gap between them.

| Evidence | Source | Detail |
|----------|--------|--------|
| Client receives POI instances, not meshes | game IL | `NetPackagePOIAround.ProcessPackage` → `Dictionary<int,PrefabInstance>` → `PrefabLODManager.UpdatePrefabsAround` |
| Imposter resolved locally by name | game IL | `PrefabInstance.GetImposterLocation()` → `PathAbstractions.SearchDefinition.GetLocation(name,…)` → `.FullPath` → mesh load |
| Separate imposter search path | game IL | `PathAbstractions.PrefabImpostersSearchPaths` (distinct from `PrefabsSearchPaths`) |
| Dynamic mesh is LCB-scoped, imposters off | measured | run log: `DynamicMeshLandClaimOnly = True`, `DynamicMeshUseImposters = False`, `DynamicMeshEnabled = True`, `DynamicMeshDistance = 1000` |
| Dynamic mesh full regen is manual | server IL | `DynamicMeshConsoleCmd.Execute` "newworldregen" / "World full regen: " |
| POI blocks do stream (renders up close) | game IL | `NetPackageChunk.Setup(Chunk, Boolean)` + read/write carry chunk block arrays |

**Fix-prototype target.** The concrete seam is `PrefabInstance.GetImposterLocation()` returning an unresolved location
on a client that lacks the file. A fix would need to either (a) stream the imposter `.mesh` bytes to the client keyed by
prefab name (a new package, or an extension of the `NetPackagePOIAround`/LOD path), or (b) drive the custom POI's
distant representation through the already-streaming `DynamicMesh` path (which would require lifting the
`LandClaimOnly`/`UseImposters` gates for POI regions and a server-side regen of the POI's region at placement).

## Failure mode 2 — blank or raw-key POI display name

**Symptom.** The POI's name is missing or shows the raw localization key in the danger meter / quest text / compass —
community reports attribute this to the client lacking the mod's `Localization` file.

**Mechanism — and a b14 nuance [IL] + [measured].** Unlike the imposter, localization **has** a server→client streaming
path in b14, so the V3.0-era "no localization on the client, ever" explanation is *not* the whole story:

- The server **can** stream localization: `NetPackageLocalization.prepareDataPackets` logs "Preparing Localization
  chunks for clients" and `sendPacketsToClient` logs "Starting to send Localization to …"; the client applies it via
  `Localization.LoadServerPatchDictionary(byte[])` → `loadCsv`. The payload is `Localization.PatchedData`, built by
  `Localization.WriteCsv()` from `patchedCells` (only mod-patched keys). Mod localization feeds `patchedCells` through
  `ModManager.LoadLocalizations` → `Localization.LoadPatchDictionaries(modPath,…)`, which loads `<mod>/Localization.csv`.
  [all IL]
- **But the merge is gated and did not fire on a Tier-1 dedicated boot [measured].** `ModManager.LoadLocalizations`
  only processes a mod whose root contains a **`Config/` directory** (`SdDirectory.Exists(modPath + "/Config")`), and is
  invoked as `LoadLocalizations(isLoadingInGame)` from the startup `LoadPatchStuff` coroutine. With a staged custom POI
  mod that had a valid `Localization.csv` at its root, **no `"[MODS] Loading localization from mod:"` line appeared** —
  first without a `Config/` dir, then again *with* one added. `loadCsv` was never entered (no success and no
  `"Could not load localization"` error), so the per-mod merge was skipped on the dedicated boot. The likely gate is the
  `isLoadingInGame` argument (a dedicated boot loads config with it false), but I did not fully isolate the trigger.

So the honest state of mode 2: **the machinery to stream a server-side POI's name to clients exists in b14**, which
means this symptom may be *improved or fixed* relative to older reports — **but** I could not get a mod's localization
to merge into the server's `PatchedData` during a Tier-1 dedicated run, so whether a server-only POI's name actually
reaches a joining client is **unconfirmed** and needs a real join (mode-2 is genuinely open).

> **Corrected 2026-08-17 (issue #85, `.ai/localization-merge-static-analysis.md`).** The `isLoadingInGame`-gate guess
> above is **refuted** by IL. The gate is `if (_isLoadingInGame) yield break;` — it skips loading *only while loading
> into a running game*; a dedicated boot runs `GameManager.Awake` → `LoadPatchStuff(isLoadingInGame: false)` →
> `LoadLocalizations(false)`, so the server **does** load mod localization at boot. The real reason no
> `"[MODS] Loading localization from mod:"` line appeared is a **test artifact**: the only load path reads
> `mod.Path + "/Config/Localization.csv"`, and this run staged the `.csv` at the mod **root**, so `SdFile.Exists` was
> false and `loadCsv` never ran (there is no mod-root fallback). Client and server `LoadLocalizations` are
> byte-identical, and `NetPackageLocalization` streams `PatchedData` unconditionally on join — so if a mod places its
> csv in `Config/`, the name reaches the client by construction. Mode 2 is therefore most likely **not** a broken path;
> the runtime re-test (POI with `Config/Localization.csv`, real client render) is the confirmation still owed.

| Evidence | Source | Detail |
|----------|--------|--------|
| Server→client localization stream exists | server IL | `NetPackageLocalization.prepareDataPackets` / `sendPacketsToClient`; client `Localization.LoadServerPatchDictionary` |
| Streamed payload = mod-patched cells | server IL | `WriteCsv` serializes `patchedCells`; `PatchedData` backing field |
| Mod localization feeds it | server IL | `ModManager.LoadLocalizations` → `Localization.LoadPatchDictionaries(modPath,…)` loads `<mod>/Localization.csv` |
| Merge gated on `Config/` dir + `isLoadingInGame` | server IL | `SdDirectory.Exists(modPath + "/Config")`; `LoadLocalizations(bool)` from `LoadPatchStuff` |
| Merge did **not** fire on dedicated boot | measured | no `"[MODS] Loading localization from mod"` line with or without a `Config/` dir; `loadCsv` never entered |

**Fix-prototype target.** `ModManager.LoadLocalizations` / the `isLoadingInGame`-gated invocation from `LoadPatchStuff`
on a dedicated server — establish under which boot path (if any) a mod's `Localization.csv` reaches `PatchedData`, then
confirm `NetPackageLocalization` carries it to a joining client.

## Failure mode 3 — custom-block POIs render wrong/missing blocks (scoped out, flagged)

**Not exercised by the tested POI shape** (a clone of a vanilla-block house). Stated as **[inferred]**: block data
streams as numeric IDs in chunk packages; a POI built from **custom blocks defined only server-side** would place IDs
the client cannot map to its own `blocks.xml`, yielding wrong or missing blocks even up close — a distinct problem from
the imposter/name gaps, and the general "server-side-only custom content" hazard rather than a POI-specific one. A POI
built entirely from vanilla blocks (the common case, and the tested one) sidesteps it. Left for a dedicated custom-block
test.

## What works (and why "sorta" is the right word)

- **Blocks render up close [IL].** `NetPackageChunk` carries the chunk's block arrays; the custom POI's placed blocks
  arrive with normal chunk streaming. The building is really there and solid.
- **Quest targeting is server-authoritative [IL].** `QuestEventManager.SetupTraderPrefabList` and
  `GetPrefabsByDifficultyTier` call `DynamicPrefabDecorator.GetPOIPrefabs` **on the server**, whose decorator contains
  the custom POI (the server loaded the mod). So a custom POI can be *selected* for quests, and quest goto/treasure
  points stream to the client (`NetPackageQuestGotoPoint`, `NetPackageQuestTreasurePoint`). The client-side quest
  *experience* still inherits modes 1–2 (no distant marker mesh, blank name), but the binding logic itself is not
  server-side-broken.

## Empirical record (Tier-1, this session)

A custom POI was staged as a **mod** (not written into the hardlinked `Data/` tree): `.scratch/tier1/server/Mods/
POITest_House/` — a clone of `abandoned_house_01`'s six prefab files renamed `poitest_house.*` under `Prefabs/POIs/`,
plus a `Localization.csv` and (second run) a `Config/blocks.xml`. Two clean lane-A runs (readiness marker at ~22s, exit
code 0, no orphan). Log evidence:

| Observation | Evidence |
|-------------|----------|
| Mod discovered and loaded | `[MODS] Loaded Mod: POITest_House (1.0.0)`; `version` over telnet lists `Mod POITest_House: 1.0.0` |
| No prefab-pool scan on an existing Navezgane save | zero `LoadPrefabs`/`prefab` load lines — the POI pool is scanned at **world-gen**, not when loading a pre-built world; on Navezgane the custom POI is loadable but not auto-placed |
| Dynamic-mesh prefs are LCB-scoped, imposters off | `DynamicMeshLandClaimOnly = True`, `DynamicMeshUseImposters = False`, `DynamicMeshEnabled = True`, `DynamicMeshDistance = 1000` |
| No dynamic mesh generated (no players/LCB) | save `…/StrongDevTier1/DynamicMeshes/` stayed empty |
| Mod localization not merged | no `"[MODS] Loading localization from mod"` line (both runs) |
| No POI-related load errors | only benign `[EOS]`/`[HResult] E_XBL_NOT_INITIALIZED` noise, unrelated |

Reuse note: the run reused the existing `…/Saves/Navezgane/StrongDevTier1` save (not deleted). The disposable driver,
static-analysis helper, and per-run logs were not retained; the evidence and conclusions are preserved in this report.

## The Tier-1 / Tier-2 boundary

**What Tier-1 (this harness) established:** the server loads a mod-supplied custom POI cleanly; the server-side prefab,
imposter, and localization discovery paths; the dynamic-mesh operational defaults (measured); that no dynamic mesh is
generated without a land claim; and — via static IL of the game assembly — the exact client-side resolution paths that
must fail for a server-only POI.

**What Tier-1 alone could NOT establish (needs a joining client):**

- **The actual client render** of failure mode 1 (imposter absent) and mode 2 (name blank). These are **client-side
  rendering/UI outcomes**. Note this is *beyond even a Tier-2 protocol client* (#82): a protocol client receives and can
  inspect **packets** (it could confirm `NetPackagePOIAround` carries the instance and that no imposter bytes follow,
  and whether `NetPackageLocalization` carries the POI key), but it does **not run the Unity render pipeline**, so it
  cannot literally observe "no distant mesh drawn." Full visual confirmation needs a real graphical client joined to the
  test server.
- **Whether server mod localization ever reaches a client** (mode 2): needs a join to observe `NetPackageLocalization`
  on the wire — a **Tier-2** subject. Pinning *why* the merge didn't fire on a dedicated boot is a further Tier-1
  static/dynamic task (the `isLoadingInGame` gate).
- **A placed custom POI in a live world:** on Navezgane the POI pool is not scanned when loading an existing save, so
  placement would require an RWG world generated *with* the mod present (or an in-world editor/spawn path), which
  entails a longer world-gen run and, for the client-facing modes, a joining client anyway.

## Reproduction

The following commands record the historical method. Their disposable scratch inputs were not retained.

```bash
# from repo root — stage a custom POI as a Mods folder (never the hardlinked Data tree), then run lane A:
#   .scratch/tier1/server/Mods/POITest_House/{ModInfo.xml, Prefabs/POIs/poitest_house.*, Localization.csv, Config/}
dotnet run .scratch/tier1/lanes/poi/poislice.cs        # cold start, telnet drive, shutdown; writes FINDINGS.md + logs
# static analysis (net40 Cecil, PowerShell 5.1), e.g.:
powershell -ExecutionPolicy Bypass -File .scratch/tier1/lanes/poi/cecil.ps1 -Unit game -Mode il -Query "PrefabLODManager.UpdatePrefabsAround"
```

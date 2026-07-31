# Load-order tiers: encode prefix intent

Resolves issue #18 (option A from the discussion there): each project declares a *semantic tier* instead of a raw
prefix, and one central map turns tiers into the established literals. Deployed folder names stay verbatim with
**one accepted exception**: AECVehiclesFixes moves `Z_` → `ZZ_` so its tier can be the honest `AfterDependencies`
(decision in the #18 discussion; the rename's safety is verified below).

## Ground truth (verified from the 2026-07-30 client log)

The game loads mods alphabetically by folder name with a **culture-aware** comparison. The observed order:

```
_disabled < 0_TFP_Harmony < 000000-StrongMods < plain names < Z_* < Z-IM-PZ-Compat < ZZ_* < ZZZZZZZZZZ_*
```

Two non-obvious facts the central map must record:

- Culture-aware, not ordinal: ordinal ASCII would sort `ZZ_` *before* `Z_` (`Z` 0x5A < `_` 0x5F). The prefixes only
  work because of the comparer the game actually uses.
- `000000-` is not absolutely first — `0_TFP_Harmony` (the game's own Harmony bootstrap, enforced first by the
  game) precedes it. That is correct and intended.

Project Z deploys in two styles, which drives the Z-count: the GA build is a single mod whose folder starts with
`ProjectZ` (sorts among plain names); the Experimental build is 10+ separate mods all starting with `Z_`. A mod
that must follow Project Z *in either style* therefore needs at least two Z's.

## The tier vocabulary

| Tier                | Prefix         | Meaning                                                                                                       |
|---------------------|----------------|---------------------------------------------------------------------------------------------------------------|
| `First`             | `000000-`      | Foundation other mods build on; must precede everything blockable. Only the game-enforced Harmony mods come earlier. |
| `AfterDependencies` | `ZZ_`          | After the mod's dependencies, wherever they sort — plain-named, `ProjectZ*` GA, or `Z_*` Experimental. Two Z's cover the worst case, so the tier holds regardless of how a dependency names itself. |
| `Last`              | `ZZZZZZZZZZ_`  | After everything, including other repos' "load last" attempts.                                                |
| `LocalConfig`       | `ZZZZZZZZZZ_`  | Alias of `Last` with sharper intent: the instance-specific config layer (à la ATG Dynamo localconfig); nothing may follow it. |

`Last` and `LocalConfig` deliberately share a prefix: the property encodes *intent*, the map collapses intents into
sort bands. Order within a band is alphabetical by project name, which is acceptable for the current occupants.
`AfterDependencies` also anticipates dependency-driven ordering (option D in #18): tiers named for intent, not for
sort mechanics, survive that transition unchanged.

## Per-project assignment

| Project           | Tier                | Intent (one-line comment in the csproj)                                        |
|-------------------|---------------------|--------------------------------------------------------------------------------|
| StrongMods        | `First`             | Building block for other mods; dependency validator must precede what it blocks. |
| AECVehiclesFixes  | `AfterDependencies` | After its ModInfo-declared dependencies (the AEC mods). **Deploy folder renames `Z_` → `ZZ_`.** |
| ProgressiveBiomes | `AfterDependencies` | After anything that tweaks spawning or entity groups (includes the `Z_*` suite). |
| ProjectZFixes     | `AfterDependencies` | After Project Z in either deploy style (GA `ProjectZ*` or Experimental `Z_*`); the ModInfo dependency is optional-or, the prefix covers both. |
| AutoCollectLoot   | `Last`              | After anything that might add new enemies.                                     |
| ChatCommandHelper | `Last`              | After anything that might implement a chat command.                            |
| StrongholdTweaks  | `LocalConfig`       | The true last layer: config specific to this instance.                         |

The remaining 21 projects have no prefix and no tier — nothing changes for them.

## The AECVehiclesFixes rename, verified safe

Renaming `Z_AECVehiclesFixes` → `ZZ_AECVehiclesFixes` moves it after the entire `Z_*` suite and to the head of the
`ZZ_` band (alphabetically before `ZZ_ProgressiveBiomes` and `ZZ_ProjectZFixes`). Only mods sorting *between* the
old and the new name change their order relative to it — the flip window: `Z_Armor_Improved` … `Z_Vulnerability`
plus `Z-IM-PZ-Compat`. Everything outside the window (including `AEC-Vehicles-NoMicrocraft`, the mod it patches,
which does ship a `vehicles.xml`) keeps its relative order and is irrelevant to the safety argument.

Checked 2026-07-30 against the live client install: AECVehiclesFixes patches only `vehicles.xml`, and a filename
search across the whole `Mods\` tree finds `vehicles.xml` only in `AEC-Vehicles-NoMicrocraft` (outside the window)
and AECVehiclesFixes itself — **no mod in the flip window touches it**. The order flip therefore cannot change any
XML outcome today.

The rename has two follow-on chores:

- `AECVehiclesFixes/README.md` Installation section names the `Z_` folder twice — update to `ZZ_`.
- The live client `Mods\Z_AECVehiclesFixes\` folder becomes an orphan after the first deploy under the new name
  (Deploy mirrors per-folder; it cannot remove a folder it no longer owns). Both folders would load and collide on
  the mod's internal `<Name>`, so the old folder must be deleted manually — a human step, flagged at handoff.

## Mechanism

In `build/Deploy.targets` (shared by both project shapes, evaluated after the project body), immediately before
`ModDeployName` is derived:

1. If `$(ModLoadTier)` is set and `$(ModLoadPrefix)` is empty, set `ModLoadPrefix` from the tier via one
   `<PropertyGroup>` of conditioned assignments, headed by a comment block carrying the ground-truth section above.
2. Guard rails (readable `<Error>`s, same style as the existing game-not-installed error):
   - both `ModLoadTier` and `ModLoadPrefix` set → error (ambiguous; pick one);
   - `ModLoadTier` set but not one of the five values → error listing the vocabulary.
3. `ModLoadPrefix` survives as the low-level escape hatch for anything a tier cannot express.

Edits:

- `build/Deploy.targets` — the map + guards (~18 lines).
- 7 csprojs — swap `<ModLoadPrefix>…</ModLoadPrefix>` for `<ModLoadTier>…</ModLoadTier>` plus the intent comment
  from the table (2 lines each).
- `AECVehiclesFixes/README.md` — two `Z_` folder references become `ZZ_`.
- `CLAUDE.md` — the Deploying bullet that documents `ModLoadPrefix` gains the tier property as the preferred form;
  the csproj example comment mentions `ModLoadTier`.
- Header comments in `build/Mod.targets` / `build/Modlet.targets` that mention `ModLoadPrefix` — one-line touch-ups.

Total well under the 100-line target.

## Verification

1. **Evaluation diff (primary, airtight):** `msbuild -getProperty:ModDeployName,OutDir,TargetDir` for all 28
   projects against a `git worktree` of HEAD via `build/tools/compare-eval.py` — 27 must be byte-identical, and
   AECVehiclesFixes must differ *exactly* by `Z_` → `ZZ_` in `ModDeployName`. Proves no deployed folder name moves
   except the one intended, without building anything.
2. **Guard-rail check:** evaluate one project with `-p:ModLoadTier=Bogus` and with both properties set; each must
   produce its readable error.
3. **Build:** `dotnet build StrongMods.sln -c Debug` (stages to `bin\` only; no deploy).

## Out of scope

- Renaming any deployed folder other than AECVehiclesFixes (above).
- Dependency-driven XML patch ordering (option D in the #18 discussion) — a possible future StrongMods feature that
  would dissolve prefix games for XML order; to be raised as its own issue if wanted.
- The `<Dependencies>` ModInfo spec work — adjacent (ProjectZFixes' optional-or dependency is why `AfterZPrefixed`
  exists), but no spec changes here.

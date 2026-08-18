# Overnight — localization linter (#50 gap S4), and the 9 real defects it found

**Date:** 2026-08-17 (unattended session) · **Scope:** V3.1.0 b14, game unit.
**Tool:** `StrongDev/.ai/tools/loclint.cs` (new, untracked). **Suite:** unaffected — the tool is a file-based
app, not part of any project.

```bash
dotnet run StrongDev/.ai/tools/loclint.cs
```

`-- --selftest` runs 21 built-in cases (all pass); `-- <ModName>` lints one mod. `SDTD_TREE` overrides the
game tree whose vanilla `Localization.csv` is the baseline; by default it takes the newest restored package
tree.

## The rules are read from the game's loader, not from convention

Every rule cites `Localization.loadCsv` / `addCsv` IL in the declared tree, so the linter reports **what the
game would actually do** rather than a style preference. Four facts were worth the reading:

1. **The first column's name is irrelevant.** `loadCsv` assigns `buffer[0] = "KEY"` *before* the check that
   compares `buffer[0]` to `"KEY"` — so that check can never fail, and its error branch is dead code. `Key`,
   `key`, anything: identical to the loader. The repo's five different spellings of column 1 are harmless.
2. **A header with fewer than 2 columns is discarded silently.** `loadCsv` returns false with no log at all.
   A mod whose localization is malformed this way loses *all* its text with nothing in the log to say so.
3. **Any other header column must match `^[A-Za-z]+$` or start with `context` (case-insensitive), or the
   whole file is rejected** with a `Log.Error`. That escape is why vanilla's own
   `Context / Alternate Text` column is legal despite its spaces and slash.
4. **Column names match the vanilla header case-insensitively** (`Extensions.EqualsCaseInsensitive`), so
   `English` and `english` are one column. A column vanilla does not have is **not** an error — it is
   silently appended as a new column. That is how a typo becomes a phantom language instead of a warning.

## Negative results worth recording

- **No mod's localization file is rejected today.** All ten headers pass rule 3.
- **The header inconsistency the seed doc flagged is cosmetic, not functional.** Five distinct headers exist
  across ten files (`Key,Source,Context,English` · `Key,Source,Type,english` · `key,english` ·
  `Key,File,Type,UsedIn,English` · `Key,File,Type,english`), and by rules 1 and 4 none of the differences
  changes what loads. Standardizing on vanilla's shape is still worth doing — it removes eleven warnings and
  the phantom columns — but it is **not** a bug, and it should not be filed as one.
- **`English` vs `english` does not matter.** Rule 4. Worth stating, because it looks like it should.

## Errors found — 9, in 3 mods, all verified against the source

| Mod | Where | Key referenced | Verified |
|-----|-------|----------------|----------|
| PootPavillion | `Config/blocks.xml:5` | `pootPavillion_Name` | defines `pootPavillionStage0`, not this — **already filed as #88** |
| PootPavillion | `Config/blocks.xml:39` | `pootPavillion_Done_Name` | defines `pootPavillionStage2`, not this — **#88** |
| PlayerSpawnedTraders | `Config/blocks.xml:35` | `traderRektPlaceableDesc` | CSV has `traderRektPlaceable`, no `…Desc` row |
| PlayerSpawnedTraders | `Config/blocks.xml:52` | `traderJenPlaceableDesc` | same |
| PlayerSpawnedTraders | `Config/blocks.xml:69` | `traderBobPlaceableDesc` | same |
| PlayerSpawnedTraders | `Config/blocks.xml:86` | `traderHughPlaceableDesc` | same |
| PlayerSpawnedTraders | `Config/blocks.xml:103` | `traderJoelPlaceableDesc` | same |
| StrongUtils | `Config/buffs.xml:5` | `buff_in_stronghold_name` | CSV defines the two `buff_strongsworn_*` and three `buff_no_claims_*` families only |
| StrongUtils | `Config/buffs.xml:6` | `buff_in_stronghold_desc` | same |

The two PootPavillion rows are the **positive control**: the linter independently rediscovers the bug already
filed as #88, which is the evidence that the rule works.

New, not previously filed:

- **PlayerSpawnedTraders ships five placeable trader blocks with no description text.** The CSV defines the
  *name* key for each (`traderRektPlaceable`, …) and the `…Desc` companion for the variant helper
  (`traderPlaceableVariantHelperDesc`), so the `…Desc` convention was clearly intended and five rows are
  simply missing. In game, each block's description renders as the raw key.
- **StrongUtils' `buff_in_stronghold` buff has neither a name nor a description.** `Config/buffs.xml` declares
  `name_key="buff_in_stronghold_name"` and `description_key="buff_in_stronghold_desc"`; neither is in
  `Config/Localization.csv`. This is the buff a player receives on entering a stronghold zone, so it is the
  most player-visible of the three.

## Warnings found — 11

- **`unknown-column` ×10.** `Source` (BloodRain, PlayerSpawnedTraders, ProjectZFixes, StrongFill,
  StrongholdTweaks, StrongUtils, AECInternationalMarketFixes), `Context` (AECInternationalMarketFixes,
  ProjectZFixes), `UsedIn` (PootPavillion). Each is silently appended as a new column by the loader. Vanilla's
  name for the same intent is `File` for `Source` and `Context / Alternate Text` for `Context`; `UsedIn`
  appears to be a hand-rolled cross-reference with no vanilla counterpart.
- **`extra-fields` ×1, and it is really a defect.**
  `AECInternationalMarketFixes/Config/Localization.csv:8` —
  `AEC_Groenland_ResearchPapers,items,item,,Greenland research papers` has **five** fields against a
  four-column header. The empty fourth field pushes the English text into a fifth column that the header does
  not declare, so the surplus is dropped and the `English` column gets the empty string. The item's name
  renders blank. Every other row in that file has four fields, so this is a stray comma, not a schema
  difference.

## What the tool checks, in full

CSV: header shorter than two columns (error) · illegal header column (error) · non-vanilla header column
(warn) · no recognized language column (warn) · empty key (error) · duplicate key (error) · row with more
fields than the header (warn). References: every `*_key="…"` attribute and every
`<property name="…Key|…_key" value="…"/>` pair in the mod's `Config/**/*.xml`, resolved against vanilla's
25,574 keys plus every key any repo mod defines (error if unresolved). Comma-separated key lists are split.

## A false positive that was found and fixed, worth knowing about

The first run flagged `PootPavillion/Config/items.xml:36`
(`DescriptionKey="resourceEmptyJarDescription"`). That reference lives inside an XML comment — a disabled
`resourceEmptyJar` item block, commented out with the note *"seem to break empty jars in 2.5"*. Commented-out
config is inert, so this was not a missing key. The tool now blanks XML comments before matching, preserving
newlines so line numbers still hold, and three self-test cases cover it. **Any future text-scanning lint over
this repo's XML needs the same treatment** — the configs carry a lot of commented-out history.

## If the owner wants issues from this

One `type:bug` per mod is the natural shape: `mod:PlayerSpawnedTraders` (five missing `…Desc` rows),
`mod:StrongUtils` (two missing `buff_in_stronghold_*` rows), `mod:AECInternationalMarketFixes` (the stray
comma). #88 already covers PootPavillion. The header standardization is a separate, lower-priority
`type:chore` — and the report above is the argument for **not** treating it as a bug.

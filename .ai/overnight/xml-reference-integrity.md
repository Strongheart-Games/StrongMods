# Overnight — post-patch XML reference integrity (#50 gap S3)

**Date:** 2026-08-17 (unattended session) · **Scope:** V3.1.0 b14 and V3.0.1 b4, game unit.
**Suite:** green, 398 passed.

## What was built

| File | What it is |
|------|------------|
| `Tests/Fixtures/ReferenceGraph.cs` | The name graph of the config XML: which documents define names, which attributes reference them. |
| `Tests/Patcher/ReferenceIntegrityTests.cs` | Four tests over that graph, per declared version. |
| `Tests/Fixtures/PatchPipeline.cs` | One added property, `Documents` — the merged post-patch documents the replay already produced but discarded. |

The gap the seed doc named was "post-patch XML reference/graph validator". It is built **inside the test
suite**, not as a standalone tool, because the merged document only exists after the game's own patcher has
run — and `PatchPipeline` already runs it headlessly against every declared version. A standalone tool would
have had to re-implement the patcher to see the same document.

## The design decision: differential, not absolute

**Vanilla itself ships references that resolve to nothing.** Measured, and identical in both declared
versions: **12**, every one of them a buff reference — ten in `buffs.xml`, one in `entityclasses.xml`, one in
`items.xml`. `buffPerkTheDentistSilver` and `…Gold` both add `buffPerkTheDentist`, which no `<buff name=…>`
defines; `buffIsOnFire` adds `buffBurningEnvironmentHack`; `twitch_cooldown` adds `twitch_Cooldown`, a
case-only mismatch against its own name; `twitch_regen`, `twitch_insta_regen` and `twitch_buffCritImmune`
each reference `buffInfection01Main` and `buffBurning`; an entity class removes `buffForest_Hazard_Over`; and
`meleeToolHammerOfGodAdmin` adds `knockdown`.

Those are The Fun Pimps', not this repo's. Asserting "zero dangling references" would mean carrying a
permanent exemption list describing the *game*, which says nothing about the mods. So the subject is the
difference: **a mod must not introduce a dangling reference vanilla did not already have.** That framing also
catches the reverse case for free — a mod that *removes* something vanilla still points at.

## What the rules cover

Nine rules, each admitted only after being measured clean against the untouched vanilla XML:

| Reference | Must resolve to |
|-----------|-----------------|
| `blocks.xml` property `Extends` | a `<block>` |
| `items.xml` property `Extends` | an `<item>` |
| `blocks.xml` property `Next` | a `<block>` |
| `blocks.xml` property `DowngradeBlock` | a `<block>` |
| `recipes.xml` `<recipe name>` | an item, block, or item modifier |
| `recipes.xml` `<ingredient name>` | an item, block, or item modifier |
| `loot.xml` `<item group>` | a `<lootgroup>` |
| `loot.xml` `<item name>` | an item, block, or item modifier |
| `<triggered_effect buff="…">` in buffs / items / blocks / progression / entityclasses | a `<buff>` |

**16,821 references** are resolved on V3.1.0 b14 and 16,752 on V3.0.1 b4 (16,646 and 16,577 of them in the
untouched vanilla documents; the rest are what the mods add). Two value conventions are excluded because they name
nothing the config defines: a value containing `:` (vanilla's shape-variant helpers, e.g.
`woodShapes:VariantHelper`) and a value starting with `@` (an asset path). Comma-separated lists are split —
without that, `buff="a,b,c"` looked like one dangling name and produced 100+ phantom findings in the first
measurement pass.

Getting the universe right mattered too: **item modifiers are craftable**. Before `item_modifiers.xml` was
added to the recipe/loot universe, 81 recipe outputs and 126 loot entries looked dangling (`modArmorBandolier`,
`modDyeRed`, …). All false.

A fourth test guards the guard: if fewer than 5,000 references are collected, the suite fails. A game update
that renames `<triggered_effect>` or `Extends` would otherwise silently reduce this to asserting nothing while
still passing green.

## The finding — 12 dangling ingredients in StrongholdTweaks

Merged dangling is **24** on both versions against a vanilla baseline of 12, so the mods introduce exactly
**12** — all of them the same finding.

`StrongholdTweaks/Config/recipes.xml` appends twelve "Strong crop" recipes whose seed ingredient is
`planted<Crop>1Sel2` — `plantedAloe1Sel2`, `plantedMushroom1Sel2`, `plantedPumpkin1Sel2`, and nine more.

Verified:

- **No vanilla block or item carries a `Sel<n>` suffix, in either declared version.** `grep -c Sel2` over
  `blocks.xml` returns 0 for both V3.1.0 b14 and V3.0.1 b4. The convention does not exist in the game's own
  naming; the actual crop chain is `plantedAloe1` → `plantedAloe2` → `plantedAloe3HarvestPlayer`.
- **No mod in this repo defines one.** The only occurrences repo-wide are these twelve ingredient references
  and their copies in `bin/`.

**What is not established:** the recipes sit inside
`<conditional><if cond="mod_loaded('PootPavillion') and (mod_loaded('ProjectZ') or mod_loaded('Z_Bosses'))">`,
so the remaining possibility is that the **foreign** mod ProjectZ or Z_Bosses defines these blocks. That
cannot be checked from this repo — it is exactly gap S6 / issue **#61**, the foreign-mod base fixture. So the
honest statement is: *these names resolve to nothing the repo or the game provides, and whether they resolve
at all depends on a mod nobody here can see.*

Given that, the twelve are **declared** in `KnownDangling` with the reason and the #61 pointer, following the
repo's established `ExpectedToLog` contract — including the second direction: a separate test fails if a
declared name ever stops dangling, so the list cannot rot. If the owner can check a ProjectZ install, that
resolves the question either way and the declaration should then be removed or turned into a fix.

## Negative results

- **Every `Extends` chain in the merged config resolves** — 5,966 block and 1,139 item references. The seed
  doc named StrongMining's "`Extends`-chain existence on 9 vanilla parents" as its top guard for that mod;
  this covers it, and every other mod's, on both versions.
- **Every loot group reference resolves** — 1,789 of them.
- **Apart from the twelve above, the repo introduces no dangling reference of any covered kind.**

# Overnight — how tonight's work lands on the open issues, plus a doc-rot pass (#70 / #55)

**Date:** 2026-08-17 (unattended session) · **Scope:** repo at this working tree; game claims V3.1.0 b14 /
V3.0.1 b4. Nothing was filed, edited or closed — an unattended session does none of those.

## Part 1 — where tonight's four harnesses land on existing issues

Read as a proposal, not a claim of completion. Several issues are **partly** served; saying so precisely is
the point.

| Issue | What it asks for | What tonight delivers | Verdict |
|-------|------------------|-----------------------|---------|
| **#58** Localization.csv conformance tests | "our files are well-formed, consistent, and actually applied" | `StrongDev/.ai/tools/loclint.cs` — well-formedness and consistency, with the rules read from the game's own `loadCsv` IL, plus key-resolution across every mod's XML. | **Most of it.** "Actually applied" is not covered: the linter checks the file the game *would* accept, not that a running game loaded it. If #58 means the static half, it is done and wants only a decision on whether the tool becomes a CI-run test. |
| **#41** XML lint follow-on: structural validation of Config patch files | structural validation beyond well-formedness | `Tests/Patcher/ReferenceIntegrityTests.cs` validates the *result* of patching (cross-references), which is adjacent but not the same thing — #41 is about the patch files' own structure (valid commands, required attributes). | **Complementary, not a substitute.** #41 still wants doing. |
| **#61** Mod compatibility tests (foreign-mod fixtures) | do our mods behave when other mods add content | Nothing built, but tonight produced the sharpest concrete case yet for it: twelve StrongholdTweaks recipe ingredients whose resolution *cannot be decided* without a ProjectZ install. | **Motivating evidence.** Worth pasting into #61. |
| **#50** test-idea → tool-gap inventory | the inventory itself | Four of its gaps are now built and run (U1, S1, S3, S4), and two of its assumptions were measured wrong — see below. | **Inventory needs an update, not a close.** |
| **#88** PootPavillion loc keys | the bug | Independently re-found by the new linter. | **Unchanged**, now with a regression tool behind it. |
| **#57** find infra behavior defended only by prose | a sweep | Part 2 below is a small instance of that sweep, scoped to AGENTS.md's structural claims. | **A sample, not the sweep.** |

### Two corrections the #50 inventory should carry

1. **Gap U1's premise is wrong.** It assumed "expanded game-type stubs" was low cost. Measured:
   Assembly-CSharp references **225 CoreModule types the stub does not declare**, and `EntityPlayer` fails to
   load on `UnityEngine.Vector3`. The workable split is two universes (stub = logging works, entities do not;
   real Unity = the reverse), which needs **no** stub expansion and **no** per-mod seam. Only logic needing
   both logging and entity types is blocked — much less than U1 implies.
2. **Gap S1's shape is wrong, and cheaper than described.** It proposed asserting the CIL pattern statically.
   Running the transpiler against the target's real IL answers the same question, reuses the mod's own
   `ThrowIfInvalid` diagnostics, and took one afternoon. The real obstacle was not the pattern matching — it
   was that Harmony's IL reader routes through MonoMod, which refuses this runner outright.

## Part 2 — doc-rot pass over AGENTS.md's structural claims (#70, #55)

Each claim below was checked against the repo, not against memory. **Reported only; AGENTS.md is unedited.**

### Claims that hold

| Claim | Check | Result |
|-------|-------|--------|
| "the canonical code mod is 4 lines" | line count of every mod csproj | holds — 12 mods are exactly 4 non-blank lines, and every deviation adds lines for a stated reason |
| "Only `BloodRain` actually pulls a package — Cronos" | `PackageReference` across all mod csproj | holds — BloodRain is the only one (the other hits are under `.scratch/`, which is gitignored) |
| "the only cross-project reference in the repo: `AutoCollectLoot` → `StrongMods`" | `ProjectReference` across all mod csproj | holds — exactly one |
| "there is deliberately no `Directory.Build.props`/`.targets`" | `ls Directory.Build.*` | holds — none exist |
| "**StrongMods loads first**, via `<ModLoadTier>First</ModLoadTier>`" | its csproj | holds |

### Claims that have drifted

1. **The test-suite description is badly understated — this is #70's core.** AGENTS.md describes the suite as:
   *"resolves every mod's Harmony patch targets … against the unit `$(SdtdDir)` points at"*. That describes
   `SmokeTests` alone. Even **before** tonight the suite also carried foreach and `ensure` spec conformance
   (13 classes), the real patch-application replay, the patcher cache, project-convention checks, build-path
   resolution and a settings lint — 269 tests across 22 classes. A reader following AGENTS.md would not know
   that changing a `Config\*.xml` file, a `.csproj`, or `.claude/settings.json` is covered by tests.
   Tonight added four more areas (mod logic, reference integrity, transpilers) and 134 tests.

   Suggested replacement framing, for the human to word: the suite has **six** areas — patch-target
   resolution, transpiler match points, XML patch application and post-patch reference integrity,
   StrongMods engine conformance (`foreach` / `ensure`), mod decision logic run headlessly, and
   repo/build conventions.

2. **"All projects are listed in `StrongMods.sln`" is not literally true.** Three are not:
   `build/GameAssemblies.csproj` (restore-only, and AGENTS.md itself describes it that way elsewhere),
   `Tests/Stubs/UnityStub.csproj` and `Tests/FunctionMod/FunctionMod.csproj` (both built through
   `ProjectReference` from `Tests`, so a solution build does build them). The rule as *intended* holds; the
   sentence as written does not. Wording, low priority.

3. **"`<IsDeployable>false</IsDeployable>` … (both templates; `Tests`)" undercounts by one.**
   `Tests/FunctionMod/FunctionMod.csproj` sets it too. Arguably "Tests" covers the family; a parenthetical
   "(both templates; the `Tests` projects)" would settle it.

4. **"A monorepo of ~25 mods".** 28 directories carry a `ModInfo.xml`, excluding the two templates. Harmless
   as an approximation, worth a nudge if the number is being used to reason about scale.

### Not checked

Everything about deploy behavior, overlay semantics, CI, and the publishing tools. Those claims need a
`-t:Deploy` run or a network round trip, and an unattended session should do neither. #55 remains open for
them.

## What I would do with all this, if it were mine to decide

- Update **#50** with the two corrections, since the inventory is the artifact other work reads from.
- Add the StrongholdTweaks case to **#61** — it is the first concrete, measured instance of the gap.
- Treat **#70** as ready to write: part 2 above is the evidence, and the six-area framing is a draft.
- Leave **#58** open until you decide whether `loclint.cs` should become a test that CI runs, or stay a tool
  a human invokes. That is a real design choice — the reference and transpiler checks became tests because
  they need the game tree the suite already resolves; the localization linter needs only files, so it could
  be either.

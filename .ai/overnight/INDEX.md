# Overnight session index — 2026-08-17

Unattended run. Every artifact below is **uncommitted**; the human reviews and commits.
The morning summary of all of it is published as an HTML artifact, "Night Run 2026-08-17".
Suite state at the last checkpoint: `dotnet build StrongMods.sln -c Debug` and
`dotnet test StrongMods.sln -c Debug` both green.

| Report | What it built or found |
|--------|------------------------|
| [strongutils-unit-tests.md](strongutils-unit-tests.md) | New `ModLogicHost` fixture + 123 real tests for `ConfigManager`, `PlayerDamage`, `ServerLifecycleCommands`, `XmlKeyValueStore`, `StrongZone` + the zone diff. Found that a `buff`-tagged zone declared in XML is silently inert. Measured that the #50 "expand the Unity stub" plan does not scale (225 missing CoreModule types) and split gap U1 into two universes instead. |
| [kvstore-culture-bug.md](kvstore-culture-bug.md) | **Measured defect:** `XmlKeyValueStore` writes floats in the current culture — `3.5f` stored under `de-DE` reads back as `35` under `en-US`. Dormant feature; cheap to fix now. Plus two smaller source-read issues in the same file. |
| [localization-linter.md](localization-linter.md) | New `StrongDev/.ai/tools/loclint.cs` (#50 gap S4), rules read from the game's `Localization.loadCsv` IL. Ran it repo-wide: **9 unresolved-key errors in 3 mods** (2 are #88, re-found as a positive control; 5 new in PlayerSpawnedTraders, 2 new in StrongUtils) plus a stray-comma row in AECInternationalMarketFixes that blanks an item name. Negative result: the header inconsistency across 10 files is cosmetic, not functional. |
| [xml-reference-integrity.md](xml-reference-integrity.md) | New `ReferenceGraph` fixture + `ReferenceIntegrityTests` (#50 gap S3): 16,821 cross-references resolved per version, **differential against vanilla's own 12 dangling refs**. Found 12 dangling recipe ingredients in StrongholdTweaks (`planted<Crop>1Sel2`, a suffix vanilla never uses), declared pending #61. |
| [transpiler-verification.md](transpiler-verification.md) | New `IlReader` + `TranspilerVerification` + `TranspilerTests` (#50 gap S1): all **13 transpilers run against their target's real IL** on both declared versions. Harmony's own IL reader is unusable here (MonoMod rejects CoreCLR 10), so the reader is BCL-only. Negative result: no transpiler has lost its match point. Two harness bugs recorded. |
| [issue-mapping-and-doc-rot.md](issue-mapping-and-doc-rot.md) | Where tonight's four harnesses land on **#58 / #41 / #61 / #50 / #88 / #57**, two corrections the #50 inventory needs, and a doc-rot pass over AGENTS.md's structural claims (five hold, four have drifted — the test-suite description is #70's core). Reported only; AGENTS.md is unedited. |

Running total of tests: 269 baseline → 403.

# Categorized attribute patches for CaseSensitiveFilesystem — plan (#44)

Convert the repo's only programmatic patch site — `CaseSensitiveFilesystem.ApplyExistsPatches()` — to ordinary
`[HarmonyPatch]` classes in a config-gated category, per the #44 hypothesis (owner: the programmatic mechanism
may date to a need that predates an earlier refactoring). If this lands, the `[PatchTargetManifest]` seam stays
as a guardrail for future programmatic patching, and #44's `Require()`-generalization branch is moot.

## 1. Established facts (investigated 2026-08-01)

- **Category precedent exists**: `[HarmonyPatchCategory]` + config-gated `harmony.PatchCategory(...)` is used by
  `BreadthFirstXmlPatcher`, `ServerOnlyClass`, and `ModUnloader`. **Shared-implementation patches do not exist
  yet** (owner-confirmed): four patch classes delegating to one transpiler body is a repo first.
- **`MethodType.Enumerator` is exactly equivalent to the hand-rolled scan.** Proven headlessly on both units
  (`.scratch/spike-headless-load/enumerator-check.cs`): `AccessTools.EnumeratorMoveNext` returns the *identical*
  `MethodInfo` as `GetMoveNext`'s `<name>d__` pattern scan — `<LoadUiAtlases>d__18.MoveNext` and
  `<LoadLocalizations>d__19.MoveNext`.
- **New thin-Harmony gap**: executing `AccessTools.EnumeratorMoveNext` on CoreCLR required `MonoMod.Utils`
  (25.0.6), which the game does not ship — same family as the `MonoMod.Backports` gap (test-harness-plan.md R2).
  Consequences split by runtime: the `Tests` resolver needs the package (its Enumerator path executes for real
  once these patches exist); in-game behavior is the plan's one open risk (R1 below).

## 2. Design

**D1 — Four patch classes**, nested in `CaseSensitiveFilesystem`, each `[HarmonyPatchCategory("CaseSensitiveFilesystem")]`:

| Target | Shape |
| --- | --- |
| `Localization.LoadPatchDictionaries` | `MethodType.Normal` |
| `ModManager.LoadUiAtlases` | `MethodType.Enumerator` |
| `ModManager.LoadLocalizations` | `MethodType.Enumerator` |
| `XmlPatchMethods.Include` | `MethodType.Normal` |

Each class carries a one-line `[HarmonyTranspiler]` method delegating to the existing
`ReplaceFileOrDirectoryExists` — the shared-impl pattern's first appearance, which is the point: reuse survives
the conversion.

**D2 — Wiring**: `ModApi.InitCaseSensitiveFilesystem` replaces `ApplyExistsPatches(harmony)` with
`harmony.PatchCategory("CaseSensitiveFilesystem")`, staying behind `Config.CaseSensitiveFilesystemEnabled`
exactly like the three precedents. `ModUnloader.Init` and `ValidateModInfos` are untouched.

**D3 — Deletions**: `ApplyExistsPatches`, `ExistsPatchTargets`, `Require`, and `GetMoveNext` all go. Behavior
notes: a missing target now fails at `PatchCategory` time with Harmony's own error instead of our `Require`
throw (still loud, still at init — and now also caught earlier by the smoke tests); the per-target
`[CaseSensitiveFilesystem] Replacing Exists() calls in ...` log lines disappear (the transpiler still throws if
a body contains no `Exists` calls, so silent no-op patching remains impossible).

**D4 — Manifest mechanism stays, pointers update**: `PatchTargetManifestAttribute` and the conformance test
remain as the guardrail for future programmatic patching. Its doc comment loses the
`ExistsPatchTargets` reference-implementation pointer (the inline example carries the pattern alone), and the
StrongMods README section notes the repo currently has no programmatic patch site — which is the desired state.

**D5 — Tests fallout** (same iteration; consequential, not bundled scope):
  - Add the `MonoMod.Utils` PackageReference — the resolver's `MethodType.Enumerator` path executes for real now.
  - Coverage sanity drops the "at least one manifest" assertion: zero manifests is now the *correct* state.
  - `Manifest_targets_resolve` becomes a `[Fact]` iterating all discovered manifests — xunit fails a `[Theory]`
    whose `MemberData` is empty, and empty is now legitimate.

**D6 — Version**: behavior-preserving by intent; `ModInfo.xml` bump deferred to the owner's release batching, as
with iteration 1's error-semantics change.

## 3. Verification

1. `dotnet build StrongMods/StrongMods.csproj -c Debug` — clean.
2. Full suite against all three trees (live, `vendor/game/V3.1.0-b14`, `vendor/dedicated-server/V3.1.0-b14`):
   the four new patch classes appear as resolution test cases, the two Enumerator specs exercising the
   resolver's `EnumeratorMoveNext` path for the first time. Resolved targets must be the same four members the
   manifest yielded (the Enumerator equivalence is already proven; the Normal targets are `nameof`-checked at
   compile time).
3. Conformance test stays green with zero programmatic call sites; coverage sanity green with zero manifests.
4. **In-game check (owner, closes R1)**: deploy to a test instance and confirm startup is clean — specifically
   that `PatchCategory("CaseSensitiveFilesystem")` succeeds and case-sensitivity enforcement still fires (e.g.
   the ModInfo casing validation path). This is the only step that can prove Mono executes
   `EnumeratorMoveNext` without `MonoMod.Utils` present.

## 4. Risks

- **R1 — In-game `MonoMod.Utils` dependency.** On CoreCLR the JIT eagerly resolved `MonoMod.Utils` for
  `EnumeratorMoveNext`; Mono resolves lazily, so the game likely never touches it — but only step 3.4 can prove
  that. If it fails: options are shipping `MonoMod.Utils.dll` beside `StrongMods.dll` (mod-folder probing), or
  reverting to the programmatic site + manifest (the mechanism is retained either way). Failure mode is loud —
  mod init throws at startup.
- **R2 — Parameter-name binding.** The transpiler takes only `IEnumerable<CodeInstruction>`, so no `__`-injection
  or parameter-name concerns apply; listed to record it was considered.

## 5. Size estimate

Net negative in StrongMods: delete ~55 lines (the four methods), add ~35 (four small classes), 1 line in
`ModApi`. Tests: ~25 lines across csproj and `SmokeTests.cs`. Docs: ~10 (attribute doc comment, StrongMods
README). Total churn well under the 100-line target.

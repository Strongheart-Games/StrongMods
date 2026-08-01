# Test harness + Harmony patch-target smoke test — design plan (#14)

Design for [#14](https://github.com/Strongheart-Games/StrongMods/issues/14): stand up the repo's first test
project and land exactly one test on it — the Harmony patch-target smoke test. Non-goals, tracked elsewhere:
deploy-shape verification in CI (#42) and behavioral/`<foreach>` tests (#43, which builds on this harness).

## 1. Established facts

The spike (results on #14, 2026-08-01; code in `.scratch/spike-headless-load/spike.cs`) proved on Windows
.NET 10: `Assembly-CSharp` loads headlessly with 100% of types reflectable on all three vendored trees; 0Harmony's
net4x code executes on modern .NET; 59/59 patch specs across all 18 built mod DLLs resolve against both units; a
bogus target correctly fails. From the repo: CI (`.github/workflows/build.yml`) is a Debug × {game,
dedicated-server} matrix on **ubuntu**, restores **both** package trees into `.scratch/game-packages/` in every
leg, locates the leg's tree, and builds the solution with `-p:SdtdDir=<tree>`; line 82 reserves the `dotnet test`
slot.

One patch site is invisible to attribute enumeration: `CaseSensitiveFilesystem.ApplyExistsPatches()` (the repo's
only programmatic `harmony.Patch(...)` call) resolves four targets at runtime — two via `AccessTools.Method` +
`nameof` (compile-checked against the *build* assemblies, but not across version skew) and two via a name-pattern
scan for compiler-generated `MoveNext` types, which no compile check covers. It currently skips a null target
silently; the owner confirmed (2026-08-01) that is unintentional — every target is expected present, and a miss
should error.

## 2. Design decisions

**D1 — Runtime & TFM: `net10.0` on CoreCLR.** The spike ran there; no net481 runner or `FrameworkPathOverride`
needed. The test project is tooling, not a mod, so the net481/LangVersion 9 rules don't apply to it.

**D2 — Framework: xunit** (current stable v2 line + `Microsoft.NET.Test.Sdk`), the plainest `dotnet test`
integration. First NuGet packages in the repo beyond Cronos; fetched from nuget.org like Cronos is.

**D3 — Project: `Tests/`**, a solution member. (Renamed from the originally planned `StrongMods.Tests` during
iteration-2 review: that name collided with the StrongMods core mod, and this project's vision is the single
home for every runner-based test in the repo — including, later, #43's per-mod behavioral suites — so it is
named for repo scope. Build-time lint, workflow checks, and tool selftests stay where they are.) It imports
**none** of the `build/` entry points (it is
not a mod: nothing stages, nothing deploys, XML lint doesn't apply). Two consequences handled explicitly:
  - It declares an empty `<Target Name="Deploy" />` so solution-scope `-t:Deploy` still works (every other
    project gets `Deploy` from its entry point; a solution-level target invocation fails on a project lacking it).
  - `CLAUDE.md`/`README.md` get a line each: top-level directories are mods *except* `build`, `Template*`,
    `packages`, and `Tests`.

**D4 — Path acquisition: reuse the build's knobs, embed at build time.** `Tests.csproj` imports
`build/GamePaths.props` (a new third importer, same pattern as the entry points) and embeds `SdtdManagedDir`,
`SdtdHarmonyDir`, `Configuration`, and the repo root as `<AssemblyMetadata>`; tests read them via
`AssemblyMetadataAttribute`. **The unit under test is whatever `$(SdtdDir)` resolves to** — default live game,
`-p:SdtdDir=` for the server or a vendored tree, `Local.props`/`SDTD_HOME` honored, layout auto-detected by
`GamePaths.props` exactly as builds do. One unit per test run; both units are covered by running twice (CI does
this via its existing matrix; locally it's one extra `-p:SdtdDir=` invocation). No new configuration surface.

**D5 — Mod enumeration: derived from the repo, not hardcoded.** Scan `*/[name].csproj` for projects importing
`Mod.props` (code mods), then require `[name]/bin/$(Configuration)/[name].dll` to exist — a missing DLL **fails**
with "build the solution first" rather than silently shrinking coverage (no silent caps). `dotnet test
StrongMods.sln` builds all mods first by construction once the project is a solution member.

**D6 — Runtime architecture: one `AssemblyLoadContext` for the unit's assemblies + mod DLLs, with 0Harmony
shared.** The custom ALC resolves from `$(SdtdManagedDir)` and the mod `bin` dirs but defers `0Harmony` to the
default ALC (loaded once from `$(SdtdHarmonyDir)`), so `[HarmonyPatch]` attribute instances created inside the
ALC share type identity with the test code, which compile-time references `$(SdtdHarmonyDir)\0Harmony.dll` (raw
`<Reference>`; raw references are not TFM-checked). Typed `HarmonyLib` access throughout — no
reflection-on-Harmony gymnastics.

**D7 — Resolution fidelity: Harmony's own machinery, thin glue only.** Per patch class:
`HarmonyMethodExtensions.GetFromType` / `GetFromMethod` + `HarmonyMethod.Merge` for targeting info (replacing the
spike's approximate merge), then a small `methodType → AccessTools` mapping (`Method`, `DeclaredMethod`,
property getter/setter, constructor, `EnumeratorMoveNext` for `MethodType.Enumerator`). `TargetMethod(s)`
providers are invoked (they execute mod code — by design; they must work headlessly for the test to mean
anything). The mapping asserts it covers every `MethodType` actually present, so a patch shape the resolver
doesn't understand is a test failure, not a skip.

**D8 — The tests (wave 1, complete list):**
  1. *Patch targets resolve* — one test case per patch class per mod: every merged spec resolves against the
     unit under test.
  2. *Dynamic patch targets resolve* — every `[PatchTargetManifest]` method (D9) is invoked and fully
     enumerated (manifests are lazy `yield` methods — throws surface at enumeration, not invocation, per the
     iteration-1 red-path run), and every `MethodBase` it yields must be non-null.
  3. *Coverage sanity* — every enumerated code-mod DLL loads, total patch-spec count is > 0, and at least one
     manifest was found (guards against a refactor making either half of the suite vacuously green).
  4. *Negative control, permanent* — a patch class defined inside the test assembly targeting a nonexistent
     member must fail resolution. The suite carries its own proof that it can fail.
  5. *Manifest conformance* — scan mod project sources for direct `harmony.Patch(` calls; a project with any
     must declare at least one `[PatchTargetManifest]`. The failure message is the teaching moment — it names
     the pattern, the attribute, and the doc to read — so a future developer who rolls a new programmatic
     patcher without publishing targets is interrupted by CI with instructions, not by archaeology (owner
     scenario, 2026-08-01). Source-level regex is enough for wave 1; IL-level scanning is the stricter future
     upgrade if ever needed.

**D9 — Programmatic patches: a patch-target manifest convention.** Attribute enumeration cannot see dynamic
`harmony.Patch(...)` calls, so code that patches programmatically must publish its targets through a testable
seam: a `public static IEnumerable<MethodBase>` method tagged `[PatchTargetManifest]` (a new marker attribute
in StrongMods, beside `XmlPatchFunctionAttribute`), pure resolution with no patching side effects. Named to
avoid fuzzy collision with Harmony's own `[HarmonyTargetMethod]`/`TargetMethod()` lifecycle hooks — because
unlike those, **the attribute is inert: nothing invokes a tagged method automatically**. The patching code
calls its own manifest; the test suite discovers manifests by attribute and invokes them headlessly. That
inversion of the Harmony prior is the main confusion risk, so it is mitigated in three layers: the attribute's
doc comment leads with the lifecycle ("tagging patches nothing") and embeds a usage example; a "Programmatic
Harmony patches" docs section (StrongMods README or `Docs/`, part of iteration 2) carries the narrative; and
the conformance test (D8 test 5) enforces the pattern with a failure message that teaches it. The test
assembly discovers and invokes every tagged provider across all mod DLLs and asserts each returned target
resolves (test 2 above). `ApplyExistsPatches` is refactored to consume its own provider and — per the owner's
call — to **error on a null target instead of silently continuing** (that skip was unintentional; the fix rides
in the same change since the loop is being touched anyway). This refactor is a **separate small StrongMods
change** from the test project itself (no bundling; sequenced immediately before the harness lands so wave 1
tests it).

**D10 — Failure diagnostics are a design deliverable, not an afterthought.** The consumer scenario (owner,
2026-08-01, grounded in the real 2.6→3.0 `Server.Play` signature change): a mod developed against version X is
tested against secondary version Y, a target misses on Y only, and the developer is staring at an IDE — wired to
X's assemblies — where the member plainly exists. A bare "not found" is actively confusing there. Discovery must
be easy even when the fix is hard. Therefore every resolution failure reports:
  - **Version identity, prominently**: the tree path plus a human version label — `manifest.json`'s label for
    vendored trees, `Assembly-CSharp` file/product version for live installs — and the unit layout.
  - **The full spec sought**: declaring type, member name, argument types, `MethodType`.
  - **Tiered near-miss diagnosis**: declaring type missing → say so and list loaded types with the same simple
    name (catches moves/renames); type present → list the members that *do* exist under the requested name with
    full signatures beside the requested one (makes a signature change self-evident: "`Play(string, int)` not
    found; `Server` has `Play(int)`, `Play(string, bool)`").
  Out of scope but not precluded: distinguishing "broken on primary" from "unsupported on secondary" is a
  per-mod support declaration, which is #37/#23 territory; the output format just needs to leave room for an
  expected-unsupported annotation later. #23 is this harness's future consumer — "test against Y/Z" becomes
  "invoke `dotnet test` once per pinned tree" — and gets a comment saying so when this lands.

**D11 — CI: fill the reserved slot.** In each matrix leg, after the solution build:
`dotnet test Tests/Tests.csproj -c Debug -p:SdtdDir=<leg tree>` (same property the build
step passes, so embedded metadata matches the leg). Standing safety rules unchanged: no artifacts uploaded, no
`-t:Deploy`; test output prints resolution results, never ships game bytes.

## 3. Verification plan

1. The D9 seam change verifies on its own first: `dotnet build StrongMods/StrongMods.csproj -c Debug`, plus a
   quick red-path check that a null target now errors (temporarily point the provider at a bogus member).
2. Local: `dotnet test StrongMods.sln -c Debug` (game unit) — all green, negative control red-path confirmed by
   temporarily breaking it.
3. Local: `dotnet test ... -p:SdtdDir=vendor/dedicated-server/V3.1.0-b14` and `-p:SdtdDir=vendor/game/V3.1.0-b14`
   — proves vendored-tree operation, which is what CI does.
4. **Diagnostics rehearsal (D10 acceptance)**: author a synthetic StrongHonk-style patch class in the test
   assembly targeting a member that exists in V3.1.0 but not V3.0.1, run against `vendor/game/V3.1.0-b14`
   (passes) and `vendor/game/V3.0.1-b4` (fails), and judge the failure output by the D10 criterion: the message
   alone — version identity plus near-miss signatures — must make the discovery easy. Iterate until it does;
   capture the final output in the handoff summary so the human judges it too.
5. Evaluation diff (`compare-eval`) on one existing mod project against `HEAD` — proves adding the test project
   and solution entry changed nothing about how mods build.
6. CI run on the branch — both legs green; this is also the Linux proof (risk R1).

## 4. Risks and open questions

- **R1 — Linux load is unproven.** The spike ran on Windows; CI is ubuntu. Expected fine (pure managed
  reflection, no Unity native code executes), but the first CI run is the proof. Mitigation if wrong: none
  cheap — would force a windows-runner or per-OS conditional, so run CI early in implementation.
- **R2 — Harmony public-API assumptions.** D7 names specific public members (`GetFromType`, `Merge`,
  `EnumeratorMoveNext`); if the game's 0Harmony build differs from stock Harmony 2.x surface, the glue adjusts.
  First implementation step is compiling against the real `0Harmony.dll`, which settles it immediately.
  *Partially settled by iteration 1 (2026-08-01):* the game ships a **thin Harmony 2.13** build referencing
  `MonoMod.Backports` 1.1.2 externally without shipping it (in-game Mono never resolves the reference lazily;
  CoreCLR resolves it eagerly when JITting `AccessTools`), so the test project needs a `MonoMod.Backports`
  `PackageReference`. Executing 0Harmony resolution code on .NET 10 otherwise works, proven by the headless
  manifest runs against both units. More MonoMod pieces may surface only if the harness ever executes actual
  patching, which wave 1 does not.
- **R3 — Version skew between mod DLLs and the unit under test** (mods built against X, resolved against Y) is a
  *feature* here — it's exactly the game-update check — but locally it can surprise: a red test after a game
  update is the tool working. README section in the test project will say so.
- **Open: xunit v2 vs v3.** Plan says v2 (boring, proven). Cheap to revisit at implementation time.

## 5. Size estimate

Two changes, sequenced:

1. **StrongMods seam (D9)**: `PatchTargetManifestAttribute` (~35 lines with its lifecycle-first doc comment and
   embedded example) + refactoring `ApplyExistsPatches` onto a manifest with null-as-error (~25 lines changed).
   Well under the 100-line target; its own iteration.
2. **Test project**: csproj + ~5 source files, roughly 400 lines total (new files, not edits; D10's diagnostic
   formatting and the D8 conformance test are most of the growth over the earlier estimate), plus the short
   "Programmatic Harmony patches" docs section (D9). Existing-file
   changes are small: `StrongMods.sln` entry, ~6 lines in `build.yml`, a line each in `CLAUDE.md` and
   `README.md`. Exceeds the 100-line target — hence this plan and the explicit-approval gate before
   implementation.

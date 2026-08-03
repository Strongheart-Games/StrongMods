# foreach spec conformance + patcher tests — design plan (#43)

Second-wave suite on the #14 harness: clause-by-clause conformance tests for the `<foreach>` engine against
`StrongMods/Docs/foreach.md` (476 lines, the complete spec), plus breadth-first patcher coverage, plus the
patch-application stretch goal. This plan phases the work into gated iterations; it does not design individual
tests — the spec is the test inventory.

## 1. Established facts

- The engine entry is `XmlPatchMethodForeach.Foreach(XmlFile targetFile, …, XmlFile patchFile, Mod patchingMod)`
  (registered via `XmlPatcher.addXmlFilePatchMethod`). Tests drive it directly with crafted XML and assert on
  the mutated `targetFile` document — no game loop involved.
- The engine makes **38 direct `Log.*` calls** (skip-warnings, foreach-failure errors — themselves spec'd
  behavior), and body commands route through `XmlPatcher.singlePatch`. Cross-file `source=` resolution calls
  `BreadthFirstXmlPatcher.TryGetPatchedFile`, so the two #43 scope areas share fixtures naturally.
- Tier map (`Tests/README.md`): the engine is tier-1/tier-2 — pure XML logic behind two game-type touchpoints
  (`XmlFile`/`Mod` construction, `Log`).

## 2. Known unknowns — ANSWERED by the spikes (2026-08-01, `.scratch/spike-foreach/`)

- **S1 — Log**: `Assembly-CSharp` has **no `Log` type** — the `Log` everything uses lives in **`LogLibrary.dll`**
  (early "reflection hostility" observations were a spike bug: reflecting on the null from
  `acs.GetType("Log")`). Its type initializer registers with `UnityEngine.Application.logMessageReceivedThreaded`
  — a Unity engine-internal call — so it **throws headlessly and poisons the type**; `XmlPatcher`'s initializer
  dies by cascade (it logs during its type scan). **The seam, proven end to end: a stub
  `UnityEngine.CoreModule`** served by the conformance-test load context in place of the real one (resolution-
  level substitution — no patching anywhere). With ~12 stub members (`Application` events/props with nested
  `LogCallback`, `LogType`, the `Object`→`MonoBehaviour` hierarchy, `TextAsset`, `StackTraceUtility`, `Debug`),
  `Log` initializes and works fully: `Out`/`Warning`/`Error` execute, `AddOutputPath(file)` gives assertable
  capture, and the `LogCallbacks` event allows typed in-memory capture (the *stub* owns the delegate type, so
  fixtures can subscribe compile-time).
- **S1b — Harmony patch application on .NET 10: recorded 2026-08-01 as NOT viable; OVERTURNED 2026-08-03.**
  The owner challenged the record from MonoMod's own NuGet listing, and a rerun
  (`.scratch/spike-harmony-net10/`, disposable — this entry is the durable record) proved patching works. The
  original conclusion — "MonoMod v25 ships no net10.0 runtime assets; its packages hard-refuse the TFM at
  build time; the game's net4x `RuntimeDetour` fails to load; Harmony's initializer dies" — decomposes into
  two version artifacts, neither a platform limit:
  - *Build-time:* MonoMod **25.1.0**'s target-runtime check refused net10.0 (the spike bypassed it with
    `MonoMod_ReallySkipCheckTargetRuntime`); **25.3.6** accepts net10.0 with no bypass.
  - *Runtime (reconstructed from the symptoms):* the spike pinned `MonoMod.RuntimeDetour` **25.1.0**, below
    the **25.1.2.0** floor the game's 0Harmony 2.13 references. CoreCLR refuses a lower-version bind, the
    spike's resolution hook then served the game's net4x `MonoMod.RuntimeDetour.dll` as fallback, and that
    flavor genuinely cannot execute on CoreCLR — every recorded symptom, downstream of one version pin.
  The rerun used the harness's exact topology (stub CoreModule + game assemblies in a custom ALC, the game's
  own 0Harmony typed in the default context, NuGet `MonoMod.RuntimeDetour` 25.3.6): the game's 0Harmony
  applied a prefix to `XmlFile.GetXpathResultsInList`, a real `singlePatch` dispatch of a marked xpath landed
  in the prefix, and unmarked xpaths fell through to the original evaluator. Scope of proof: Windows, live
  game install (V3.1.0-b14 line), .NET 10.0.302; the first ubuntu CI run of a detour-based test is the Linux
  proof. Consequence: **runtime detours are available to this suite.** Reference `MonoMod.RuntimeDetour` at
  ≥ 25.3.6 and ≥ the version the unit's 0Harmony asks for; a future game Harmony bump above the pin fails
  loudly (`FileLoadException`), fixed by bumping the pin.
- **S2 — construction is trivial**: `XmlFile` has an in-memory ctor `(string text, string dir, string filename,
  bool throwExc)` and its document is the public **field** `XmlDoc` (`XDocument`); `Mod` has a parameterless
  ctor with a settable `Name`.
- **R3 — resolved positive**: `XmlPatcher.singlePatch` ran end to end in the stub room — a crafted `<set>`
  applied correctly (`result=True`, document mutated, patch-trace comment inserted). One wrinkle: `XmlPatcher`'s
  initializer populates its patch-method registry via a type scan that bails on the stub room's partial type
  loads, so **fixtures register the vanilla methods manually** through the public
  `addXmlFilePatchMethod(string, MethodInfo, bool)`, reading each `[XmlPatchMethod]` on `XmlPatchMethods` —
  proven, 11 methods registered.

## 2b. Host-runtime decision spike (2026-08-01, owner-requested): net481 vs .NET 10 — STAY ON .NET 10

The owner asked whether retargeting the test project to net481 would unlock more (chiefly: real Harmony
patching, hence behavioral patch tests). The spike (`.scratch/spike-net4x/`) proved four things and then
answered the question with a fifth:

| Capability | .NET 10 / CoreCLR | net481 / desktop CLR |
| --- | --- | --- |
| `dotnet test` toolchain | yes (any OS — ubuntu CI) | yes (Windows only — proven locally) |
| Game assemblies load/reflect | 7558/7558 | partial in stub room; loadable |
| **Game code EXECUTES** | **reliably** — CoreCLR's API surface is a superset of Unity's Mono profile (all of §2's results) | **unreliably** — desktop 4.8 is a SUBSET of Unity's Mono: the first game path exercised (`XmlPatcher.addXmlFilePatchMethod`) died on `Dictionary.TryAdd`, an API Unity's Mono has and desktop lacks; unfixable, it is the game's code |
| **Harmony patch application** | **yes — since the 2026-08-03 S1b correction** (MonoMod ≥ 25.3.6; the "no" recorded here on 2026-08-01 was a 25.1.0 artifact) | **yes — proven**: game 0Harmony + vendored net4x MonoMod applied a prefix AND StrongMods' real `ReplaceFileOrDirectoryExists` transpiler to the real `Localization.LoadPatchDictionaries` |

Neither host does both *(2026-08-01 finding — no longer true, see the note below)*; the game's own runtime
(Unity Mono) is the only place both work, and that is in-game territory. **Decision: the suite stays on
.NET 10**, because reliable game-code *execution* is the foundation the conformance suite stands on, while
the patching unlock is itself hobbled on desktop CLR — executing a patched game method hits the same subset
walls. The unlock is not lost, just parked: (a) a future, separate net481 test project could host *apply-only*
patch checks (transpiler applies cleanly without executing the target — exactly what the spike proved); (b)
the day MonoMod ships net10 support, CoreCLR gets patching too and everything unifies. *(2026-08-03: (b)
arrived — MonoMod 25.3.6, see the S1b correction. .NET 10 now does both, the stay-on-.NET-10 decision stands
stronger than when it was made, and the parked net481 apply-only idea is obsolete.)* Nuggets recorded for
that future: on net4x, `LoadFrom`-context same-directory probing
preempts resolution hooks — preload the stub so simple-name binding wins; and `FrameworkPathOverride`
compilation lets test-own code silently bind Mono-only overloads that explode on desktop
(`string.Split(char, StringSplitOptions)` bit within minutes).

## 3. Design decisions

**D1 (rewritten post-spike) — The seam is a stub `UnityEngine.CoreModule`, not patching.** A small checked-in
stub project (our own clean-room code; it contains no Unity IP, only empty shapes) builds an assembly named
`UnityEngine.CoreModule.dll`; the conformance fixtures' load context serves it in place of the real one, which
makes `LogLibrary`'s `Log` initialize and run headlessly — captured via the `LogCallbacks` event (typed: the
stub owns the delegate) and/or `AddOutputPath`. The smoke tests' load context keeps resolving the *real*
CoreModule and is untouched; the two rooms coexist. Fixture setup also performs the manual vanilla
patch-method registration (§2 R3). Harmony patching plays no role in these waves — a design choice that
stands on its own; the "cannot on .NET 10" rationale this sentence originally cited was overturned 2026-08-03
(see the §2 S1b correction), so patching is an available tool for future waves, not a blocked one.
Note the earlier claim that wave D needed patching capability was wrong: wave D applies *XML* patches via
`XmlPatcher`, which the spike already proved working.

**D2 — Test organization: folders per area in `Tests/`** — `Foreach/` (conformance), `Patcher/` (breadth-first),
`Fixtures/` (shared XML builders, the Log capture, engine invocation plumbing). Namespaces follow folders.
Wave-1 smoke tests stay where they are, untouched.

**D3 — Conformance style: spec-section theories over golden fixtures.** Each spec section becomes a test class;
each documented behavior (including every skip-with-warning and fail-the-whole-foreach case in "When it doesn't
work", "Gotchas", and the Reference tables) becomes a case: input document + patch element in, expected document
+ expected log verdicts out. Case names cite the spec section so a failure reads as "spec §X violated".
**Divergence rule (from the issue): a mismatch between spec and implementation is a finding — fix whichever is
wrong, never silently encode the bug in the test.** Divergences get raised to the owner before the test lands.

**D4 — Patcher coverage: the cache and ordering seams, not the Harmony wiring.** `TryGetPatchedFile`,
mod-major ordering, consumed-entry removal, and the outside-the-pipeline fall-through are all static-level
behavior exercisable headlessly; the two `[HarmonyPatch]` prefixes stay covered by the wave-1 resolution tests.

**D5 — Wave D (stretch, own go/no-go): patch-application over real `Config/` — exploratory, findings
expected.** Apply each mod's actual patch files against the unit's vanilla XMLs via the real `XmlPatcher` +
engine. **"Applies cleanly against vanilla" is NOT a universal expectation** (owner, 2026-08-01): some mods
target other mods' XML — ProjectZFixes patches ProjectZ, not vanilla — and the conditional guards for the
missing-dependency case are not applied consistently. So the first pass is deliberately exploratory: run
everything against vanilla, catalogue what fails, and triage each finding through the owner's three questions —
*should the mod fail more gracefully? should the test's expectation change? or should tests be added* (e.g.
assert the mod no-ops on vanilla, plus applies-without-warning against a fixture base the mod provides, plus
possibly vendoring the real dependency mods for testing)? The end state is per-mod expectations (applies-cleanly
/ no-ops-on-vanilla / needs-fixture-base), declared, not inferred. Depends on D1's patching capability and S2;
runtime complement to #41's structural lint. Explicitly cut to its own issue if the earlier waves surface
enough friction or the findings backlog deserves its own tracking.

**D8 (added during wave D) — entry points are read from IL, not from the loaded type.** The set of patchable
documents is `WorldStaticData.xmlsToLoad`, and *touching* it headlessly is impossible: the type's initializer
pulls in Unity engine types (`Color`, then `Vector2`, then `Unity.Profiling.ProfilerMarker` — an unbounded
chase). `Tests\Fixtures\EntryPoints.cs` reads the names out of the `.cctor`'s IL with Mono.Cecil instead: one
`newobj XmlLoadInfo(string _xmlName, …)` per entry, first `ldstr` in each run is the name. No execution, no
stub growth, and version-accurate for whichever unit is under test — which matters, because the list changes
between game versions (`sandbox_overrides` arrived in 3.0). This is the counter-example to D1's stubbing
approach: stub when a *few* engine touchpoints block otherwise-pure code, inspect when the initializer is the
engine.

**D9 (added during wave D) — patch-application expectations are declared with reasons, and checked in both
directions.** The default is that a mod's patch applies to vanilla with no error or warning; any exception is
declared in `PatchApplicationTests` with a reason and an issue number. The tests then assert **both** that
nothing undeclared logs *and* that every declaration is still earning its place — a declared exception that
goes quiet, or whose patch no longer exists, fails. Without the second half the list becomes a suppression
file that silently outlives its reasons; with it, landing #60 or fixing #62 makes the suite tell you to delete
the entry.

**D6 (added after wave B3, from #51) — fixture projects must order their own dependencies, and a test says so.**
A helper assembly under `Tests\` that compiles against a repo project via `HintPath` into its `bin\` has nothing
in the build graph ordering that project first: it passes on any machine where the sibling was ever built and
fails on a clean checkout (reproduced: 5× `CS0246`). The fix is a `ProjectReference` carrying
`ReferenceOutputAssembly="false"` (orders without changing what is compiled) plus
`SkipGetTargetFrameworkProperties="true"` (skips the TFM check — needed here because the fixture is
netstandard2.0 and StrongMods is net481). Documentation alone would not hold, because the trap is inherited by
copy-paste, so `Tests\ProjectConventionTests.cs` scans every repo `.csproj` and fails with the fix inline —
the same enforce-don't-document choice as #44's manifest conformance test. The full fixture-project checklist
lives in `Tests/README.md`.

**D7 (added after wave B3) — diagnostic probes must not leave a failing suite behind.** Reading the engine's
real messages before asserting on them is what keeps this suite's assertions non-vacuous, and every wave has
used it. But the wave-B3 probe (`_Dump`, an `Assert.True(false, …)` dumping messages) sat in the working tree
long enough for a parallel session to see a red suite on a green SHA (#51's first finding). The technique
stays; the red does not. Probes write their output to `.scratch\` and pass, or live in a scratch runner —
never an intentionally failing test in `Tests\`.

## 4. Iterations (each gated, each ≤ the size limits)

1. **Spikes S1+S2** (scratch, no tracked changes) → results logged here.
2. **Wave A — plumbing + first conformance tests**: `Fixtures/` (engine invocation, XML builders, Log capture
   per D1), plus the "Writing a loop" + "Filling in values" sections as the first conformance classes. Proves
   the whole path; likely the largest single iteration (~250 lines, mostly fixtures).
3. **Wave B — the conformance matrix**: remaining spec sections in 2–3 iterations (bind tables, functions,
   dynamic names, error/skip semantics, gotchas). *Ran as B2 (functions) and B3 (failure modes and gotchas);
   with wave A2 that completes every section of the spec.*
4. **Wave B4 — fixture-project hygiene** (D6, D7): the FunctionMod ordering fix, the convention test that
   generalizes it, and the documentation. No new spec coverage; inserted after #51 surfaced the trap, because
   every later wave that adds a fixture would otherwise inherit it.
5. **Wave C — patcher tests** (D4).
6. **Wave D — go/no-go decision with the owner, then the exploratory pass and findings triage** (D5, D8, D9).
   Ran as: prerequisite (#59), exploratory survey, owner triage, then the conversion to declared-expectation
   tests. Findings
   that turn into mod-behavior changes (graceful-failure guards) are separate per-mod iterations or issues,
   never bundled into the test change.

## 4b. Results ledger

What each wave actually delivered, measured. Suite counts are cumulative and identical on the live install and
both `V3.1.0-b14` vendored units unless noted.

| Wave | Landed | Suite | Notes |
| --- | --- | --- | --- |
| Spikes | — | 68 | §2, §2b |
| A | `Stubs\`, `Fixtures\GameRoom`, first 9 conformance tests | 77 | Two assertions hardened after dumping the engine's real messages |
| A2 | 9 more clauses of the first two spec sections | 86 | Found that `xpath`'s presence is enforced by vanilla dispatch, not foreach |
| B | `<bind>` — 8 clauses | 94 | The resolved-once test needed the body to mutate the document to be meaningful |
| B2 | `<function>` — 10 clauses, plus `Tests\FunctionMod` | 104 | Positive tests needed a real mod assembly registered with `ModManager` |
| B3 | Failure modes and gotchas — 12 clauses | 116 | **Every section of `foreach.md` now covered: 48 clauses, zero spec/implementation divergences** |
| B4 | `ProjectConventionTests`, FunctionMod ordering fix | 117 | Surfaced #46's third failure mode as a side effect |
| C | `Fixtures\PatcherCache`, 6 cache/prefix tests, 3 cross-file tests | 126 | Coroutine itself stays out of scope — needs the game (#49) |
| D | `Fixtures\PatchPipeline`, `PatchApplicationTests` (4 tests) | 130 | See below |

### Wave D outcome

Of **52 mod patch files** across 18 mods, applied to the unit's real vanilla XML:

- **41 apply with no error or warning.**
- **9 log, all declared with reasons**: 6 target another mod's content (#61), 2 are the paired
  `setattribute`+`append` idiom (#60), 1 is a documented `<foreach>` skip.
- **1 is dead** — `StrongholdTweaks/Config/XUi/windows.xml` sits at a path matching no entry point, so the
  game never opens it and never complains (#62). Found by the survey, not by anyone noticing the UI tweak
  missing.

Two of my own conclusions were wrong before measurement corrected them, both worth remembering:

1. The two StrongholdTweaks warnings were first classified as **silent no-op bugs**. They are the documented
   paired idiom — `xml-patch-ensure-spec.md` §1 quotes one of them verbatim as its motivating example. The
   mods are correct; the language lacks the word. (Owner called this before I checked.)
2. The first survey derived entry points **from the filesystem** — "is there a vanilla file with this name?".
   Wrong: entry points come from `xmlsToLoad` and are path-qualified (`XUi_InGame/windows`). Under the correct
   model, the base-name "collision" I reported dissolves, `items_xmas_cooking.xml` is correctly not an entry
   point (its `<include>` is found), and the XUi file is revealed as genuinely dead rather than "unreliable".

The exploratory instrument (`_Survey.cs`) was deleted once its findings became the tests above; its last
report is in `.scratch\wave-d-survey.md` and its findings are in #60, #61 and #62.

### Prerequisite this wave forced

Patch application needs the game's `Data\Config`, which vendored trees did not carry — running it against a
live install would have been a **third live-install dependency and the first on the CI path**, giving up the
game-free property #15 and #48 established. #59 extended `vendor.cs` to capture `Data\Config` wholesale
(+33 MB per package, `Localization.csv` included: ten of our mods ship one). `SdtdConfigDir` joined
`build\GamePaths.props` as a first-class path. Wave D therefore runs in CI and across the version matrix,
which is where it earns its keep — vanilla XML restructuring on a game update is a top breakage class.

## 5. Verification

Per iteration: suite green against the live install + both `V3.1.0-b14` trees (3.0.1 trees stay smoke-only —
conformance asserts current-version behavior, not cross-version). CI green on push. For D3's divergence rule:
any spec/implementation mismatch is presented to the owner in the iteration's handoff, with the proposed
resolution direction, before the affected test is finalized.

## 6. Risks

- **R1 — resolved negative 2026-08-01; overturned 2026-08-03** (see the §2 S1b correction): Harmony patch
  application works on .NET 10 with `MonoMod.RuntimeDetour` ≥ 25.3.6. Waves A–D neither needed nor used it
  (the D1 seam), so nothing shipped changes; the capability is simply available now.
- **R2 — did not materialise.** All 48 spec clauses matched the implementation; not one divergence was found
  across every section of `foreach.md`. The reconciliation project this risk anticipated never started.
- **R3 — resolved positive** (see §2): `singlePatch` works end to end in the stub room with manual registry
  setup.
- **R4 — bounded in practice, with a limit found.** The stub stayed at its original ~12 members through every
  wave; no conformance test needed more. Wave D found where the approach stops paying: `WorldStaticData`'s
  initializer needs an unbounded chase of engine types, so that path is read from IL instead (D8). Rule of
  thumb: stub a few touchpoints, inspect an engine-shaped initializer.
- **R5 (new) — a live-install dependency on the CI path**, raised when wave D needed vanilla config. Resolved
  before it landed by extending the vendored trees (#59) rather than reaching for the install; see §4b.

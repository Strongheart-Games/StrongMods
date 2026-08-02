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
- **S1b — Harmony patch application on .NET 10 is NOT viable** (plan R1, resolved negative): MonoMod v25
  ships no net10.0 runtime assets — its packages hard-refuse the TFM at build time, the game's net4x
  `RuntimeDetour` fails to load, and Harmony's own initializer dies without it. Nothing in waves A–D needs it
  anymore (the seam above replaced D1's patching idea), but any future test wanting runtime detours is blocked
  until MonoMod ships net10 support.
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
| **Harmony patch application** | no (MonoMod v25 ships no net10 assets) | **yes — proven**: game 0Harmony + vendored net4x MonoMod applied a prefix AND StrongMods' real `ReplaceFileOrDirectoryExists` transpiler to the real `Localization.LoadPatchDictionaries` |

Neither host does both; the game's own runtime (Unity Mono) is the only place both work, and that is in-game
territory. **Decision: the suite stays on .NET 10**, because reliable game-code *execution* is the foundation
the conformance suite stands on, while the patching unlock is itself hobbled on desktop CLR — executing a
patched game method hits the same subset walls. The unlock is not lost, just parked: (a) a future, separate
net481 test project could host *apply-only* patch checks (transpiler applies cleanly without executing the
target — exactly what the spike proved); (b) the day MonoMod ships net10 support, CoreCLR gets patching too
and everything unifies. Nuggets recorded for that future: on net4x, `LoadFrom`-context same-directory probing
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
patch-method registration (§2 R3). Harmony patching plays no role — see §2 S1b for why it cannot on .NET 10.
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
6. **Wave D — go/no-go decision with the owner, then the exploratory pass and findings triage** (D5). Findings
   that turn into mod-behavior changes (graceful-failure guards) are separate per-mod iterations or issues,
   never bundled into the test change.

## 5. Verification

Per iteration: suite green against the live install + both `V3.1.0-b14` trees (3.0.1 trees stay smoke-only —
conformance asserts current-version behavior, not cross-version). CI green on push. For D3's divergence rule:
any spec/implementation mismatch is presented to the owner in the iteration's handoff, with the proposed
resolution direction, before the affected test is finalized.

## 6. Risks

- **R1 — resolved negative, consequence contained** (see §2 S1b): no Harmony patch application on .NET 10;
  nothing in waves A–D needs it after the D1 rewrite.
- **R2 — Spec drift discovered en masse.** If clause-by-clause testing surfaces many divergences, the effort
  becomes a spec-vs-implementation reconciliation project. Mitigation: the divergence rule + owner checkpoints
  per iteration keep that visible and steerable rather than silently absorbed.
- **R3 — resolved positive** (see §2): `singlePatch` works end to end in the stub room with manual registry
  setup.
- **R4 (new) — stub growth.** The stub grew member-by-member during the spike (~12 so far); engine code paths
  not yet exercised may demand more. Each addition is a two-line empty shape; the risk is tedium, not
  viability. The stub's header documents that it is exercise-driven, not a Unity API surface.

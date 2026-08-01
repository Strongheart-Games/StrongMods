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

## 2. Known unknowns — two spikes before anything else

- **S1 — Does `Log.Out` work headlessly, or throw?** The game's `Log` may route to Unity natives (throws
  headlessly) or to console/callbacks (fine). Decides the seam: if it throws, the harness must intercept before
  any engine test can run. Also determines whether the spec'd warning/error *messages* are assertable (we want
  them to be: skip-vs-fail semantics are half the spec).
- **S2 — Can `XmlFile` and `Mod` be constructed headlessly?** `XmlFile` is likely a pure managed XML wrapper;
  `Mod` may want a real folder/ModInfo. Fallbacks if hostile: reflection-construct uninitialized instances and
  set only the fields the engine reads, or a minimal on-disk fixture mod folder under the test's scratch.

## 3. Design decisions

**D1 — The Log seam doubles as a capability milestone: Harmony-patch `Log` in the harness.** Whatever S1 finds,
the suite wants captured log output (to assert spec'd warnings), not just suppressed output. Patch `Log.Out`/
`Log.Warning`/`Log.Error` with capturing prefixes at fixture setup — the first actual *patch application* on
CoreCLR, requiring `MonoMod.RuntimeDetour` (modern flavor, NuGet, same pattern as Backports/Utils). This
de-risks the wave-D patch-application goal as a side effect: if we can patch `Log`, we can apply mod patches.
Fallback if CoreCLR patching proves hostile: a `Log.LogCallbacks`-style hook if the game exposes one (S1 will
show), else assert on document state only and skip message assertions (recorded as a coverage gap, not silent).

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

## 4. Iterations (each gated, each ≤ the size limits)

1. **Spikes S1+S2** (scratch, no tracked changes) → results logged here.
2. **Wave A — plumbing + first conformance tests**: `Fixtures/` (engine invocation, XML builders, Log capture
   per D1), plus the "Writing a loop" + "Filling in values" sections as the first conformance classes. Proves
   the whole path; likely the largest single iteration (~250 lines, mostly fixtures).
3. **Wave B — the conformance matrix**: remaining spec sections in 2–3 iterations (bind tables, functions,
   dynamic names, error/skip semantics, gotchas).
4. **Wave C — patcher tests** (D4).
5. **Wave D — go/no-go decision with the owner, then the exploratory pass and findings triage** (D5). Findings
   that turn into mod-behavior changes (graceful-failure guards) are separate per-mod iterations or issues,
   never bundled into the test change.

## 5. Verification

Per iteration: suite green against the live install + both `V3.1.0-b14` trees (3.0.1 trees stay smoke-only —
conformance asserts current-version behavior, not cross-version). CI green on push. For D3's divergence rule:
any spec/implementation mismatch is presented to the owner in the iteration's handoff, with the proposed
resolution direction, before the affected test is finalized.

## 6. Risks

- **R1 — CoreCLR patch application is unproven** (D1). The MonoMod story so far: resolution executes fine,
  application untried. S1/wave A settles it; the fallback keeps conformance viable either way.
- **R2 — Spec drift discovered en masse.** If clause-by-clause testing surfaces many divergences, the effort
  becomes a spec-vs-implementation reconciliation project. Mitigation: the divergence rule + owner checkpoints
  per iteration keep that visible and steerable rather than silently absorbed.
- **R3 — `XmlPatcher.singlePatch` internals** may pull game state beyond the document (localization, version
  checks). S2's fixture-driven first tests will surface this early, while the plumbing is still shapeable.

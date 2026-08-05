# Mod testing architecture: the discussion record

Why StrongDev exists, how per-mod behavioral tests should be structured, and — explicitly — which questions are
settled and which are open. The empirical companion is
[`headless-server-testing.md`](headless-server-testing.md); the work and its status live in GitHub issues.

**This is a record of a design conversation, not an approved plan.** The ledger in §7 is the authoritative
statement of what was actually decided.

## 1. The question that started it

All runner-based tests live in the central `Tests/` project. Should mod-specific tests instead live with the mods
they test?

The initial assessment said keep them central, and had reasonable grounds: `Tests/README.md` records that as a
deliberate decision ("New runner-based tests land here … rather than in per-mod test projects — the expensive
infrastructure is shared"); the project was deliberately renamed *from* `StrongMods.Tests` to `Tests` for repo
scope; and although 65 of 89 test methods were StrongMods-specific, **StrongMods was the only mod with
behavioral tests at all**, so there was no scattering problem to solve.

## 2. What changed the answer

The roadmap. Issue #50 plans a test-idea inventory across *all* mods — StrongFill's fill behavior, AuthZ and
StrongUtils cheat detection, StrongLocks defaulting things to locked. Many mod-specific suites are coming.

That inverts the timing argument. Establishing the pattern is cheapest **before** the wave: today it is a
handful of StrongMods-specific files; afterwards it is a dozen suites plus habits. Fixed costs that looked
disproportionate for one mod amortize across many. And suites born in the right place never need migrating.

**The counterweights are real and remain true.** The TFM chasm does not go away: `Tests` targets `net10.0` while
mods target `net481` against game assemblies, so tests reach mod code by reflection into an isolated
`AssemblyLoadContext`, never by `ProjectReference`. Some suites are irreducibly repo-wide (`SmokeTests`,
`ProjectConventionTests`, `BuildPathResolutionTests`, and `PatchApplicationTests`, which is repo-wide *but runs
on the StrongMods-specific host* — which is precisely why hosts must live in a shared layer). And the current
suite is fast (~5.6s) *because* one process shares its hosts.

## 3. Model A vs Model B

Two ways a mod-specific test can reach the runner.

**Model A — harness-owned discovery.** The mod's test assembly is *content*, not a test project. The single
harness enumerates content assemblies (cheaply, via a Cecil metadata scan — nothing heavy loads at discovery
time) and manifests each found method as a test case tagged with the mod. Precedent already exists:
`SmokeTests.PatchClassCases()` reaches into every mod, pulls out its `[HarmonyPatch]` classes, and manifests
them as individual cases.

**Model B — first-class per-mod test projects.** Each mod gets a real test project the runner ecosystem
recognizes.

**Decision: Model A**, with B layerable on top later if friction demands it.

The reasoning: B is less first-class than it sounds. A real test project must be modern .NET, and the runner only
discovers tests *in the test assembly itself* — so `net481` test logic cannot carry the attributes. B degenerates
into thin wrapper facts delegating to the same engine, meaning the gutter icon you gained sits on the wrapper,
not on the actual test. Meanwhile B forces a process per assembly, and therefore a fixture rebuild per assembly.

A's honest cost is ergonomic: no run-this-test gutter icon at the definition site. That cost is low for this
repo's owner, who builds, tests and deploys the whole solution and would trade finer-grained control for a faster
solution-wide run. It may be higher for future collaborators or outside StrongDev users — which is what B is held
in reserve for.

**The insight that makes A better than the alternative it replaced:** because the test content is an assembly
loaded *into* the host, it lives on the game's side of the TFM chasm. It can reference the mod and the game
assemblies at compile time and assert against real types — colocation *and* typed integration, which a nested
`net10.0` test project could never have. `Tests/FunctionMod/FunctionMod.csproj` is the working prototype of
exactly this shape: a small non-deploying assembly compiled against `StrongMods.dll` for type identity, loaded
into the host by path.

## 4. Performance principles

From the stated preference for "a faster solution-wide button" over finer-grained selection:

- **Lazy, memoized fixtures** — built on first use, once per process, never speculatively. Filtered runs then
  stay fast for free. `PatcherHost.Instance`/`ForLabel` already has this shape.
- **Parallelism by fixture partition, not by process** — partition cases by the fixture they need so independent
  hosts run concurrently in one process. I/O-bound server tests overlap well with CPU-bound conformance tests.
  A single process makes this schedulable in our own code.
- **A speed ledger with per-fixture attribution** — report what each fixture cost to build and each mod's slice
  cost to run, so when the suite crosses the "too slow" threshold the offender is named rather than profiled for.
  Cheap to build in from the start, painful to retrofit.
- **Metadata-only discovery** — enumeration must never construct hosts, keeping discovery instant no matter how
  heavy execution fixtures become.

## 5. Readiness as a capability ladder

The empirical doc §4a establishes that no single signal means "ready": telnet accepting, a command answering,
and `ModEvents.GameStartDone` are all insufficient for some operations, and the marker that does work
(`Dymesh door replacement: imposterBlock`) was found empirically and is unintuitive.

The design response is to **emit our own staged markers** from a probe mod — stable, greppable, self-explanatory
lines at points we choose — and have each test declare the level it needs. When a new operation turns out to need
a later stage, a rung is added rather than a magic line re-derived. It also improves the logs generally, which
matters given that the single largest startup phase currently logs nothing at all.

## 6. The StrongDev product boundary

StrongMods is a **shipped mod**; development-time machinery does not belong in it. StrongDev is the dev-time
toolbox, shipping with StrongMods but not with other mods, and eventually covering asset browsers as well as
testing.

The boundary the recon drew on its own, from the StrongLocks worked example: asserting lock state needs
information the vanilla protocol does not volunteer, so a **server-side probe mod** is required — and that probe
*deploys to the test server*, making it StrongMods-adjacent, while the **driver** that spawns and drives the
server is pure StrongDev.

**The probe exposes primitives; the harness composes assertions.** If the probe command does act-and-assert and
prints PASS, the test logic has migrated out of the test suite into a mod — the coupling to avoid. Keep
assertions in the harness and the probe dumb.

Productizing for *other* modders is a second mountain: the harness bakes in this repo's layout, mod roots and
version registry. The seam should be designed so extraction stays possible, and otherwise deferred until
demanded — consistent with #50's own "demonstrated need, not speculation" rule.

## 7. Decided vs. deferred

**Leaned toward, but not ratified** — all of these were reached in discussion and none were executed; treat them
as strong starting positions, not commitments:

| Position | Basis |
|---|---|
| Mod-specific tests should live with their mods, as a go-forward pattern | The #50 wave makes early adoption cheapest |
| Model A (harness-owned discovery), with B layerable later | §3 |
| Test content is a game-side assembly, not a `net10.0` project | The TFM chasm; FunctionMod as prototype |
| A probe mod exposes primitives; assertions live in the harness | §6 |
| Readiness is a staged ladder from our own markers | §5 |
| Fixture granularity is per-session or per-mod-set, never per-test | Startup floor of ~10-12s |
| StrongDev is a separate project from StrongMods | §6 |

**Explicitly deferred — no decision was made:**

- Where the abstractions live (attributes/interfaces in StrongMods vs a separate `StrongDev.Abstractions`). The
  StrongMods-carries-them option has the simpler type-identity story and precedent in `[XmlPatchFunction]` /
  `[PatchTargetManifest]`; the separate assembly has the cleaner product boundary. **Open.**
- What a contributed test should *say* — code-first attributed methods, declarative scenario data, or both. This
  was deliberately held for the #50 ideation session, which will answer it far more thoroughly. The one stated
  desire on record: standing up a test dedicated server with mods loaded and driving it by simulating client
  behavior.
- Whether the existing `Tests/` StrongMods suites (`Foreach/`, `Ensure/`, `Patcher/`) migrate at all. A third
  answer emerged and was never resolved: they are conformance tests of the *platform StrongDev bundles*, so they
  may belong harness-side as the product's own suite rather than moving per-mod.
- The whole extraction sequence a migration would need — a shared `build/` entry point for test projects (there
  is deliberately no `Directory.Build.props`; see the `Mod.targets` header for why), a fixture library, the
  `ProjectConventionTests` `KnownNonMods` roster and `ModInventory` source-scan exclusion, and switching CI from
  `dotnet test Tests/Tests.csproj` to solution-scoped. Costed, never scheduled.
- Whether "one process" is a requirement or merely today's implementation of "hosts built once". The stated
  preference is *share what is reasonably shareable* and start with low-hanging fruit.

## 8. Doc-accuracy debts noticed along the way

Independent of every decision above, and safe to fix regardless of direction:

- `CLAUDE.md` still states "There is no automated test suite or linter for this repository" — untrue since the
  test suite landed, and directly contradicted by its own *Verifying* section.
- Nothing in `StrongMods/` points at where its tests live, which is the legitimate discoverability complaint
  underneath the original question.
- `Tests/README.md`'s "Planned next" names suites that landed long ago, and its standing decision against
  per-mod test projects should record that it was reconsidered.

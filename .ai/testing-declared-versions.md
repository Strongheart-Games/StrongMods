# The test suite follows declarations: the `SdtdTestVersions` axis (#23 phase 6)

The final phase of `.ai/declared-game-versions.md`, planned separately as that doc promised. Everything before
this made `SdtdTestVersions` declarable, closure-checked (test E), and deploy-consulted (phase 5) — but no
test *runs* against it yet. This phase makes the declaration mean what it says: every mod's tests assert
against every version it declares, and only those.

## 1. What exists (measured, 2026-08-03)

### Vocabulary: "host" (owner, 2026-08-03)

The harness's central concept had gone undefined, under a nonstandard name — "room", coined in the
#14/#43-era work and never written down. Defined, and renamed:

> A **host** is one isolated assembly universe: the game's Managed assemblies plus a deliberately chosen set
> of companions (the real Unity engine or a stub, mod DLLs, StrongMods), loaded into a private
> `AssemblyLoadContext` so its types never unify with the test runtime's or another host's. Built once per
> session — construction loads ~50 MB of assemblies and runs static initializers.

Two hosts exist because their requirements contradict: patch-target **resolution** needs the REAL
`UnityEngine.CoreModule` (Unity types appear in target signatures), while **executing** the XML patcher needs
the STUB CoreModule (the real one's engine-internal callback registration throws headlessly and poisons
`LogLibrary`, and the whole patcher with it). Nothing crosses between them.

**Host is carried everywhere a host appears — class names, file names, and doc references** (owner,
2026-08-03, REVERSING the "differentia not genus" position previously recorded here). That argument assumed a
reader already inside the hosts' context; names travel OUTSIDE it — planning docs, commit messages, grep
output, chat — and out there `GameTree` said nothing about being a host, while `Ctx` invited the owner to
guess `AssemblyLoadContext` (wrongly: it is the smoke tests' shared-state bag). Confusion of that kind breeds
bugs; clear communication outranks rename churn. The general form of this decision is the repo **naming
rule** below, landing in CLAUDE.md with 6-0.

The names, under the rule:

| Today | Becomes | What it is |
| --- | --- | --- |
| `GameRoom` + `GameRoomCollection` (files too) | `PatcherHost` + `PatcherHostCollection` | the stub-engine host that executes the XML patcher |
| `GameTree` | split in 6a: `GameTree` remains as the plain identity **record** (label, root, dirs — the tree itself, not a host) + **`GameEngineHost`**, the real-engine reflection universe for target resolution | a record, and a host |
| `GameTree.Metadata()` — 17 of the class's 34 call sites | `AssemblyMetadata` (owner, 2026-08-03) — it names both ends of one pipe: the `<AssemblyMetadata>` items in `Tests.csproj` write it, the BCL's `AssemblyMetadataAttribute` carries it, this class reads it. `GameTreeMetadata` failed on measured contents (4 of 7 keys aren't tree facts); `BakedMetadata` named the how, not the what | its own small helper |
| `Ctx` | `SmokeTestCtx` (the owner's example), reshaped per-label in 6a | the smoke tests' shared lazy state |

Three senses of "game tree" must not be conflated (they were, briefly, in this very discussion — the rule
catching its own authors): **the artifact** (the on-disk tree, `$(SdtdDir)` — decision 5 vocabulary; files,
not a host), **today's class** `GameTree` (a FUSION: a host's load context + identity data about the
artifact + the `Metadata()` statics — which is why naming arguments about it ran circular), and **the
post-split record** `GameTree` (pure identity: label, root, dirs — a value object describing the artifact,
holding no load context; NOT a host, so the Host suffix would be false, which is worse than vague). The
ontology, in has-a/is-a terms: `GameEngineHost` is-a host, `PatcherHost` is-a host, the record is neither — each
host has-a record naming the artifact its universe was loaded from. Hosts do not nest; both are siblings
over artifacts.

**Host names are contents-first while the suite's shape is still maturing** (owner, 2026-08-03, final):
purpose names are HYPOTHESES about future use; contents names are present FACTS — and with an immature test
suite, the facts age better. A purpose name calcifies today's guess about what tests will want; the owner
explicitly reserves revisiting once more tests and more hosts exist. Taste acknowledged as a legitimate
component of the call. The pair — each named by what it holds:

- **`GameEngineHost`** — holds the REAL game engine (plus every declaring mod's DLL): faithful signatures
  for reflection; cannot execute code that logs. Backs 68 of 142 tests today, and every code mod flows
  through it. Its file-top doc comment MUST open by disarming the one ambiguity the name carries: "hosts the
  real game engine — not a stub" (owner's mitigation, a 6a requirement).
- **`PatcherHost`** — holds the game's XML patching pipeline, executable headlessly (stub CoreModule, seeded
  patch methods, log subscription); wrong for signature fidelity. Backs 63 (Foreach conformance + Patcher
  tests).

13 tests use no host (shell-MSBuild, XDocument scans). There is no "default" host — a test author picks by
what they need from the universe. New host KINDS arrive only with a new isolation requirement (a new
companion/stub set to execute some other subsystem headlessly), never per test category; 6a/6b multiply
INSTANCES (kind × declared label), not kinds. The word "host" itself resolves to exactly one concept in this
codebase (owner); revisit the vocabulary only if a second meaning ever arrives.

Candidate history, compressed for the audit trail: `GameTreeHost` failed differentia (both hosts load from a
game tree); `ModHost` failed on inspection — PatcherHost holds mods too (StrongMods, FunctionMod), so "holds
the mods" never differentiated (the owner named this the unspoken bother); `InspectionHost` + `PatcherHost`
won the owner's inspect-vs-execute litmus test but was set aside — the test was a thought experiment, and
purpose names lost to the durability argument above. If the suite's maturing ever reopens the naming, the
InspectionHost analysis stands ready in this file's history.

The split itself is baked in for two independent reasons: it is what 6a's laziness wants (cheap tree
identities eagerly for every label; expensive hosts on demand — risk R1), and it is what lets `GameTree`
keep its name honestly — a record OF a tree may be named after the tree. In a no-split world the rule cuts
the other way: the single class IS a host and must carry a Host name.

### The naming rule (lands in CLAUDE.md Conventions, phase 6-0)

> **Names must survive leaving their context.** A file or class name is read in places its declaration can't
> follow — planning docs, commit messages, grep output, error text, chat — and the name alone must say what
> the thing is when met there. The test: write the bare name in a doc for a reader who has not opened the
> file; do they know what kind of thing it names? `Ctx` fails (*which* of the dozen kinds of context?);
> `SmokeTestCtx` passes. `GameRoom` failed; `PatcherHost` passes. Carry the category word wherever the
> category is what disambiguates (`*Host` on every isolated game-assembly host); abbreviate only what is
> unambiguous repo-wide (`Sdtd` is; `Ctx` was not). Where a fully self-identifying name is impractical, every
> out-of-file reference carries qualification instead (`SmokeTests.Ctx`, a path). Existing violations are
> grandfathered — the backlog issue (filed in 6-0) enumerates them for one-by-one fixes — but a name a change
> already touches conforms as part of that change, and new names conform always.

Judgment-based, so enforced by prose and review, not a convention test — unlike #54's import order, "does the
bare name communicate?" is not mechanically checkable; the backlog issue's enumeration is the audit.

### The three pieces

Two hosts and a fixture built on the second, all keyed off the **one** `SdtdManagedDir` baked into the test
assembly at build time:

| Piece | Built by | Used by | Grain |
| --- | --- | --- | --- |
| `GameTree` (host: real CoreModule + mod DLLs; splits into `GameTree` record + `GameEngineHost` in 6a) | `Ctx.Tree`, lazy singleton | `SmokeTests` patch-target resolution via `TargetResolver` | already per **mod** (theory per patch class) |
| `PatcherHost` (host: stub CoreModule + StrongMods engine + FunctionMod; today `GameRoom`) | lazy singleton, "expensive… one per test session" | `Foreach/*` engine conformance, `Patcher/*` | per **engine** |
| `PatchPipeline` (fixture on `PatcherHost`: vanilla entry-point docs + each mod's `Config\` applied) | from the host | `PatchApplicationTests` | per **mod** |

The unit axis is per-build (`-p:SdtdUnit`, CI's two legs). The version axis does not exist below the build.

## 2. Decision: expand inside one test process, not across N invocations

Two candidate shapes:

- **A — multi-tree, one process.** `Ctx` becomes per-label; theories expand to (mod × declared version);
  trees load side-by-side in isolated `AssemblyLoadContext`s (the design `UnitLoadContext` already has).
- **B — N invocations.** CI/local loop `dotnet test` once per registry version, `ModInventory` filtering mods
  per run; the Tests assembly rebuilt per version.

**A wins**, on this repo's own facts:

1. Phase 4 pre-paid for it: the restore already lands the **whole registry** per CI leg — the results section
   said so verbatim ("phase 6's test legs arrive with no new restore machinery"). B would spend that on
   invocation loops instead.
2. One `dotnet test` = the full declared matrix, locally and in CI. B's local default run silently skips
   every pinned-away mod — a coverage shrink exactly of the kind `ModInventory` was designed to refuse.
3. The mechanism is already in the codebase: `UnitLoadContext` isolates a tree; two trees are two contexts.
   B instead adds build/test interleaving (per-version Tests rebuilds against `--no-build` mod DLLs) — more
   moving parts in CI YAML, not fewer.
4. Failure identity already carries `Tree.Label` in every message; the axis extends the pattern rather than
   inventing one.

## 3. Design

### 3a. Declarations reach the runtime

- **Tree paths: MSBuild stays the only resolution authority.** `Tests.csproj` bakes one `AssemblyMetadata`
  per registry row — `SdtdTree:<label>` → the resolved tree path for the build's `$(SdtdUnit)` and source —
  derived by batching over the map the same way the build resolves (respecting `-p:SdtdPackagesDir`, so CI's
  isolated root flows through untouched). No C# reimplementation of the path shape.
- **Per-mod lists: one parser, shared.** A small `DeclarationReader` (leading block else
  `GameVersions.props` default; `SplitList` semantics) used by **both** `ModInventory` (consumption) and
  `ProjectConventionTests.E` (validation) — consistent by construction; E keeps its independent `FourPart`
  recomputation, which is the part that must stay independent. `ModInventory.CodeMod` gains `TestVersions`.
- **A declared version whose tree is absent is a test FAILURE naming the restore command** — the phase-5
  stance: unverifiable must not decay into unverified. (Locally: restore covers the registry; CI: always
  present.)

### 3b. `SmokeTestCtx` becomes per-label (phase 6a — the heart)

- `Ctx.Tree` → `SmokeTestCtx.Hosts[label]` (a lazy `GameEngineHost` per label over its `GameTree` record); each
  host loads **only the mods declaring that label** — a pinned-away mod must not even be type-loaded against
  a tree it disclaims (that load blowing up is precisely what its pin exempts it from).
- `SmokeTests` theories expand to (mod, patch class, label); `Manifest_targets_resolve` and
  `Coverage_sanity` iterate labels. `Negative_control` and `All_code_mods_are_built` stay single —
  version-free by nature.
- New sanity: every mod appears in ≥1 tree (dev ∈ test makes the dev tree that floor).
- 0Harmony stays the single default-context copy (type identity for attribute reading demands it). Recorded
  risk R2, not solved here.

### 3c. `PatcherHost`/`PatchPipeline` per label (phase 6b)

- `PatcherHost.Instance` (today `GameRoom.Instance`) → per-label lazy map; `PatchPipeline` follows;
  `PatchApplicationTests` expand to (mod × declared version) — vanilla XML differs per version, which is
  exactly why a mod's patch application is worth asserting per version.
- **`Foreach/*` engine conformance deliberately stays on the default host.** It tests StrongMods' engine
  semantics against synthetic XML, not per-mod compatibility; per-version engine legs are real cost for
  hypothetical signal. Out of scope, noted for a future issue if StrongMods itself ever pins.
- `FunctionMod` (fixture, no entry-point imports, `KnownNonMods`) loads into every host as today — its
  version-independence to be confirmed while implementing, not assumed silently.

## 4. Phases

| # | Phase | Files | Est. |
| --- | --- | --- | --- |
| 6-0 | No behavior change: `GameRoom` → `PatcherHost` and `GameRoomCollection` → `PatcherHostCollection` (classes AND files), `room` locals/comments, host definition into the class doc; the naming rule into CLAUDE.md; the violations backlog issue filed | `Tests/Fixtures/*`, `Tests/Foreach/*`, `Tests/Patcher/*`, `Tests/Tests.csproj` (comments), CLAUDE.md | ~75, mechanical + rule text |
| 6a | DeclarationReader (+ E refactored onto it), per-registry-row baked metadata items, the `GameTree` split (`GameTree` record + `GameEngineHost` + the `AssemblyMetadata` helper absorbing the 17 `Metadata()` call sites; the host's doc comment opens "hosts the real game engine — not a stub"), `Ctx` → `SmokeTestCtx` per-label, SmokeTests expansion | `Tests/Tests.csproj`, `Tests/GameTree.cs`, `Tests/GameEngineHost.cs` (new), `Tests/AssemblyMetadata.cs` (new), `Tests/ModInventory.cs`, `Tests/SmokeTests.cs`, `Tests/ProjectConventionTests.cs` | ~230 |
| 6b | Per-label `PatcherHost`/`PatchPipeline`, PatchApplication expansion | `Tests/Fixtures/*`, `Tests/Patcher/PatchApplicationTests.cs` | ~110 |

6-0 is its own commit by the no-mixed-refactoring rule — rename and redesign never share a diff. 6a and 6b
exceed the 100-line target — this plan is the validation request. CI needs **no workflow change**: each unit
leg already restores the registry and passes `SdtdPackagesDir`/`SdtdUnit` to the test build.

## 4b. Phase 6-0 results (2026-08-03)

| Check | Result |
| --- | --- |
| Backlog issue | filed as **#64**, cited by the CLAUDE.md rule text |
| Renames | `git mv` ×2 (`PatcherHost.cs`, `PatcherHostCollection.cs`) + one mechanical token sweep across 12 files (`GameRoom`→`PatcherHost`, `room`/`Room` identifiers → `host`/`Host`); residual-token grep empty |
| The one deliberate non-rename | `Tests/Stubs/UnityStubs.cs`'s "Clean-room stand-ins" — the reverse-engineering idiom, a different sense of the word; untouched |
| Host definition | landed as `PatcherHost`'s opening doc comment (what a host is; this host's companions; why the stub; the separate-host rationale) |
| Naming rule | in CLAUDE.md *Conventions*, citing #64; `Tests.csproj`'s two "conformance room" comments now say `PatcherHost` (the curry-the-label rule applied to prose) |
| Suite | **exactly 142/142** — the phase's bar: zero assertion changes |
| Diff | spot-checked token-mechanical; sed normalized the 12 touched files' line endings to the repo-standard LF (`.editorconfig`), hence git's benign autocrlf warnings |

## 4c. Phase 6a results (2026-08-03)

The split and the axis, as planned — with the settled names (`GameTree` record, `GameEngineHost`,
`AssemblyMetadata`, `SmokeTestCtx`, `GameVersionDeclarations` — the plan's "DeclarationReader", renamed under
the naming rule before birth). Implementation facts worth keeping:

- **Evaluation-time item metadata cannot use the bare update form** (`<Item><Meta>` outside a target is
  MSB4232); the registry-row metadata went inline on the `Include` element, where `%(Identity)` references
  resolve per created item — the ubiquitous `%(Filename)` mechanism. Verified by `-getItem`: both rows baked
  as `SdtdTree:<label>` → resolved roots, plus `SdtdUnit`/`SdtdUnitDataDir`/`SdtdTreeIsDeclared`.
- **The escape hatch is a baked verdict, not a runtime probe**: `SdtdTreeIsDeclared` compares `$(SdtdDir)`
  to `$(_SdtdDeclaredTree)` in MSBuild; false → the suite runs every mod against that one tree under the
  `"SdtdDir override"` pseudo-label.
- **The Harmony resolver became an explicit call** (`GameEngineHost.EnsureHarmonyResolver()`, called by both
  hosts) — it used to ride on a static-ctor side effect of "touching GameTree first", an implicit ordering
  dependency the split would have silently broken.
- The missing-tree failure demonstrated itself unprompted: this machine's `packages/` held only `3.1.0.14`
  (phase 4's full-registry restore went to scratch), so the first run failed 4 tests with the
  restore-command message — the design's onboarding moment, working. One local-source restore fixed it.

| Check | Result |
| --- | --- |
| Suite | 142 → **206/206**: 63 patch-class cases × 2 labels + the new `Every_mod_is_tested_against_at_least_one_tree` fact — deterministic |
| Wall-clock (R1) | ~4 s → ~5.5 s for the second `GameEngineHost`; per-label laziness holds; 6b may proceed |
| Missing tree | 4 failures with the teaching message, live (see above); green after restore |
| Pin break (DisableLAN → `V3.0.1-b4` only) | Its `V3.1.0-b14` case **vanished**, its `V3.0.1-b4` case remains, 205/205 green — both directions of the filter |
| Escape hatch (`-p:SdtdDir=<live install>`) | Single-tree mode: 143/143 (63 cases × 1 pseudo-label + facts) |
| `-p:SdtdUnit=dedicated-server` | 206/206 — the axis × the unit, both server trees |
| Full solution suite, default | 206/206 |
| Reverts | `git status` shows only the intended 6a files |

Changed: 8 files + 3 new (~330 lines against the ~230 estimate; the overage is doc comments and the
`LoadedLabel` cache shape). CI needs no change — proven by the same commands CI runs.

## 5. Verification

- 6-0 verifies as a rename must: the whole suite green at exactly 142/142, zero assertion changes, the diff
  mechanical.
- Suite counts rise deterministically in 6a/6b (patch-class theories × declared versions); the exact
  before/after recorded per phase.
- **Break checks, the standing bar:** pin a mod to `V3.0.1-b4` only → its `V3.1.0-b14` cases vanish and its
  `V3.0.1-b4` cases appear (both directions asserted); declare a version whose tree is deleted → the
  restore-command failure; revert-diff clean after every break.
- The full suite green on both units (`-p:SdtdUnit=dedicated-server` locally; CI legs after commit).
- 6b additionally: a deliberate patch-application divergence between vanilla versions demonstrated (or the
  absence of any, measured and recorded).

## 6. Risks

- **R1 — cost.** Two trees + two PatcherHosts in one xunit process (~50 MB+ each, initializers rerun).
  Laziness bounds it to labels actually declared; 6a measures wall-clock before 6b commits to per-label
  hosts.
- **R2 — 0Harmony is a singleton across trees** (default-context hook, one `SdtdHarmonyDir`). Attribute-API
  compatibility across game versions is assumed. Acceptable while TFP ships Harmony 2.x; the failure mode is
  loud (type-identity errors), and the fix (per-room Harmony) is known but not free. Recorded, deferred.
- **R3 — 6b may surface real per-version incompatibilities** (a mod's patch warning on 3.0.1 vanilla). That
  is signal, not noise — but it can block 6b's landing. The remedy is honest: pin the mod (shape 2) or fix
  it, each its own commit; 6b does not paper over findings to go green.

## 7. Out of scope

Per-version `Foreach/*` engine legs; advisory-lane test steps (build-only by design); auto-vendor (#63);
per-room 0Harmony (R2's fix).

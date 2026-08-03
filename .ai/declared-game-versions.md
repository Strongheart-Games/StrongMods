# Declared game versions: compile against what you declare, deploy to what you have (#23, #37, #21)

One design, three issues. #23 asks for a tracked declaration — "this mod is developed against version X, tested
against Y/Z" — plus the resolution flip that makes it authoritative and auto-vendoring of anything missing. #37
is what that declaration must survive: a major release where mods migrate at different speeds. #21 contributes
the *unit* axis (game vs dedicated server); its standing CI check already shipped with #22, and the residue its
closing comment names — the local dual-build story, per-unit `GameAssembly` divergence — is discharged once a
declared (version × unit) matrix exists.

Prerequisites are all closed: #13 (the Deploy seam), #14 (test harness), #15 (vendored trees), #22 (feed,
packages, CI).

## 1. Decisions taken (owner, 2026-08-02 – 2026-08-03)

1. **Full flip.** Builds compile against the declared version's tree. The live install matters only as the
   deploy destination.
2. **Auto-vendor is deferred** (filed as #63; since demoted by decision 8). A missing declared tree is a
   readable build error naming the exact command that fixes it — per source: the restore command on the
   packages side, the `vendor.cs` command in vendor mode.
3. **Declared combinations block; a separate advisory lane does not.** Every declared (version × unit) is a
   blocking CI build. A non-blocking lane builds everything against the newest published version, so migration
   progress is visible without holding up main.
4. **Deploy refuses a mismatch**, with a `-p:` escape hatch.
5. **Naming: `SdtdDir` keeps its broad meaning; the narrow side gets the new name.** The resolved game tree is
   read by compilation, the tests' `GameTree`, and the patch-application fixtures — "everything except deploy" —
   so renaming it ("compile", "dev") would trade one too-narrow name for another, and `SdtdDir` already means
   "the tree we work against" across CLAUDE.md, CI, and every `.ai/` doc. Instead the deploy half narrows OUT of
   it: **`SdtdInstallDir`** names the live install in its three roles — deploy destination, `vendor.cs` source,
   and (phase 5) the version-check subject. `Local.props`' `<SdtdDir>` becomes `<SdtdInstallDir>` (old spelling
   honored as the install root so no machine breaks silently); `SDTD_HOME` keeps meaning exactly what its name
   says, matching `vendor.cs --install-dir` discovery.
6. **One tree layout everywhere: the packages layout.** Local builds and CI resolve a declared version under
   the same restored-packages shape (`<root>/7dtd.assemblies.<unit>/<version>/`). `vendor/<unit>/<label>/` is
   publishing staging — `vendor.cs` writes it, `pack.cs` reads it — and a build root only per decision 7.
7. **`vendor/` as a build root is explicit-only — an input, never a discovery.** Resolution never probes both
   roots and picks one: the source is selected by `-p:SdtdTreeSource=vendor` (default `packages`), and a
   missing tree in the selected source errors even if the other source has it, so a broken packages path can
   never be silently masked by a stale vendored tree. The packages-side error teaches the vendor escape and
   says it is temporary; vendor mode announces itself in the build log on every build, so it cannot linger
   unnoticed — including when parked in `Local.props`.
8. **Fresh clone restores from the private feed** (`PACKAGES_READ_TOKEN`, a read PAT — the same variable CI
   uses) rather than vendoring + packing locally. Vendoring is a publishing procedure, run once per version by
   the publisher, not once per machine — DRY at the procedure level. Decisive beyond DRY: the feed is the only
   source that can supply an *old* version during a #37 transition, because local vendoring can only capture
   what is currently installed — the vendor-first onboarding story had a hidden dependency on #63. Consequence:
   collaborator feed access becomes an entitlement assertion (they own the game) — a policy line to record in
   `.ai/ci-feed-and-workflow.md`'s leak model when phase 2 lands.
9. **Per-mod declarations live in the mod's `.csproj`, above the entry-point props import** — not in a central
   exceptions list. Mod-specific config belongs with the mod, where someone reading the mod finds it, and it
   matches the repo's own "csprojs carry only what is unique to them". Measured (§4): a leading block is set
   before `GamePaths.props` derives; the same block between the imports is the silent `OutDir`-latch shape —
   so phase 2 adds an execution-time consistency guard and phase 3 amends #54's first-element rule with a
   literal-only, declaration-only leading-block allowance. `build/GameVersions.props` remains, recast as
   repo defaults + the version registry, bound to the per-mod declarations by closure tests.

## 2. The two roots — the change everything else rests on

`build/GamePaths.props` derives every path from a single `$(_SdtdRoot)`: `SdtdManagedDir`, `SdtdHarmonyDir`,
`SdtdConfigDir`, `FrameworkPathOverride` — but also `ModsDir`, `SdtdSavesDir`, `SdtdServerDir`. That conflation
is invisible today because the one root is the live install, which genuinely is both.

The flip breaks it. Point the root at `vendor/game/V3.1.0-b14` and `ModsDir` becomes
`vendor/game/V3.1.0-b14/Mods` — a deploy into the vendored tree, the exact footgun CLAUDE.md warns about and
`.ai/f5b-game-assembly-packages.md` §"Footgun" records. So the flip *is* the split:

| Root | Property | Set from | Derives |
| --- | --- | --- | --- |
| **Game tree** | `SdtdDir` | declared version × unit, resolved under the restored-packages root — locally and CI alike; `vendor/` only via explicit `-p:SdtdTreeSource=vendor` | `SdtdManagedDir`, `SdtdHarmonyDir`, `SdtdConfigDir`, `FrameworkPathOverride` |
| **Install** | `SdtdInstallDir` | this machine — `Local.props`, `SDTD_HOME`, the default | `ModsDir`, `SdtdSavesDir`, `SdtdServerDir` |

This is #23's "the machine's installed game version matters only at the deploy step" expressed structurally
rather than as prose. It also retires the standing "never combine `-t:Deploy` with `-p:SdtdDir`" hazard: after
the split, `-p:SdtdDir` cannot reach the deploy destination at all.

`SdtdHarmonyDir` sits on the game-tree side (it is a compile reference, and `vendor.cs` copies
`Mods/0_TFP_Harmony` into every tree for exactly this reason). Naming and `Local.props` migration: decision 5.

## 3. Three coordinates

- **Unit** — `game` | `dedicated-server`. Build-level, not per-project: `-p:SdtdUnit=…`, default `game`. This is
  #21's axis, promoted from "pass a different `-p:SdtdDir`" to a first-class coordinate.
- **Version label** — per project, declared. `V3.1.0-b14`, the human coordinate `vendor.cs` already uses.
- **Root** — `<packages-root>/7dtd.assemblies.<unit>/<package-version>/`, the same layout locally and in CI
  (decision 6). Locally the restore target is the repo-root gitignored `packages/` — already NuGet's shape (a
  stray Cronos sits there today) and already ignored, but only by the boilerplate `[Pp]ackages/` rule; phase 2
  gives it the explicit ignore rule + legal comment `vendor/` got. CI keeps its isolated
  `.scratch/game-packages`. `vendor/<unit>/<label>/` is publishing staging and the explicit-only escape
  (decision 7).

Resolution keys on the package version while humans declare labels, so the declaration file carries the pairs
explicitly (§4) rather than deriving one from the other in MSBuild string functions — the mapping is not a
substring swap (`V2.5-b8` → `2.5.0.8`, four parts always). `pack.cs`'s `FourPart` stays the canonical rule; a
test asserts every declared pair against an independent recomputation of it — the same
two-independent-computations pattern `pack.cs` itself uses against the vendor stub.

## 4. Where the declaration lives

**In the mod's own `.csproj`, above the entry-point props import** (owner, 2026-08-03): mod-specific config
belongs with the mod, where someone reading the mod finds it — and CLAUDE.md's own rule says `.csproj` files
carry only what is unique to them, which a pin is.

Two properties, both holding registry labels: `SdtdDevVersion` — the version this mod is developed and
compiled against; `SdtdTestVersions` — every version its tests assert against (semicolon list; still a
literal, so the #54 allowance holds). Semicolon, not comma, is measured mechanics, not taste:
`Include="$(SdtdTestVersions)"` splits a `;`-list into items natively (the `TargetFrameworks` idiom), while a
comma yields ONE item containing the comma — every consumer would need a hand-rolled split, and a forgotten one
is a silent list-of-one. A stray comma still fails loudly: the comma-joined token matches no registry row, and
the closure test's message should hint "labels are semicolon-separated" (phase 3). **A mod's dev version must appear in its test list** — compiling against
a version you never test is exactly the silent gap the declaration exists to close — and the closure test
enforces it.

### The three expected day-to-day shapes

Labels as of 2026-08-03: `V3.1.0-b14` is the 3.1-line head, `V3.0.1-b4` the 3.0-line head.

**1. The default — most mods: no block at all.** Develop against the latest, test against every supported
branch head. The repo default *is* this scenario, so the canonical csproj stays the untouched 4-liner:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\build\Mod.props" />
  <Import Project="..\build\Mod.targets" />
</Project>
```

**2. Works only on the latest** — say, it uses an API the 3.0 line lacks. Dev inherits the default; the test
list narrows to what the mod actually supports:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <SdtdTestVersions>V3.1.0-b14</SdtdTestVersions>
  </PropertyGroup>
  <Import Project="..\build\Mod.props" />
  <Import Project="..\build\Mod.targets" />
</Project>
```

**3. Not yet working on the latest** — the #37 transition case. Both properties pin to the older head; the mod
compiles, tests, and stays green there while migration happens:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <SdtdDevVersion>V3.0.1-b4</SdtdDevVersion>
    <SdtdTestVersions>V3.0.1-b4</SdtdTestVersions>
  </PropertyGroup>
  <Import Project="..\build\Mod.props" />
  <Import Project="..\build\Mod.targets" />
</Project>
```

A lagging mod does **not** declare the new version as a test version to "watch it fail" — declared tests must
pass; how-far-along-is-migration is the advisory lane's job (decision 3).

**Pins are literals, not roles** — "latest" never appears as a value. When the default advances, unpinned mods
follow automatically and pinned mods deliberately do not; a pin moving is an edit in that mod's file and
history. Writing the shapes out surfaced the retention interaction that makes this workable: the feed keeps
only the head build per `major.minor.patch` line (per unit), so registry rows — and therefore declarable
labels — are **branch heads**. A same-line bump (b14 → b15) *replaces* the label: the default, the registry
row, and any pinned list naming it rename together — mechanical, and enforced, because the closure test
refuses a label with no row and the superseded package no longer restores. A **cross-line** pin — shape 3's
`V3.0.1-b4` while the 3.1 line advances — is the durable kind: the 3.0.1 head stays published. The case the
design exists for is exactly the stable one.

`build/GameVersions.props` (new, imported by `build/GamePaths.props` before it resolves anything) is **not a
per-mod exceptions list** — it carries only what is genuinely central: the repo-wide defaults and the version
registry.

```xml
<PropertyGroup>
  <SdtdUnit Condition="'$(SdtdUnit)' == ''">game</SdtdUnit>
  <!-- Shape 1, the repo-wide default: develop against the latest head, test against every supported head. -->
  <SdtdDevVersion Condition="'$(SdtdDevVersion)' == ''">V3.1.0-b14</SdtdDevVersion>
  <SdtdTestVersions Condition="'$(SdtdTestVersions)' == ''">V3.1.0-b14;V3.0.1-b4</SdtdTestVersions>
</PropertyGroup>

<!-- The version registry: every label anything declares (dev or test), with its package version — THE union
     CI restores; resolution reads PackageVersion. Rows are branch heads, mirroring feed retention. A closure
     test binds this to the per-mod declarations: every declared label has a row, every row is declared by
     something (no dead pins), each mod's dev version appears in its test list, and every PackageVersion
     matches an independent FourPart recomputation. -->
<ItemGroup>
  <SdtdGameVersion Include="V3.1.0-b14" PackageVersion="3.1.0.14" />
  <SdtdGameVersion Include="V3.0.1-b4" PackageVersion="3.0.1.4" />
</ItemGroup>
```

**Placement is load-bearing, measured (2026-08-03).** The property pass is document-order: a declaration ABOVE
`Mod.props` is set before `GamePaths.props` resolves, and the registry default's `Condition` yields to it. The
same block BETWEEN the imports — the conventional deviation slot — assigns after derivation has already run:
measured, `SdtdDir` set between the imports reads back the override while `SdtdManagedDir` silently kept the
default. That is the `OutDir`-latch shape. Two guards make it loud:

- **Execution-time consistency check** (phase 2, shared targets): `GamePaths.props` captures the declaration
  value it resolved with; a validation target errors if the final value differs — "set it above the
  `build\Mod.props` import, not below". Catches what a file scan cannot (values set by unscanned imports).
- **#54's convention test, amended** (phase 3): the first-element rule gains a scoped allowance — one optional
  leading `PropertyGroup` containing only declaration properties (`SdtdDevVersion`, `SdtdTestVersions`) with
  **literal values, no `$()` references**. Literal-only keeps the `C:\Hades` class structurally impossible:
  that incident was a pre-import element *referencing* a shared property, which a literal cannot do. The same
  scan fails a declaration property found between the imports, teaching the two-slot rule: declarations above,
  deviations between.

Shape notes: modlets need no allowance (their entry point is a lone targets import; body-before-import is
already their shape). Overlays get the same leading-block allowance; `DeployRoot` stays between the imports,
where its `$(ModsDir)` reference resolves. One documented edge: an unconditional declaration property in
`Local.props` would stomp every mod's pin before derivation, invisibly to the guard — declaration properties
do not belong in `Local.props`; a lint is the remedy if that ever bites.

Game-tree precedence, highest first: explicit `-p:SdtdDir` (escape hatch — the CI advisory lane and one-off
verification) → this project's declared version resolved under `$(SdtdUnit)` and the selected source
(`$(SdtdTreeSource)`: `packages` default, `vendor` explicit) → a readable, per-source, instructional error.
Packages-side it names the exact restore command and, for the pre-publish case, the temporary
`-p:SdtdTreeSource=vendor` escape with the nudge that packages is canonical; vendor-side it names the exact
`vendor.cs` command. Never a probe: a missing tree in the selected source errors even if the other source has
it (decision 7).

## 5. Phases

Each lands separately with its own go. Sizes are changed lines excluding docs.

| # | Phase | Discharges | Files | Est. |
| --- | --- | --- | --- | --- |
| 1 | Split the game tree from the install root (`SdtdInstallDir`), still fed by one value — behavior identical | groundwork | `build/GamePaths.props`, `Local.props.sample`, CLAUDE.md | ~50 |
| 2 | `build/GameVersions.props`, `SdtdUnit`, packages-root resolution + `SdtdTreeSource` escape, per-source instructional errors, vendor-mode announcement, declaration consistency guard; restore vehicle leaves `build/ci/`; explicit `packages/` ignore rule. The flip | #23 (1, 2) | `build/GameVersions.props` (new), `build/GamePaths.props`, `build/*.targets`, `build/ci/*` (relocate), `.gitignore` | ~120 |
| 3 | Executable guards for the declaration and the split; #54's first-element rule gains the declaration-block allowance | #23, #54 pattern | `Tests/` | ~120 |
| 4 | CI: per-declaration × unit matrix, plus the non-blocking newest-version lane | #21, #37, #23 | `build/ci/`, `.github/workflows/build-and-test.yml` | ~120 |
| 5 | Deploy refuses a version mismatch, `-p:` escape hatch | #37 | `build/Deploy.targets`, `build/Overlay.targets`, `Tests/` | ~70 |
| 6 | Test suite follows declarations; the `SdtdTestVersions` axis | #23 (1), #14 | `Tests/GameTree.cs`, `Tests/ModInventory.cs`, CI | large — re-planned when reached |

**Phase 1 is deliberately a no-op.** It is the riskiest edit in the set — it touches evaluation for all 29
projects — so it lands with `compare-eval` proving *nothing moved*, before any behavior change rides on it.

**Phase 2 is the first visible change**, and its `compare-eval` diff should show exactly one class of delta:
game-tree properties re-rooting from the live install to the declared version's restored package tree
(`packages/7dtd.assemblies.game/3.1.0.14/`), with `ModsDir` / `SdtdSavesDir` / `SdtdServerDir` untouched.
Anything else in that diff is a bug. Phase 2's verification also demonstrates both instructional errors and the
vendor-mode announcement live, and that packages mode ignores an existing vendored tree (no probing).

**Phase 4's shape.** `GameAssemblies.csproj` carries one `PackageReference` version per unit, and NuGet cannot
reference two versions of one id from a single project. `PackageDownload` can: it takes an exact-version list
(`Version="[3.1.0.14];[4.0.0.1]"`) and downloads without referencing — on paper exactly this use case,
collapsing N restores to one. Verify that against the real feed before designing around it; N restores into
the shared packages folder is the fallback (versions sit in sibling directories by construction). The
workflow's "expect exactly one restored version directory" guard becomes "expect exactly the declared set" —
generated from `SdtdGameVersion` items, never hand-listed. The matrix stops passing a global `-p:SdtdDir`; it
passes a packages root and lets each project resolve its own declaration. Blocking *builds* are per declared
**dev** version × unit; the restore set is the whole registry (dev and test labels alike), so phase 6's test
legs arrive with no new restore machinery.

**Phase 6 is the big one and is estimated, not planned.** `Tests` bakes `SdtdManagedDir`/`SdtdConfigDir`/
`SdtdHarmonyDir` into the assembly as `AssemblyMetadata` at build time and builds one `GameTree` for all mods.
Per-declaration testing means a tree per declared version and mods grouped by declaration — a real change to
`GameTree` and `ModInventory`. It gets its own plan when phases 1–5 have landed.

## 6. Verification approach

- **Phases 1–2:** `compare-eval` against a `HEAD` worktree for one project of each shape, plus `Tests`. Phase 1
  must be a no-op; phase 2's diff must be exactly the game-tree re-rooting. Then full solution build and full
  suite against both units.
- **Phase 3:** every new test confirmed to fail when its invariant is broken — the standard set by #53/#54.
- **Phase 4:** the workflow exercised on a branch before it gates main; the advisory lane proven non-blocking by
  making it fail on purpose.
- **Phase 5:** redirected deploys into `.scratch/` only, both the refusal and the escape hatch.
- Live installs are never a deploy target during any phase.

## 7. Risks

- **R1 — Phase 2 changes what every mod compiles against.** Today that is the live install; after, a vendored
  tree. If the two differ, real compile errors surface. That is the point of the change, but it means phase 2
  can go red for reasons that are not bugs in phase 2. Mitigation: the repo-wide default declares the label
  matching the current live install, so the first flip should be a semantic no-op; any error it does surface is
  a genuine pre-existing divergence worth a filed issue.
- **R2 — A fresh clone needs feed access.** One restore with `PACKAGES_READ_TOKEN` set (a read PAT) and the
  clone builds — no vendoring, no packing; `pack.cs` stays a publisher-and-CI concern, off the everyday
  critical path. No token, offline, or building a just-vendored not-yet-published version → the explicit
  vendor escape (decision 7). What remains of the old "every developer vendors" burden lives in #63, demoted
  to a publish-time convenience.
- **R3 — The game-tree/install split can silently re-merge.** A future edit deriving `ModsDir` from the game
  tree again would reintroduce deploy-into-the-tree. Phase 3 pins it with a test rather than a comment — the
  lesson #52 taught about prose.

## 8. Not in scope, filed separately

- **Auto-vendor** (#23 piece 3) — filed as **#63**. SteamCMD fetch of a declared-but-missing version. Anonymous
  SteamCMD reaches only the dedicated server (294420); the game (251570) needs a logged-in owning account, and a
  naive fetch is multi-GB for a ~47 MB tree, so depot-scoped download needs research before design. Local-only
  by construction: CI restores trees from the private feed and must keep doing so. **Demoted 2026-08-03** with
  decision 8 (recorded on the issue): fresh clones restore from the feed too, so auto-vendor is a publish-time
  convenience — fetching a version not currently installed — not an onboarding blocker.

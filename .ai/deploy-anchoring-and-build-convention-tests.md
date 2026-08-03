# Deploy-path anchoring and the tests that pin it (#52 → #53 → #54)

One thread, three commits. #52 fixes the last half of the relative-path bug class #46 opened; #53 makes both
halves executable; #54 does the same for the import-order invariant. Each phase lands separately and needs its
own go.

| Phase | Issue | Files touched | Est. changed lines |
| --- | --- | --- | --- |
| 1 | #52 | `build/Deploy.targets`, `build/Overlay.targets`, `build/GamePaths.props` (comment), `CLAUDE.md` | ~20 |
| 2 | #53 | `Tests/BuildPathResolutionTests.cs` (new) | ~130 |
| 3 | #54 | `Tests/ProjectConventionTests.cs` | ~90 |

---

## Phase 1 — #52: anchor the deploy destination at the invocation directory

### The bug, restated

`_DeployDir` is computed *inside* the `Deploy` target. MSBuild points the process working directory at each
project while running that project's targets, so `[MSBuild]::NormalizeDirectory('$(ModsDir)', …)` with a
relative `ModsDir` resolves against the **project**, not the invocation. `-p:ModsDir=.scratch/deploy` therefore
creates 29 per-project `.scratch\deploy` folders and nothing at the repo root, reporting `0 Error(s)`.

#46's fix does not reach it: `ModsDir`/`SdtdSavesDir` arrive as *global* properties, which a project file
cannot reassign, and the resolution happens at execution rather than evaluation time. So the anchor has to go
at the consumption site.

### Changes

1. `build/Deploy.targets:80` — name the startup directory explicitly:
   ```xml
   <_DeployDir>$([MSBuild]::NormalizeDirectory('$(MSBuildStartupDirectory)', '$(ModsDir)', '$(ModDeployName)'))</_DeployDir>
   ```
   `NormalizeDirectory` has `Path.Combine` semantics, so an absolute `$(ModsDir)` — every real deploy, and CI —
   discards the anchor and passes through byte-identical.

2. `build/Overlay.targets:79` — same treatment for `DeployRoot`, which overlays build out of `$(ModsDir)` or
   `$(SdtdSavesDir)`:
   ```xml
   <_DeployDir>$([MSBuild]::NormalizeDirectory('$(MSBuildStartupDirectory)', '$(DeployRoot)'))</_DeployDir>
   ```
   Ordering note: the driveless-`DeployRoot` guard at `Overlay.targets:75` runs *before* this and is untouched,
   so the `C:\Hades` failure mode still produces its readable error rather than being silently anchored.
   `_MirrorStaged`/`_MirrorDeployed`/`_ProtectedStaged` all derive from `_DeployDir`, so scoped mirroring
   follows the anchor for free.

3. `build/Deploy.targets:21–26` — the comment currently says the opposite of the truth ("PASS -p:ModsDir AS AN
   ABSOLUTE PATH… #52 fixes it… this note goes when that lands"). Replaced with the now-true rule: relative
   means relative to where you ran the command, and *why* the anchor is named rather than left implicit.

4. `build/GamePaths.props:16–20` — its header already claims Deploy.targets applies the same rule. True after
   this; keep, and drop the parenthetical at lines 38–40 that cites the scattering as a live bug.

5. `CLAUDE.md` — two spots that exist only because of this bug: the *Verifying* §2 "**Both must be absolute
   until #52 lands**" sentence and the matching clause under *Scratch space*.

`.ai/gamepaths-relative-fix.md` §D4 is **not** edited — it is the record of that effort, and "out of scope
here, filed as #52" stays accurate as history.

### Verification

- `compare-eval` vs a `HEAD` worktree for one project of each shape (code mod, modlet, overlay). Must be a
  no-op — `_DeployDir` is target-local so it should not even appear, which is the point: nothing evaluation-time
  moves.
- `-t:Deploy -p:ModsDir=<abs>\.scratch\...` (the absolute form used today) unchanged: mirror lands in exactly
  one folder.
- `-t:Deploy -p:ModsDir=.scratch/deploy -p:SdtdSavesDir=.scratch/saves` from the repo root: 28 mod folders plus
  the StrongholdSaves content under the repo-root `.scratch\`, and **zero** per-project `.scratch` directories
  (`git status` plus an explicit sweep for `*/.scratch`).
- The same relative command run from a *subdirectory*, to prove it follows the invocation and not the repo root.
- Full solution build against the live install unchanged; full suite green.

Live installs are never targeted during this phase — every deploy is redirected into `.scratch\`.

### Phase 1 results (2026-08-02)

Environment: repo root, no `Local.props`, `SDTD_HOME` unset, live client install, `dotnet` 10 driving MSBuild.

| Check | Invocation | Result |
| --- | --- | --- |
| Evaluation no-op | `compare-eval` vs a `HEAD` worktree: DynamicFeralSense (mod), AECInternationalMarketFixes (modlet), Hades + StrongholdSaves (overlays) | Three IDENTICAL; DynamicFeralSense's only diff is the worktree-location prefix on `TargetDir`/`TargetPath`. `_DeployDir` is target-local and never appears — the point of the phase |
| Relative redirect, whole solution | `-t:Deploy -p:ModsDir=.scratch/p52-deploy -p:SdtdSavesDir=.scratch/p52-saves` from the repo root | 28 mod folders at the repo-root redirect plus the StrongholdSaves overlay content; **zero** stray per-project `.scratch` directories; `git status` clean but for the four edited files |
| The same command on `HEAD` | run inside the baseline worktree | Reproduced: `0 Error(s)`, nothing at the invocation root, **29** scattered per-project `.scratch` folders. The check is not vacuous |
| Invocation-dir anchoring | cwd `.scratch\p52-subdir\`, one modlet by absolute path, `-p:ModsDir=out` | Landed at `.scratch\p52-subdir\out\…` — follows the invocation, not the repo root and not the project |
| Absolute redirect unchanged | mod + both overlays, `-p:ModsDir=<abs>\mods -p:SdtdSavesDir=<abs>\saves` | Each landed in exactly one folder at the redirect; the anchor is discarded as designed |
| `C:\Hades` guard survives | `-p:DeployRoot=\Hades` on Hades | Still the readable driveless-`DeployRoot` error. The anchor cannot rescue a rooted-but-driveless path (`Path.Combine` discards the base) and the guard runs ahead of it either way |
| Full build, live install | `dotnet build StrongMods.sln -c Debug` | 0 warnings, 0 errors |
| Full suite | `dotnet test StrongMods.sln -c Debug` | 130/130 |

No live install was written to: every `-t:Deploy` in this phase carried a redirect, and the plain build stages
to `bin\` only.

---

## Phase 2 — #53: a regression test for invocation-relative resolution

New file `Tests/BuildPathResolutionTests.cs`. Shells `dotnet msbuild` and asserts the resolution contract, the
one thing an XDocument scan cannot see.

**Why shelling is the only option:** the rule under test is about MSBuild's own evaluation and target-execution
phases. Nothing short of running MSBuild observes it.

**No game install needed.** Layout detection is `Exists()` on directories, so a fixture is two empty
directories. Each test builds its own throwaway root under the OS temp dir and — crucially — sets the shelled
process's `WorkingDirectory` there, so a passing test cannot be an accident of the repo root.

| Test | Invocation | Asserts |
| --- | --- | --- |
| Evaluation, rooted | `-getProperty:SdtdManagedDir,FrameworkPathOverride,ModsDir -p:SdtdDir=<relative>` on a code mod | all three `Path.IsPathRooted` |
| Evaluation, anchored | same | all three start with the temp working directory, not the project dir and not the repo root |
| Layout detection | fixture containing `7DaysToDieServer_Data\Managed` | `SdtdManagedDir` ends in the **server** data folder — the silent misdetection #46 fixed |
| Deploy, anchored | `-t:Deploy -p:ModsDir=<relative>` on one modlet | exactly one folder at `<temp>\<relative>\<ModDeployName>`; `<projectDir>\<relative>` does not exist |

A modlet is the deploy subject deliberately: no compile, so the test costs a copy. The deploy target is the
only thing in the repo that writes outside `bin\`, and this pins where.

Failure messages carry the when/where rule — evaluation vs target execution, project dir vs
`$(MSBuildStartupDirectory)` vs process cwd — in the house style where the failing test teaches the trap.

### Phase 2 results (2026-08-02)

**The open question, settled: no restore is needed.** `dotnet msbuild <code mod> -getProperty:…` against a
freshly-added `git worktree` (no `obj\`, nothing restored) evaluates fine and prints the properties.
`-getProperty` never runs a target, and the NuGet assets file is only consulted by targets. So the evaluation
tests keep the code-mod shape and keep `FrameworkPathOverride`.

**Two design points that moved off the plan, both for the better:**

1. **The probe projects are synthetic, not repo projects.** Each test writes a two-line `.csproj` into the temp
   tree importing the real `build\Mod.props`/`Mod.targets` or `build\Modlet.targets` by absolute path. That pins
   the shared files themselves rather than one mod's spelling of them, needs no game install, and writes nothing
   into the working tree — a repo project would have staged into its own `bin\`.
2. **The invocation directory and the project directory are SIBLINGS in the temp tree** (`invoked-from\` and
   `elsewhere\`). If the project sat inside the invocation directory the two resolution bases would be
   indistinguishable and the test would pass under either behavior.

**`ModsDir` is deliberately absent from the evaluation assertions.** `Local.props.sample` documents
`<ModsDir>` as a per-machine permanent redirect, and `GamePaths.props` imports `Local.props` from any project —
so asserting a derived `ModsDir` would fail on a developer machine that legitimately sets one. `ModsDir` is
covered where it actually matters instead: the deploy test passes `-p:ModsDir=` as a global, which no
`Local.props` can override, at the target-execution site #52 fixed.

**A trap worth recording:** a fixture tree needs a zero-byte `mscorlib.dll` in its `Managed\` folder.
`Microsoft.Common.CurrentVersion.targets:85` blanks `FrameworkPathOverride` to `$(MSBuildFrameworkToolsPath)` —
empty under `dotnet msbuild` on CoreCLR — unless `mscorlib.dll` is found there. Without the stub the test would
assert on `""` and pass vacuously.

| Check | Result |
| --- | --- |
| The three new tests | Pass |
| Deploy anchor reverted in `build/Deploy.targets` | `Relative_ModsDir_…` **fails**, message naming the invocation dir, the project dir, and the fix |
| `_SdtdRoot` anchor reverted in `build/GamePaths.props` | Both `Relative_SdtdDir_…` tests **fail**; layout detection falls through to the game layout and `SdtdManagedDir` comes back as the literal `tree\7DaysToDie_Data\Managed` |
| Shared build files after the revert experiments | `git diff build/` empty — byte-identical to `HEAD` |
| Full suite, live install | 133/133 (was 130) |
| Full suite, `-p:SdtdDir=vendor/dedicated-server/V3.1.0-b14` | 133/133 — the new tests pass their own `-p:SdtdDir`, so they are unit-independent by construction |
| Temp workspaces after the run | 0 left behind |

---

## Phase 3 — #54: convention test for entry-point import order

Extends `Tests/ProjectConventionTests.cs`, the pattern #51 established. Pure `XDocument` scan of every
`.csproj`; classification is by which `build\` entry point a project imports.

| Shape | Rule |
| --- | --- |
| Code mod (`..\build\Mod.props`) | `Mod.props` is the FIRST child element of the project body, `Mod.targets` the LAST |
| Modlet (`..\build\Modlet.targets`) | imports it as the LAST child element, and imports no other entry point |
| Overlay (`..\build\Overlay.props`) | `Overlay.props` FIRST, `Overlay.targets` LAST, `DeployRoot` defined between them |
| Any | never mixes entry points, and never imports `Deploy.targets` directly (the two Deploy targets would collide) |

Comments and the XML declaration are not elements, so the templates' `<!--#if (IsTemplate)-->` blocks and every
project's leading comment are invisible to "first child element" — no special-casing needed, and the templates
are expected to *pass* rather than be exempted.

**Exemptions are asserted, not skipped.** The set of scanned projects importing no entry point must equal
exactly `{Tests, Tests\Stubs\UnityStub, Tests\FunctionMod, build\ci\GameAssemblies}`. A new unclassified project
fails the test and has to be named, which is the opposite of a silent skip.

The failure message cites the two incidents that make order load-bearing: the `DeployRoot`-above-`Overlay.props`
deploy that landed in `C:\Hades` (2026-07-30), and the `OutDir` latch that forbids a `Directory.Build.targets`.

### Phase 3 results (2026-08-02)

Landed as two tests, not one. `B_…` checks the shapes and their order; `C_…` asserts the roster of projects
importing no entry point. They are separate because `B` can only check a project it can classify — the
unclassified ones are exactly the blind spot, so the roster needs its own assertion rather than a clause inside
`B`. The break test below demonstrates that split: making a modlet unclassifiable leaves `B` **passing**.

**Both plan predictions held.** The templates conform and need no exemption — `dotnet new`'s
`<!--#if (IsTemplate)-->` markers are XML *comments* wrapping real elements, and comments are not elements, so
they are invisible to a first/last-child check. And `build\ci\GameAssemblies.csproj` is a fourth unclassified
project the issue text did not list; it is in the roster with the other three.

| Break | Result |
| --- | --- |
| `<PropertyGroup>` appended after `DisableLAN`'s `Mod.targets` import | `B` fails: "LAST element must be…", message carrying the `OutDir`-latch story |
| `Hades`' `Overlay.props` import moved below the `DeployRoot` `PropertyGroup` | `B` fails **twice** — first-element rule and the `DeployRoot`-below-the-import rule — both citing the `C:\Hades` deploy |
| `Modlet.targets` import added to `StrongBoxes` (a code mod) | `B` fails: entry points of 2 different shapes, colliding `Deploy` targets |
| `AECVehiclesFixes`' import path mangled so it matches no entry point | `C` fails, listing the project; **`B` passes** — the blind spot, shown live |
| All four reverted | `git diff -- '*.csproj'` empty |

| Check | Result |
| --- | --- |
| Full suite, live install | 135/135 (was 133) |
| Full suite, `-p:SdtdDir=vendor/dedicated-server/V3.1.0-b14` | 135/135 |
| Full solution build | 0 warnings, 0 errors |

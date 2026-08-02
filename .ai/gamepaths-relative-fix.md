# Relative `-p:SdtdDir` resolves per-project — fix (#46)

Small, contained change to `build/GamePaths.props`. Recorded because it touches shared build infrastructure
every project imports, and because the constraint that shapes it (global properties cannot be reassigned) is
not obvious from the diff.

## 1. The bug

MSBuild resolves relative paths in `Exists()` and `HintPath` against **the project's** directory, not the
invocation directory. Every project sits one level below the repo root, so the documented
`-p:SdtdDir=vendor/game/V3.1.0-b14` never resolves to the tree the caller meant. Three observed failure modes:

| Case | Today |
| --- | --- |
| Dedicated-server tree, relative path | Layout detection's `Exists()` misses → **silently** falls back to the game layout |
| Any tree, relative path, mod project in the graph | `NETSDK1052: Framework list file path … is not rooted` (from `FrameworkPathOverride`) |
| Any tree, relative path, Tests only | Worked, but only because `Tests.csproj` carries a local workaround |

The third row is why this stayed quiet; #51's fix put a mod project into the test build graph and turned it
into the second row, breaking the documented multi-version test invocation (#46 comment, 2026-08-02).

## 2. Design

**D1 — Normalize once into a private property, derive everything from it.** `SdtdDir` is normally a *global*
property (`-p:SdtdDir=…`), and MSBuild ignores project-file assignments to global properties — so it cannot be
rewritten in place. `_SdtdRoot` holds the absolute form; the derived paths (`SdtdServerDir`, `SdtdManagedDir`,
`SdtdHarmonyDir`, `ModsDir`) are built from it, and the layout-detection `Exists()` tests it. `$(SdtdDir)`
keeps its caller-supplied value and stays what error messages echo back, which is what a reader typed.

**D2 — Relative means "relative to where you ran the command", stated explicitly.** Resolution is
`[MSBuild]::NormalizePath('$(MSBuildStartupDirectory)', '$(SdtdDir)')`, not a bare `GetFullPath`. Both behave
identically today, because evaluation happens before MSBuild retargets the working directory — but **MSBuild
points the working directory at each project while running that project's targets**, so anything implicitly
cwd-relative means one thing at evaluation and another at execution. Naming the startup directory removes the
dependency on which phase the expression happens to run in.

**D3 — `NormalizePath`, not `NormalizeDirectory`.** The latter always appends a trailing slash, which would
corrupt `SdtdServerDir` — built by string concatenation as `$(_SdtdRoot) Dedicated Server`.

**D4 (corrected 2026-08-02 — the original claim here was wrong) — `ModsDir` and `SdtdSavesDir` have the same
bug, and it is still open.** This section previously asserted they "already resolve invocation-relative
through `NormalizeDirectory` at their use sites", taken from the comment at `build/Deploy.targets:19`.
Measured, that comment is false: `dotnet build StrongMods.sln -t:Deploy -p:ModsDir=.scratch/deploy` — the
redirect CLAUDE.md documents for safe deploy verification — writes into **29 per-project `.scratch/deploy`
folders** and nothing at the repo root. Reproduced identically on unmodified `HEAD`, so it is pre-existing and
not caused by this change. The mechanism is D2's second half: those properties are consumed inside a *target*,
where the working directory is the project's own. They remain out of scope for this fix (different property,
different consumption site) and are filed separately (#52); the remedy is the same `$(MSBuildStartupDirectory)`
anchor at the `_DeployDir`/`DeployRoot` sites.

**D5 — Deletions the fix earns.** `Tests/Tests.csproj`'s four-line anchoring workaround and the "use absolute
paths until #46 lands" note in `Tests/README.md` both go: they exist only because of this bug. Keeping either
would leave a second, divergent copy of the layout-detection logic.

## 3. Verification

1. **Evaluation diff is the safety net.** `compare-eval` against `HEAD` for one project of each shape — code
   mod, modlet, overlay — with the default (absolute) install path. Must be a no-op: this change may not move
   anything for the normal case.
2. Relative `-p:SdtdDir=vendor/game/V3.1.0-b14`: a mod project builds (today: `NETSDK1052`).
3. Relative `-p:SdtdDir=vendor/dedicated-server/V3.1.0-b14`: `SdtdManagedDir` resolves to
   `7DaysToDieServer_Data` (today: silently the game layout).
4. Relative `dotnet test Tests/Tests.csproj -p:SdtdDir=vendor/…` for both units: 117/117, with the
   `Tests.csproj` workaround deleted.
5. Full solution build and full suite against the live install: unchanged.
6. Redirected deploy to `.scratch/` still lands where it should — the one target that writes outside `bin\`.

## 4. Risks

- **R1 — A path with a trailing slash** (corrected 2026-08-02; originally claimed "doubled separator, harmless").
  Measured: `NormalizePath` *preserves* a trailing separator, so `-p:SdtdDir=vendor/game/V3.1.0-b14/` yields
  `_SdtdRoot = …\V3.1.0-b14\` and `SdtdServerDir = …\V3.1.0-b14\ Dedicated Server` — a backslash-space directory
  name, i.e. broken, not cosmetic. (The doubled separator in the other derived paths is the harmless part;
  Windows path APIs tolerate it.) `HEAD` produces identical breakage for the same input, so nothing is introduced
  here; if it ever bites, the hardening is trimming trailing separators when building `_SdtdRoot`.
- **R2 — Empty `SdtdDir`** (someone passes `-p:SdtdDir=` explicitly, a global that cannot be defaulted) would
  make `GetFullPath('')` throw during evaluation, replacing a readable error with a crash. Guarded: `_SdtdRoot`
  is conditional on a non-empty `SdtdDir`, so the existing "7 Days To Die was not found at …" error from
  `build/Mod.targets` still fires.

## 5. Verification results (2026-08-02)

Every §3 check run during the pre-commit review, as an independent re-run. Environment: repo root on the dev
machine, no `Local.props`, `SDTD_HOME` unset, vendored `V3.1.0-b14` trees of both units present, `dotnet` 10
driving MSBuild.

| Check | Invocation | Result |
| --- | --- | --- |
| §3.1 evaluation no-op, default absolute path | `compare-eval` vs a `HEAD` worktree: DynamicFeralSense (mod), AECInternationalMarketFixes (modlet), Hades + StrongholdSaves (overlays), Tests | No-op. Only worktree-location noise (`TargetDir`/`TargetPath` prefix). The deleted `Tests.csproj` workaround produced byte-identical `SdtdManagedDir`/`SdtdHarmonyDir` |
| §3.2 relative game tree, mod project | `-p:SdtdDir=vendor/game/V3.1.0-b14` | Rooted under the invocation dir, game layout; `FrameworkPathOverride` rooted (was `NETSDK1052` territory) |
| §3.3 relative server tree | `-p:SdtdDir=vendor/dedicated-server/V3.1.0-b14` | `7DaysToDieServer_Data` layout detected. Reproduced the bug on `HEAD`: same command silently yields the game layout and unrooted paths |
| §3.4 suite on both units, workaround deleted | `dotnet test Tests/Tests.csproj -c Debug -p:SdtdDir=vendor/…` (relative), game then server | 117/117 and 117/117 |
| §3.5 full build against the live install | solution build + forced `-t:Rebuild` of one mod | 0 warnings, 0 errors |
| §3.6 redirected deploy | modlet `-t:Deploy -p:ModsDir=<abs>\.scratch\review46-deploy` | Mirror landed in exactly one folder at the redirect; removed after |
| D2 invocation-dir anchoring | from `DynamicFeralSense\`, `-p:SdtdDir=../vendor/dedicated-server/V3.1.0-b14` | Resolves against the invocation directory, not the project or repo root |
| R2 explicit empty override | `-p:SdtdDir=` | `_SdtdRoot` empty, evaluation survives; the readable `Mod.targets` error path is preserved |
| R1 trailing slash | `-p:SdtdDir=vendor/game/V3.1.0-b14/` | Separator preserved through `NormalizePath`; see the corrected R1 |

One pitfall for the next compare-eval run: **restore the baseline worktree before evaluating a project with
`PackageReference`s.** Unrestored, package-delivered props are missing from evaluation and the diff shows phantom
changes (`OutputType` `Library -> Exe`, testhost/xunit items) that have nothing to do with the edit under test.

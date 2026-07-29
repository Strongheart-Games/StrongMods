# Plan: F1 — SDK-style migration (`Microsoft.NET.Sdk`, `net481`)

- **Issue:** [#9](https://github.com/Strongheart-Games/StrongMods/issues/9) — status and discussion live there, not here.
- **Scope:** the 19 code mods plus `Template7DtDMod` (decided 2026-07-28, final phase). Build files only; no C#
  changes; no intended behaviour changes. Modlets are untouched — they are bare `<Project>` files with no format to
  migrate.
- **Prerequisite:** the shared-build refactor (`.ai/build-refactor-plan.md`), complete. Its explicit-import sandwich
  survives this migration unchanged; its worktree-evaluation method and redirected-build method are reused here.
- **De-risked by:** F2 (`.ai/f2-bloodrain-fresh-clone.md`), which proved the NuGet restore path on one project and
  settled BloodRain's package mechanism before this migration touches it. Per F2 §8, the pilot is a boring project
  (`StrongHorns`), **not** BloodRain.

## 1. Goal and non-goals

Convert each classic non-SDK `.csproj` to `<Project Sdk="Microsoft.NET.Sdk">` with `<TargetFramework>net481</TargetFramework>`,
deleting the `Compile` lists (SDK globs `**/*.cs`), the `ProjectGuid`s, the `ToolsVersion`/`xmlns` boilerplate, and the
explicit `Microsoft.Common.props` / `Microsoft.CSharp.targets` imports. BloodRain's
`GeneratePathProperty` + explicit `<Reference>` workaround collapses back to a bare
`<PackageReference Include="Cronos" Version="0.11.0" />` — SDK projects consume package assets natively under both
toolchains, which is exactly what the legacy format could not do under `dotnet`.

The migration must be a **behavioural no-op**: same deployed file set per mod, same `OutDir`/`TargetDir`, same
`LangVersion` (9), same `DefineConstants`, same `DebugType`, same `PlatformTarget`, same assembly-level attributes.

**Non-goals** (tracked elsewhere or deliberately excluded):

- A real Deploy target separate from Build — [#13](https://github.com/Strongheart-Games/StrongMods/issues/13).
- Exercising `dotnet new` end-to-end — [#12](https://github.com/Strongheart-Games/StrongMods/issues/12). This plan
  migrates the template's *csproj shape*; #12 covers proving the template engine itself.
- Deleting `Properties/AssemblyInfo.cs` in favour of generated attributes. The files stay;
  `GenerateAssemblyInfo=false` keeps the SDK from duplicating them (CS0579 if we forgot). Collapsing them later is a
  possible follow-on, raised as an issue only if someone wants it.
- Central package management (`Directory.Packages.props`) — still pointless for one package, and still an
  auto-imported file in a repo that deliberately has none.
- `.slnx` migration — `StrongMods.sln` stays, updated in place (decided 2026-07-28).

## 2. Target shape

### A typical code mod after conversion (~8 lines)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\build\Mod.props" />
  <PropertyGroup>
    <!-- deviations only: ModLoadPrefix, ModsDir, GameAssembly items, PlatformTarget, PackageReference -->
  </PropertyGroup>
  <Import Project="..\build\Mod.targets" />
</Project>
```

The sandwich survives because the SDK's implicit imports bracket the whole body: `Sdk.props` lands before the
`Mod.props` import, `Sdk.targets` (which pulls in `Microsoft.Common.CurrentVersion.targets` and derives
`OutDir`/`TargetDir` from `$(OutputPath)`) lands after the `Mod.targets` import. So `Mod.targets` still sets
`OutputPath` before anything latches — the same ordering argument as the legacy format, re-verified rather than
assumed (V1 queries `OutDir`/`TargetDir` per the standing corollary).

### Shared-file changes, and the mixed-format transition

Mid-migration, converted and unconverted projects import the **same** `Mod.props`/`Mod.targets`. The SDK sets
`$(UsingMicrosoftNETSdk)` = `true` in `Sdk.props`, before `Mod.props` is imported, so the shared files branch on it
during the transition:

| Concern | Legacy branch | SDK branch |
| --- | --- | --- |
| Framework | `TargetFrameworkVersion` `v4.8.1` | `TargetFramework` `net481` |
| Assembly info | — (files compiled explicitly) | `GenerateAssemblyInfo=false` |
| Output path shape | — | `AppendTargetFrameworkToOutputPath=false` |
| Debug type | `full` / `pdbonly` (already set) | same values pinned — SDK would default to `portable` |
| Framework refs | explicit `HintPath` refs (unchanged) | per the §3 decision |

Properties that are legacy-only noise under the SDK (`FileAlignment`, `ErrorReport`, `AppDesignerFolder`,
`OutputType=Library`, the `Configuration`/`Platform` defaults) move into the legacy branch and are **deleted in the
final phase** along with the branching itself, leaving clean SDK-only shared files.

Already format-neutral and untouched: `LangVersion=9` (SDK would default `net481` to 7.3 — keeping the pin is
load-bearing), `DefaultItemExcludes` for `.ai\**` (a property, so it is fully evaluated before the SDK's item globs
expand, despite being set "later" in document order), the `GameAssembly`/`Reference` items, the `Content` items, the
`OutputPath` logic, `ModDeploy`, and `VerifyGameInstall`.

### `StrongMods.sln`

As each project converts, its entry's project-type GUID flips from the classic C# GUID
(`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) to the SDK-style one (`{9A19103F-16F7-4668-BE54-9A1E7A4F7556}`), in the
same batch. The per-project GUIDs already in the `.sln` stay there even after the csprojs drop `ProjectGuid` — the
solution format requires them; the csproj does not.

## 3. The framework-references decision — settled by the pilot

Today framework types (`mscorlib`, `System`, …) compile against **the game's Managed folder** via the explicit
`HintPath` references in `Mod.targets`. An SDK-style `net4x` project additionally demands reference assemblies for
the targeting pack (`GetReferenceAssemblyPaths` fails with MSB3644 without them), which most machines do not have
installed standalone. Two candidate mechanisms, both to be tried on `StrongHorns` in Phase 1:

| | (a) `Microsoft.NETFramework.ReferenceAssemblies.net481` package | (b) `FrameworkPathOverride` → `$(SdtdManagedDir)` |
| --- | --- | --- |
| Mechanism | NuGet package supplies the official net481 reference assemblies | Point targeting-pack resolution at the game's own assemblies |
| Restore | Makes **all 19 mods** restore-aware (network once per machine) | No restore introduced |
| Compile semantics | Official reference assemblies; game refs still win where explicitly hinted | Closest to today — game assemblies throughout |
| Ecosystem | The standard, well-travelled answer | Common in game-modding, less travelled generally |
| Risk to check | Conflicts between ref-assembly `mscorlib` and the game's explicitly-hinted one (MSB3243/MSB3277); facade expansion | The game's Managed folder must satisfy everything the SDK resolves from a "targeting pack" |

**What the pilot measures for each:** resolved `ReferencePath` items diffed against the legacy baseline; build
warnings (any MSB3243/3245/3277 is a red flag); deploy-set parity; both toolchains. The decision and its evidence
get recorded here before Phase 2 rolls anything out. F2 §8 anticipated (a); (b) preserves current semantics more
faithfully — neither is presumed.

### Decision — settled by the pilot, 2026-07-28: (b) `FrameworkPathOverride`, by necessity

Option (a) **cannot compile this repo**. `StrongHorns` failed under it with CS1061 on
`ConcurrentDictionary.GetValueOrDefault` — an API the game's Unity Mono runtime provides but the official net481
reference assemblies do not contain. The repo's shipping code is written against the game's actual surface, so
official reference assemblies are not a viable compile target; this is a hard fail, not a fidelity preference.
(Direct consequence for [#15](https://github.com/Strongheart-Games/StrongMods/issues/15): CI reference assemblies
must be Refasmer'd from the game's own DLLs, not taken from `Microsoft.NETFramework.ReferenceAssemblies`.)

Under (b), the SDK pilot's post-RAR `ReferencePath` is **exactly the legacy set, every path from the game install**
— including `mscorlib` (option (a) additionally swapped `mscorlib` to the package's copy). Two references that the
legacy build injected invisibly from the machine's targeting pack are now explicit `GameAssembly` entries instead:

- `System.Core` — the legacy C# targets add it implicitly; now referenced from the game's copy (SDK builds).
  Legacy builds are verified byte-identical either way (their implicit pack copy still wins).
- `netstandard` (SDK-only) — the legacy build passed **115 references** to csc: the 10 explicit ones plus
  `System.Core` plus **104 facade DLLs** injected by `ImplicitlyExpandDesignTimeFacades` from the targeting pack.
  The SDK does no facade expansion; game assemblies type-forward through netstandard, so the game's own 2.1 shim is
  referenced explicitly. Any further facade need in later batches will surface as a readable CS0012 naming the
  assembly, and gets the same one-line explicit fix.

Net effect: SDK-converted projects need **no targeting pack at all** — strictly less machine-dependent than the
legacy build, which needed one invisibly. One nuance discovered en route: **every SDK-style project requires a
restore** (NETSDK1004 without one) even with zero packages; under (b) that restore is local-only, no network. The
missing-restore error is the readable one-liner [#11](https://github.com/Strongheart-Games/StrongMods/issues/11)
asked for — to be verified against BloodRain in Phase 4 and commented there.

## 4. Known format differences to neutralize

Each of these is a place the SDK's defaults differ from the legacy build; each has a countermeasure and a
verification that would catch it if the countermeasure fails.

1. **Compile globbing.** Default `**/*.cs` replaces the explicit lists. `.ai\**` is already excluded; `bin`/`obj`
   are excluded by the SDK itself. V1 compares the globbed `Compile` set against the baseline's explicit list —
   any extra or missing file is a finding, not noise. A stray `.cs` file on disk that was deliberately excluded
   from a `Compile` list would surface here.
2. **Duplicate assembly attributes.** `GenerateAssemblyInfo=false`, since every project keeps
   `Properties/AssemblyInfo.cs`. Failure mode is loud (CS0579), but V4 also compares resulting attributes.
3. **TFM appended to output path.** `AppendTargetFrameworkToOutputPath=false`, else Release output becomes
   `bin\Release\net481\` and any Debug fallback path grows a subfolder. V3 checks the deploy tree shape.
4. **Portable-vs-Windows PDBs.** SDK defaults `DebugType` to `portable`; the legacy build shipped `full` (Debug) /
   `pdbonly` (Release). Pinned to the legacy values for no-op-ness; whether to modernise later is out of scope.
5. **Content/None overlap.** The SDK's default `None` glob (`**/*`) also matches `ModInfo.xml`, `README.md`,
   `Config\**`, `Docs\**`, which `Mod.targets` declares as `Content`. `None` carries no copy metadata so the deploy
   set should be unaffected, but if the pilot shows duplicate-item warnings or IDE double-listing, the fix is
   `EnableDefaultNoneItems=false` (SDK branch) rather than per-file `None Remove` noise. V3 decides.
6. **Reference assembly artifacts.** SDK builds may emit `obj\ref\*.dll` (reference assemblies) and extra
   intermediate files. Harmless and gitignored, but V3 confirms nothing new leaks into the *deploy* folder.
7. **Restore-awareness.** Under §3 option (a), every project needs a restore before first build. `dotnet build`
   restores implicitly; bare `msbuild` does not — the documented command becomes `msbuild -restore`. Under (b), only
   BloodRain restores, as today. Phase 6 updates CLAUDE.md accordingly. Related: the missing-restore diagnostic that
   F2's V6 found unreadable becomes the SDK's actual "assets file not found — run restore" error, closing
   [#11](https://github.com/Strongheart-Games/StrongMods/issues/11) as a side effect if (a) is chosen — verify, then
   comment on that issue rather than silently absorbing it.
8. **Mixed-format `ProjectReference`.** `AutoCollectLoot` (legacy) → `StrongMods` (SDK) or the reverse must build
   during the transition. Both directions are supported for same-TFM libraries, but the pair converts in the same
   batch to keep the mixed window to one build, and V7 exercises it explicitly.
9. **Outlier traits carried over verbatim**, same as the shared-build refactor proved: `StrongMods`'s `000000-`
   prefix + `Noemax.GZip` + `.ai` excludes; `PrismaCoreFixes`'s `PlatformTarget=x86` + dedicated-server `ModsDir` +
   `PrismaCore` reference; the `ZZZZZZZZZZ_` prefixes; BloodRain per §1.
10. **Toolchain reality.** Verification runs on both local toolchains: Rider's full MSBuild 18.7
    (`%LOCALAPPDATA%\JetBrains\Installations\Rider253_000\tools\MSBuild\Current\Bin\MSBuild.exe`) and the
    Rider-bundled .NET SDK 10.0.201 (`...\lib\ReSharperHost\windows-x64\dotnet\dotnet.exe`). The repo has no
    `global.json`; pinning one is out of scope but noted as a risk (a future SDK major could change defaults).

## 5. Phases

Each phase is one iteration: well under the 250-line hard stop, ends with the batch's verification green, the `.sln`
entries updated for that batch, and an explicit **pause for review**. Decided 2026-07-28: pilot + batches of ~6.

| # | Work | Projects |
| --- | --- | --- |
| 0 | Baseline. `git worktree add --detach <scratch>/f1-baseline HEAD`. For every code mod: evaluation snapshot (properties incl. `OutDir`/`TargetDir`/`TargetPath`, items `Compile`/`Content`/`Reference`) and a redirected full-solution build (`-p:ModsDir=<scratch>`) capturing each mod's deployed file set (names, sizes, hashes of content files). This is the oracle for every later batch. | none changed |
| 1 | **Pilot.** Shared-file SDK branches; convert `StrongHorns`; run the §3 comparison and record the decision + evidence here. Full V1–V6 on both toolchains. | StrongHorns |
| 2 | Plain batch. | AutoCloseDoors, StrongBoxes, StrongFill, StrongLocks, LootDiagnostics, DisableLAN |
| 3 | Plain batch. | AuthZ, BountifulQuests, CustomChatCommands, StrongUtils, QuestUnlockFixes, DynamicFeralSense |
| 4 | Outlier batch — the `ProjectReference` pair together, BloodRain last. | DynamicLandClaimCount, ChatCommandHelper, StrongMods, AutoCollectLoot, PrismaCoreFixes, BloodRain |
| 5 | `Template7DtDMod` — SDK shape inside the `sourceName`/`#if (IsTemplate)` scaffolding; verify it still builds to `bin\` only and that a stripped-conditional copy (simulating `dotnet new` output) deploys normally. | Template7DtDMod |
| 6 | Cleanup + docs. Strip the legacy branches from `Mod.props`/`Mod.targets`; rewrite the affected CLAUDE.md sections (*Building*, *References* — the BloodRain do-not-simplify note collapses to a bare `PackageReference`, *Adding a new mod* — no more `Compile` lists); final full-solution redirected build on both toolchains. | shared files, CLAUDE.md |

Issue #9 is closed by the human after Phase 6 review, per the handoff workflow.

### Phase 0 results — done 2026-07-28

Baseline commit: `8308091` (the commit that added this plan). Artifacts live under `C:\Temp\f1\` — disposable but
fully reproducible from the pinned commit: `baseline\` (detached worktree), `eval-baseline\` (40 evaluation
snapshots: 19 code mods + `Template7DtDMod`, Debug and Release each), `deploy-msbuild\` / `deploy-dotnet\` plus
`manifest-msbuild.json` / `manifest-dotnet.json` (per-mod file names, sizes, SHA-256), and both build logs.

- **Harness self-check passed.** Repo `HEAD` vs the baseline worktree evaluates IDENTICAL for a sample spanning all
  outlier shapes (`StrongHorns`, `BloodRain`, `PrismaCoreFixes`, `StrongMods`), validating the oracle before
  anything changes.
- **Both toolchains build the full solution redirected, exit 0**: Rider MSBuild 18.7 and dotnet SDK 10.0.201. The
  deploy sets have **identical mod sets and file sets, with every content file byte-identical across toolchains**
  (192 files, 29 mod folders). All 38 compiled `.dll`/`.pdb` files differ across toolchains (different Roslyn —
  expected); the one identical binary is `Cronos.dll`, which comes from the NuGet cache, not a compiler.
- **Baseline warning set**: NU1503 ×10 (modlets skip restore) per toolchain pass, nothing else. "No new warnings"
  in V2 is measured against this, not against zero.

Three findings that adjust later phases:

1. **`PrismaCoreFixes` is excluded from solution builds** — its `.sln` config rows have `ActiveCfg` but no
   `.Build.0`, so solution-level builds silently skip it (pre-existing, presumably deliberate: it needs the
   dedicated-server install). Every batch's verification must build it **individually**; a green solution build
   proves nothing about it. Its `.sln` entry edit in Phase 4 must preserve the no-build state.
2. **Redirect `SavesOutputPath` too.** `StrongholdTweaks`'s `CopySaves` target (`AfterTargets="Build"`) writes to
   the real `%APPDATA%\7DaysToDie\Saves` on any solution build; only content-equality plus `SkipUnchangedFiles`
   made previous runs harmless. Protocol for every verification build:
   `"-p:ModsDir=..." "-p:SavesOutputPath=..."`. Verified the redirect catches the copy and the live saves stay
   untouched.
3. **Clean `obj\`/`bin\` before any cross-toolchain comparison.** A second toolchain building the same tree reuses
   the first's intermediates and skips its own compiler entirely (caught because 37/39 binaries came out
   byte-identical — impossible for two Roslyn versions). The first dotnet pass was redone from clean; V2's
   two-toolchain check is only meaningful from a cleaned tree.

Live client `Mods\`, server `Mods\`, and `%APPDATA%` saves all verified untouched by every build in this phase.

### Phase 1 results — done 2026-07-28

`StrongHorns` is SDK-style: the csproj is 4 lines (`Sdk` attribute + the two shared imports; no ProjectGuid, no
Compile list). The framework-references decision is settled — see §3. Changed files: `build/Mod.props` (format
branches + `FrameworkPathOverride`, `DisableImplicitFrameworkReferences`, `EnableDefaultNoneItems=false`,
`GenerateAssemblyInfo=false`, `AppendTargetFrameworkToOutputPath=false`), `build/Mod.targets` (+`System.Core`,
+`netstandard` SDK-only), `StrongHorns/StrongHorns.csproj`, `StrongMods.sln` (type GUID).

| # | Check | Result |
| --- | --- | --- |
| V1 | Targeted eval vs baseline, Debug+Release | ✅ All properties identical incl. `OutDir`/`TargetDir` (Debug deploy path byte-identical). `Compile` 5/5, `Content` 3/3 exact. Three intended diffs: duplicate `DEBUG` in `DefineConstants` (SDK appends; defines are a set — inert), explicit `System.Core` + `netstandard` reference items (§3). Release adds `RELEASE` to `DefineConstants` — no source in the repo uses `#if RELEASE` (verified by grep) |
| V2 | Both toolchains | ✅ MSBuild 18.7 and dotnet 10.0.201, redirected, exit 0, **zero warnings** (baseline NU1503s are modlet-only) |
| V3 | Deploy set | ✅ 5 files exact, content byte-identical modulo git CRLF/LF (baseline worktree checked out CRLF; the working tree is LF — a git artifact, not a build change). Release goes to `bin\Release\` with no `net481\` subfolder, `.pdb` present |
| V4 | Assembly metadata | ✅ `FileVersionInfo` identical to baseline; no generated `*AssemblyInfo*` under `obj\`; no CS0579 |
| V5 | Live installs | ✅ client, server, saves untouched by every build |
| V6 | Legacy inertness | ✅ `StrongLocks` evaluates identically (+ the one intended `System.Core` item) and **builds byte-equivalently: csc still receives the same 115 references** — the added explicit `System.Core` is inert in legacy builds (the pack copy still wins) |
| — | Mixed solution | ✅ Full `StrongMods.sln` (1 SDK + 30 legacy projects) builds under **both** toolchains, exit 0, warning set exactly at baseline, 28 deploy folders each |

Operational notes for later batches, learned here:

- Full MSBuild resolves `Microsoft.NET.Sdk` only if a .NET SDK is discoverable; **this machine has no global dotnet
  install**, so `MSBuild.exe` runs need Rider's bundled dotnet dir prefixed to `PATH`
  (`…\Rider253_000\lib\ReSharperHost\windows-x64\dotnet`). Machines with a normally-installed SDK need nothing.
  `MSBuildSDKsPath` alone does not work.
- When restore inputs change (shared-file edits, mode switches), NuGet's up-to-date check can leave a stale
  `project.assets.json` that crashes `ResolvePackageAssets` with a NullRef. Fix: delete `obj\` and re-restore.
  Batch protocol: clean `obj\`/`bin\` before verification builds (already required for cross-toolchain compares).

### Phase 2 results — done 2026-07-28

`AutoCloseDoors`, `StrongBoxes`, `StrongFill`, `StrongLocks`, `LootDiagnostics`, `DisableLAN` converted — all six
were plain (ProjectGuid + Compile list only), so each is now the same 4-line csproj as the pilot. Six `.sln` type
GUIDs flipped. No shared-file changes in this batch, so unconverted projects are untouched by construction.

| Check | Result |
| --- | --- |
| V1 ×6, Debug+Release | ✅ Exactly the pilot's accepted diff pattern for every project, nothing new. `Compile` globs reproduce every explicit list exactly (3/4/2/4/3/2 files); `Content` exact; Debug `OutDir` identical |
| V2/V3 | ✅ Full mixed solution (7 SDK + 24 legacy) builds under both toolchains, exit 0, warnings at baseline (NU1503 modlets only). All six deploy sets file-exact with content byte-identical modulo the known CRLF artifact; `StrongHorns` re-verified |
| PrismaCoreFixes | ✅ Individual redirected build (per Phase 0 protocol), deploy set OK |
| Release spot-check | ✅ `StrongBoxes` Release → `bin\Release\` correct shape, `.pdb` present |
| V5 | ✅ Client, server, saves untouched |

### Phase 3 results — done 2026-07-28

`AuthZ`, `BountifulQuests`, `CustomChatCommands`, `StrongUtils`, `QuestUnlockFixes`, `DynamicFeralSense` converted —
all plain; same 4-line shape; six `.sln` GUIDs flipped. No shared-file changes.

| Check | Result |
| --- | --- |
| V1 ×6, Debug+Release | ✅ Known accepted pattern only (checked mechanically). `Compile` globs exact, `StrongUtils` 30/30 included; `StrongUtils`'s `GetValueOrDefault` use compiles fine under the §3 decision, as expected |
| V2/V3 | ✅ Mixed solution (13 SDK + 18 legacy) builds both toolchains, exit 0, warnings at baseline; all six deploy sets exact; `PrismaCoreFixes` individual build OK |
| V5 | ✅ Client, server, saves untouched |

### Phase 4 results — done 2026-07-28

The outlier batch: `DynamicLandClaimCount`, `ChatCommandHelper`, `StrongMods`, `AutoCollectLoot`,
`PrismaCoreFixes`, `BloodRain`. Every distinguishing trait survived, verified by evaluation and redirected builds:
the `000000-`/`ZZZZZZZZZZ_` prefixes; `Noemax.GZip`; the `ProjectReference` with `Private=false` (redundant
`Project` GUID and `Name` metadata dropped — the SDK resolves by path); `PlatformTarget=x86` + the dedicated-server
`ModsDir` + the `PrismaCore` reference; and BloodRain's F2 workaround collapsed to a bare
`<PackageReference Include="Cronos" Version="0.11.0" />`.

| Check | Result |
| --- | --- |
| V1 ×6, Debug+Release | ✅ Known pattern only, plus two intended item diffs: AutoCollectLoot's `ProjectReference` metadata trim, BloodRain's explicit `Cronos` `Reference` replaced by native package-asset flow |
| V2/V3 | ✅ Mixed solution (19 SDK + 12 modlets) both toolchains, exit 0, warnings at baseline; all six deploy sets exact — **BloodRain back to the full 11-file F2 oracle**, `Cronos.dll` (`lib/net45`, 50,424) *and* `Cronos.xml` byte-identical. `Cronos.xml` needed `CopyDocumentationFilesFromPackages=true` in BloodRain.csproj — the SDK skips package doc files by default; the property only exists on the SDK asset path, which is why F2 found it inert under the legacy targets |
| V7 | ✅ Redirected solution build sends `StrongMods.dll` to the scratch `000000-StrongMods\`; `AutoCollectLoot`'s folder contains no `StrongMods.dll` (`Private=false` held) |
| PrismaCoreFixes | ✅ Individual redirected build OK; un-redirected `OutDir` still the dedicated-server path (evaluation identical to baseline); `.sln` no-build state preserved (config rows untouched) |
| [#11](https://github.com/Strongheart-Games/StrongMods/issues/11) | ✅ **Delivered by the migration.** A missing restore now fails with a single readable `NETSDK1004` "Run a NuGet package restore" under both toolchains, replacing the baseline's `-v:m`-suppressed MSB3245 + 4×CS0246. Verified with `obj\` deleted; commented on the issue |
| V5 | ✅ Client, server, saves untouched |

All 19 code mods are now SDK-style.

### Phase 5 results — done 2026-07-28

`Template7DtDMod` converted to the same 4-line shape with the `#if (IsTemplate)` / `ModDeploy=false` block
preserved inside the body; `.sln` GUID flipped; the `guids` entry in `template.json` removed (its only purpose was
regenerating the `ProjectGuid`, which no longer exists).

| Check | Result |
| --- | --- |
| Template never deploys | ✅ Both toolchains build it to `bin\Debug\` only (4 files, no `template.json`), nothing appears under a redirected `ModsDir` |
| **`dotnet new` end-to-end — first time ever exercised** | ✅ `dotnet new install` + `dotnet new 7dtdmod -n ScaffoldSmoke`: the engine strips the conditional block to exactly the canonical 4-line csproj, `sourceName` replacement works, and the generated project **builds and deploys** (redirected) with the correct 4-file set. Template uninstalled and scaffold deleted afterwards; commented on [#12](https://github.com/Strongheart-Games/StrongMods/issues/12) — its code-mod half is now covered, the modlet template remains unexercised |

### Phase 6 results — done 2026-07-28. Migration complete.

Shared files are now SDK-only: the `$(UsingMicrosoftNETSdk)` branches are gone from `Mod.props`, along with the
legacy-noise properties (`OutputType`, `AppDesignerFolder`, `RootNamespace`/`AssemblyName`, `FileAlignment`,
`ErrorReport`, `WarningLevel`, the `Configuration`/`Platform` defaults) — every one verified to evaluate
identically under SDK defaults, so deleting them produced **zero** evaluation diffs. `Mod.targets`'s header was
rewritten for the SDK sandwich (the OutDir-latching rationale is unchanged and still load-bearing), and the
`netstandard` reference lost its transition condition. CLAUDE.md rewritten where it described the old world: the
SDK project shape, globbing (including the every-`.cs`-compiles caveat), the framework-references necessity
argument, the universal restore requirement with its readable failure mode, the `MSBuild.exe` SDK-discovery note,
and the retired BloodRain do-not-simplify block.

| Check | Result |
| --- | --- |
| Evaluation sweep, all 19 code mods | ✅ Known accepted pattern only — the shared-file cleanup added nothing |
| **Final oracle: all 29 deployed mods** | ✅ Full solution + `PrismaCoreFixes`, both toolchains, from clean: exit 0, warnings at baseline, **29/29 deploy sets file-exact** against the Phase 0 manifest with content byte-identical (modulo the git CRLF artifact) |
| V5 | ✅ Client, server, saves untouched throughout |

Closing figures: the 20 converted csprojs (19 mods + the template) total **131 lines** — the shared-build refactor
took them from 2,379 to ~493; this migration takes them to 131, with no `Compile` lists left to maintain. A
converted project needs no targeting pack (the legacy build silently needed one), every toolchain path is
first-class, and `dotnet new` scaffolds SDK-style. Delivered en route: [#11](https://github.com/Strongheart-Games/StrongMods/issues/11)
closed; evidence comments on [#12](https://github.com/Strongheart-Games/StrongMods/issues/12) and
[#15](https://github.com/Strongheart-Games/StrongMods/issues/15). Issue
[#9](https://github.com/Strongheart-Games/StrongMods/issues/9) is ready to close on review.

## 6. Verification — per batch unless noted

The evaluation diff is no longer a whole-project no-op oracle (the SDK changes hundreds of properties by design), so
it narrows to a **targeted** comparison; the deploy set becomes the primary oracle.

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Targeted evaluation vs Phase 0 baseline | Identical: `OutDir`, `TargetDir`, `TargetPath`, `AssemblyName`, `RootNamespace`, `LangVersion`, `DefineConstants`, `Optimize`, `DebugSymbols`, `DebugType`, `PlatformTarget`, `OutputType`. Identical **identity sets**: `Compile`, `Content`; `Reference` per the §3 decision. Run for both Debug and Release |
| V2 | Redirected build, both toolchains | `msbuild -restore` and `dotnet build`, each `-p:ModsDir=<scratch>`, each exit 0, no new warnings (MSB3243/3245/3277 especially) |
| V3 | Deploy set vs Phase 0 baseline | Same file names per mod; content files byte-identical; `.dll`/`.pdb` present (hashes differ per build — expected); no `net481\` subfolder, no `ref\`, no `.deps.json` or other new artifacts in the deploy folder |
| V4 | Assembly metadata | Assembly-level attributes (Version, Title, etc.) match baseline — read via dotPeek or PowerShell reflection metadata. Pilot + one project per batch is sufficient |
| V5 | Live install untouched | Timestamps under the game's `Mods\` unchanged after every redirected build |
| V6 | Unconverted projects inert | One still-legacy project evaluates byte-identically before/after the batch's shared-file state (moot from Phase 5 on) |
| V7 | Mixed `ProjectReference` (Phase 4) | `AutoCollectLoot` builds redirected in whatever mixed state the batch passes through, and `StrongMods.dll` lands in the scratch folder, not the live install |
| V8 | Solution load (human) | Rider opens the solution cleanly at each pause — reported by the reviewer, not asserted by the agent |

Standing reminders carried forward: query `OutDir`/`TargetDir`, never just `OutputPath`; in bash quote the whole
switch with forward slashes — `"-p:ModsDir=C:/Temp/sdtd-verify"` — because backslashes are eaten silently.

## 7. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Framework-ref conflicts (game `mscorlib` vs reference assemblies) | The single most uncertain area; motivates the pilot decision in §3 | Pilot measures both candidates; V2 treats the conflict warnings as failures |
| Compile glob picks up a file an explicit list omitted | Would silently change an assembly | V1 compares exact `Compile` sets per project |
| Deploy folder gains SDK artifacts or a TFM subfolder | Would ship junk into the live game on the next real Debug build | `AppendTargetFrameworkToOutputPath=false`; V3 checks tree shape against baseline |
| PDB format changes under the SDK default | Portable PDBs may not serve Unity/Mono stack traces the same way | `DebugType` pinned to legacy values |
| Shared-file branching breaks an unconverted project mid-migration | The transition window spans several review pauses | `$(UsingMicrosoftNETSdk)` condition + V6 every batch |
| The two toolchains diverge (MSBuild 18.7 vs SDK 10.0.201) | Different Roslyn/NuGet versions | V2 runs both every batch, not once at the end |
| No `global.json` — future SDK majors change behaviour | Latent, not current | Noted; pinning is a separate decision, not smuggled in here |

## 8. Explicitly out of scope

Scope boundary, not a backlog: everything in §1 non-goals; any C# change; modlet projects; cleaning local `obj/`
folders that appear on unconverted machines; CI setup. Anything this work *raises* gets filed as a GitHub issue and
cited by number, per repo convention.

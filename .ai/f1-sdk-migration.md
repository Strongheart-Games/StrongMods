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

# Plan: share common `.csproj` logic across the repo

- **Status:** ✅ **complete.** All 31 projects converted; phases 1–5 done. Only the follow-ons below remain.
- **Scope:** build files only — no C# changes, no intended behaviour changes.
- **Result:** 31 `.csproj` files went from **2,379 lines to 493** (126 insertions, 2,012 deletions). The game install
  path went from ~310 hardcoded copies to one property in one file.
- **Verification:** every project diffed by MSBuild evaluation against a pristine `git worktree` baseline, then
  built. See §4 — including the two bugs and one silent-failure mode that only a real build caught.
- **Deliberate exception:** `PrismaCoreFixes` was compiling as C# 7.3 and now compiles as C# 9. That is the single
  intended compilation change in the whole refactor.

## 0. Follow-ons — nothing here is done

Captured so the reasoning isn't lost. No further analysis has been done on any of these beyond what is written.

| # | Item | Notes |
| --- | --- | --- |
| F1 | **SDK-style migration** (`<Project Sdk="Microsoft.NET.Sdk">`, `net481`) | Deliberately deferred; see §5.3. Deletes the `Compile` lists and `ProjectGuid`s and lets `BloodRain` use `PackageReference`. Needs `GenerateAssemblyInfo=false` (the `Properties/AssemblyInfo.cs` files still exist) and `AppendTargetFrameworkToOutputPath=false`. This refactor is its prerequisite and survives it unchanged — the explicit-import sandwich works in both formats. |
| F2 | **`BloodRain` cannot be built from a fresh clone** | `packages/` is gitignored but `BloodRain/packages.config` is tracked, and there is no `nuget.config`, so `..\packages\Cronos.0.11.0\...\Cronos.dll` is simply absent after cloning. Being `packages.config` rather than `PackageReference`, restoring needs `nuget.exe restore` — *not* `dotnet restore`. F1 fixes this properly; committing the ~30 KB DLL via a `.gitignore` negation is the stopgap. |
| F3 | **`dotnet new` never exercised end-to-end** | No .NET SDK on this machine, so the templates' `<!--#if (IsTemplate) -->` conditional and `symbols` block are unrun. The MSBuild half is verified and the stripped result was checked by hand. Run one real `dotnet new 7dtdmod` somewhere with the SDK. Failure mode is loud: a scaffolded project would build to `bin\` instead of deploying. |
| F4 | **A real `Deploy` target, separate from `Build`** | Debug still deploys as a side effect of building. `-p:ModsDir=...` and `<ModDeploy>false</ModDeploy>` already decouple *where*, so a `Deploy` target is now a small step rather than a redesign. |
| F5 | **Test project; Refasmer reference assemblies** | As discussed 2026-07-24. Untouched. |
| F6 | **CLAUDE.md Filesystem Scope bullet is self-contradictory** | It calls the 7DtD install directories "read-only", but a Debug build writes into `Mods\`, and this session deleted stale files there. Worth rewording so a future agent knows deploying is expected. Left alone deliberately — it is your policy statement, not mine to rewrite. |
| F7 | **Load-order prefixes left ad-hoc** | `000000-` / `Z_` / `ZZ_` / `ZZZZZZZZZZ_` preserved verbatim (§5.2). Now visible as `ModLoadPrefix` in each project instead of buried in a path string, so normalising them later is cheap — but it renames live deploy folders, so it needs a manual cleanup pass. |
| F8 | **Untracked leftovers** | `StrongMods/.ai/xml-patch-ensure-spec.md` and `xpath-inheritance-v1-spec.md` are untracked and predate this work — not mine, not touched. (`build/tools/` and `Local.props.sample` are now committed.) |

## 1. What's actually duplicated (measured, 31 projects)

Two project shapes exist:

**A. Code mods (21)** — classic non-SDK, `ToolsVersion="4.0"`. Diffed every one against
`Template7DtDMod/Template7DtDMod.csproj`: they are **byte-identical except for `ProjectGuid`, `RootNamespace`,
`AssemblyName`, `OutputPath`, and the `Compile`/`Content` file lists.** Every one repeats:

- the same 2 config `PropertyGroup`s (~20 lines: `PlatformTarget`, `DebugType`, `Optimize`, `DefineConstants`,
  `ErrorReport`, `WarningLevel`, `LangVersion`, `TargetFrameworkVersion`, `FileAlignment`, `AppDesignerFolder`);
- the same ~10 `<Reference>` blocks (~38 lines), each hardcoding
  `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\...` — **~310 copies of that absolute path repo-wide**;
- the same commented-out `BeforeBuild`/`AfterBuild` stub.

Real outliers, all small and all preservable:
| Project | Deviation |
| --- | --- |
| `BloodRain` | extra `Cronos` ref → `..\packages\` + `packages.config` |
| `StrongMods` | extra `Noemax.GZip` ref; `Mods\000000-` prefix; `DefaultItemExcludes` for `.ai\**` |
| `PrismaCoreFixes` | extra `PrismaCore` ref; `PlatformTarget=x86`; deploys to the **Dedicated Server** install; **missing `LangVersion` 9** (unintentional drift) |
| `AutoCollectLoot` | `ProjectReference` → `StrongMods` |

**B. Modlets (10)** — bare `<Project>`, no imports, hand-written `Build`/`Clean` targets that copy content. Also
near-identical, with these deviations:
| Project | Deviation |
| --- | --- |
| `Hades` | copy-if-newer `Condition` instead of `SkipUnchangedFiles`; `Clean` target commented out (would delete un-versioned world files) |
| `RefugeHordeBaseS11` | same copy-if-newer condition, carrying the comment `TODO: Copy the copy-if-newer condition to all projects` |
| `StrongholdTweaks` | second copy step to `$(AppData)\7DaysToDie\Saves` |
| `AECVehiclesFixes` / `ProgressiveBiomes` / `StrongholdTweaks` | `Z_` / `ZZ_` / `ZZZZZZZZZZ_` load-order prefixes |

Other drift worth fixing while we're in here:
- Load-order prefixes are encoded *inside* an absolute path string, so they're invisible unless you read all 31 files.
- 11 projects hardcode the mod name in `OutputPath`; 20 use `$(MSBuildProjectName)`.
- The template declares `<Reference Include="System">` **twice** — once pointing at `System.dll`, once at
  `mscorlib.dll`. 7 projects have since dropped the mscorlib one. The identity is simply mislabeled.
- `packages/` is `.gitignore`d, so `BloodRain`'s Cronos reference depends on an untracked local folder.

## 2. Target layout

```
build/
  GamePaths.props     # game install discovery — the ONE place the absolute path lives
  Mod.props           # code-mod defaults (properties)
  Mod.targets         # code-mod references + computed OutputPath
  Modlet.targets      # the shared copy Build/Clean targets
Local.props           # optional, gitignored, per-machine overrides
Local.props.sample    # tracked; copy it to Local.props
```
There is deliberately **no** `Directory.Build.props` and **no** `Directory.Build.targets` in this repo — nothing is
auto-imported. See "No auto-import" below.

`GamePaths.props` is never imported by a project directly — `Mod.props` and `Modlet.targets` pull it in, so a project
imports exactly one entry point for its shape.

### `build/GamePaths.props`
```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)..\Directory.Build.user.props"
          Condition="Exists('$(MSBuildThisFileDirectory)..\Directory.Build.user.props')" />
  <PropertyGroup>
    <SdtdDir Condition="'$(SdtdDir)' == '' AND '$(SDTD_HOME)' != ''">$(SDTD_HOME)</SdtdDir>
    <SdtdDir Condition="'$(SdtdDir)' == ''">C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die</SdtdDir>
    <SdtdServerDir Condition="'$(SdtdServerDir)' == ''">$(SdtdDir) Dedicated Server</SdtdServerDir>
    <SdtdManagedDir>$(SdtdDir)\7DaysToDie_Data\Managed</SdtdManagedDir>
    <ModsDir Condition="'$(ModsDir)' == ''">$(SdtdDir)\Mods</ModsDir>
  </PropertyGroup>
</Project>
```

### `build/Mod.targets` (imported *after* the project body, so projects can contribute)
```xml
<Project>
  <PropertyGroup>
    <ModDeployName Condition="'$(ModDeployName)' == ''">$(ModLoadPrefix)$(MSBuildProjectName)</ModDeployName>
    <OutputPath Condition="'$(Configuration)' == 'Debug'">$(ModsDir)\$(ModDeployName)\</OutputPath>
    <OutputPath Condition="'$(Configuration)' != 'Debug'">$(MSBuildProjectDirectory)\bin\$(Configuration)\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <GameAssembly Include="Assembly-CSharp;LogLibrary;UnityEngine;UnityEngine.CoreModule;
                           UnityEngine.AudioModule;System;System.Core;System.Xml;System.Xml.Linq;mscorlib" />
    <Reference Include="@(GameAssembly)">
      <HintPath>$(SdtdManagedDir)\%(Identity).dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(SdtdHarmonyDir)\0Harmony.dll</HintPath>   <!-- from $(SdtdDir), NOT $(ModsDir) -->
      <Private>False</Private>
    </Reference>
    <Content Include="ModInfo.xml;README.md" Condition="Exists(...)">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <Target Name="VerifyGameInstall" BeforeTargets="ResolveAssemblyReferences">
    <Error Condition="!Exists('$(SdtdManagedDir)\Assembly-CSharp.dll')"
           Text="7 Days To Die not found at '$(SdtdDir)'. Set SdtdDir in Directory.Build.user.props or the SDTD_HOME env var." />
  </Target>
</Project>
```
The `GameAssembly` item list means an outlier adds **one line** in its own csproj
(`<GameAssembly Include="Noemax.GZip" />`), and it also fixes the mislabeled `System`→`mscorlib.dll` duplicate.
The `VerifyGameInstall` target turns a missing install from ~200 `CS0246` errors into one readable message.

### No auto-import — everything is imported explicitly

**The constraint that forced this; it cost a failed build to find.**

`Microsoft.Common.CurrentVersion.targets` derives `OutDir`, `TargetDir` and `TargetPath` from `$(OutputPath)` *during
evaluation*, at the point it is imported. `Directory.Build.targets` is imported by `Microsoft.Common.targets` **after**
that. So setting `OutputPath` from a `Directory.Build.targets`:

- leaves `OutDir` latched to the `bin\$(Configuration)\` fallback → the assembly is written to `bin\Debug\` instead of
  the game folder, **even though `$(OutputPath)` itself reads back correct**;
- fails the build outright with `error : The BaseOutputPath/OutputPath property is not set for project ...`.

The general problem is that auto-import fixes the *position*, and one of the two positions is wrong for what we need.
So this repo uses no auto-imported files at all. A code mod imports a props file before its body and a targets file
after it:

```xml
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="..." />
<Import Project="..\build\Mod.props" />      <!-- defaults; the body overrides them -->
  ...ProjectGuid, Compile items, any ModLoadPrefix / ModsDir / GameAssembly...
<Import Project="..\build\Mod.targets" />    <!-- consumes the body; sets OutputPath -->
<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
```

Consequences, all of them wanted:
- Both project shapes use the **same mechanism**. Previously code mods would have been configured by auto-import and
  modlets by explicit import, purely because modlets don't import `Microsoft.Common.props`.
- The import **is** the opt-in, so unconverted projects are untouched — that is what lets the migration go a few
  projects at a time without silently retargeting the deploy folder of `StrongMods` (`000000-`),
  `AutoCollectLoot`/`ChatCommandHelper` (`ZZZZZZZZZZ_`) or `PrismaCoreFixes` (dedicated server) into a live install.
- It **survives the SDK-style migration**: `Directory.Build.props` lands before the body there too, so it still
  couldn't see a project-level `ModLoadPrefix`. The explicit sandwich works unchanged in both formats.
- A project's entire build story is readable from the project file.

The cost: a new project that forgets the imports gets nothing rather than everything. `Mod.targets` therefore fails
with a named error if `Mod.props` wasn't imported (verified, §4), and Phase 5 puts the imports in both templates.

**Corollary for verification: querying `$(OutputPath)` is not sufficient. Always query `$(OutDir)`/`$(TargetDir)`.**

### Per-machine overrides: `Local.props`

There is no MSBuild standard for a gitignored per-machine override file. MSBuild auto-discovers exactly three
per-directory files — `Directory.Build.props`, `Directory.Build.targets`, `Directory.Build.rsp` — and none is meant
for this. `Directory.Build.rsp` is the closest first-class fit (it would apply `-p:SdtdDir=...` automatically) but
**response files are an `MSBuild.exe` command-line feature that IDEs building in-process do not read**, so it can't
carry a setting the IDE needs.

So: a plain `Local.props` in the repo root, gitignored, imported by `GamePaths.props` behind an `Exists()` guard,
with a tracked `Local.props.sample` next to it. Root rather than `build/` because `build/` is tracked infrastructure
while this is the one file a human hand-creates per machine; it should be where they'll see it. It is deliberately
**not** named `Directory.Build.user.props` — that prefix implies MSBuild finds it automatically, which is exactly the
false implicitness this design is avoiding.

Precedence, highest first: `-p:SdtdDir=` (a global property, always wins) → `Local.props` → `SDTD_HOME` → the default
in `GamePaths.props`. All four verified in §4.

### After: a typical code mod — the real `StrongHorns.csproj`, 104 lines → 17
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"
          Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <Import Project="..\build\Mod.props" />
  <PropertyGroup>
    <ProjectGuid>{6539557B-0DA2-4767-9ADB-31210C1364BD}</ProjectGuid>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="ModApi.cs" />
    <!-- ... unchanged ... -->
  </ItemGroup>
  <Import Project="..\build\Mod.targets" />
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```
`RootNamespace`/`AssemblyName` are gone — they equal `$(MSBuildProjectName)` in every project, which `Mod.props`
now defaults. `ProjectGuid` stays (the `.sln` references it). Deviations sit above the `Mod.targets` import:
`StrongMods` adds `<ModLoadPrefix>000000-</ModLoadPrefix>` + `<GameAssembly Include="Noemax.GZip" />`, and
`PrismaCoreFixes` adds `<ModsDir>$(SdtdServerDir)\Mods</ModsDir>` + `<PlatformTarget>x86</PlatformTarget>`.

### After: a typical modlet
```xml
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <ModLoadPrefix>ZZ_</ModLoadPrefix>
  </PropertyGroup>
  <Import Project="..\build\Modlet.targets" />
</Project>
```
`Modlet.targets` adopts the copy-if-newer condition from `RefugeHordeBaseS11`/`Hades` for everyone — which closes
that project's `TODO` — and gates `Clean` behind `<ModletCleanEnabled>false</ModletCleanEnabled>` so `Hades` keeps
its un-versioned world files. `StrongholdTweaks` keeps its extra `Saves` copy via a `SavesContent` item the shared
target copies when non-empty.

## 3. Phases

CLAUDE.md caps a single iteration at 250 changed lines, and stripping 21 projects is ~1,800 deleted lines, so this is
sliced into batches. Each phase ends at a reviewable stopping point.

| # | Work | Files | Approx Δ lines |
| --- | --- | --- | --- |
| 0 | ✅ **DONE** — capture a property/item baseline as the regression oracle. Implemented as a `git worktree` of `HEAD` evaluated on demand rather than a saved text file, which made it re-runnable per batch; see §4. | none | 0 |
| 1 | ✅ **DONE** — added `build/{GamePaths.props,Mod.props,Mod.targets,Modlet.targets}` + `Local.props.sample`, gitignored `/Local.props`, converted pilots `StrongHorns` (104→18 lines) and `StrongMining` (20→4). No auto-imported files. Both **build clean**; see §4. | 5 new, 3 edited | +215 / −103 |
| 2a | ✅ **DONE** — `AutoCloseDoors`, `StrongBoxes`, `StrongFill`, `StrongLocks`, `LootDiagnostics`. All 5 evaluate identically to baseline except the intended `System`→`mscorlib` relabel; all 5 **build clean**. `Config\**\*.xml` widened to `Config\**\*` for `Localization.csv`. | 5 | −430 |
| 2b | ✅ **DONE** — `AuthZ`, `BountifulQuests`, `CustomChatCommands`, `StrongUtils`, `DisableLAN`. | 5 | −430 |
| 2c | ✅ **DONE** — `QuestUnlockFixes`, `DynamicFeralSense`, `DynamicLandClaimCount`, `ChatCommandHelper`, `AutoCollectLoot`. Both `ZZZZZZZZZZ_` prefixes reproduced via `ModLoadPrefix`; AutoCollectLoot's `ProjectReference` preserved. | 5 | −430 |
| 3 | ✅ **DONE** — `StrongMods`, `BloodRain`, `PrismaCoreFixes` (`Template7DtDMod` was handled with the templates). All evaluate identically to baseline except the `mscorlib` change and PrismaCoreFixes' `LangVersion`; all build clean. | 3 | −250 |
| 4 | ✅ **DONE** — the 8 remaining modlets (`Template7DtDModlet` went with the templates). All evaluate identically except three losing inert `CopyToOutputDirectory` metadata; all build clean. | 8 | −130 |
| — | ✅ **DONE (pulled forward from Phase 5)** — both `dotnet new` templates converted to the shared build and made **non-deploying**. See "Templates" below. | 4 | −100 |
| 5 | ✅ **DONE** — `CLAUDE.md`: rewrote *Building* (shared files, the no-auto-import constraint, references, deploying, verifying) and *Adding a new mod*; fixed the `RootNamespace`/`AssemblyName` and `.ai/` convention bullets. | 1 | +75 |

All 15 projects in Phase 2 evaluate identically to baseline except the `mscorlib` change, and all 15 **build clean**.
Six of them (`DisableLAN`, `QuestUnlockFixes`, `DynamicFeralSense`, `DynamicLandClaimCount`, `ChatCommandHelper`,
`AutoCollectLoot`) previously had **no** `mscorlib` reference and now compile against the game's Unity `mscorlib`
like the others — that was the open risk in standardising the reference list, and their clean builds settle it.

**During Phase 2 only, AutoCollectLoot needed `-p:BuildProjectReferences=false`.** It references `StrongMods`, which
was still unconverted and therefore ignored `-p:ModsDir` — a plain redirected build would have rebuilt StrongMods
straight into the live `Mods\000000-StrongMods\`. Phase 3 removed the need; verified afterwards that a plain
redirected build sends StrongMods to the scratch folder and leaves the live DLL untouched.

### Templates: build to `bin` only

The template projects were installing themselves into the live game, because their Debug `OutputPath` pointed at
`Mods\` exactly like a real mod — so building the solution deployed scaffolding as a playable mod.

The fix could not just be "hardcode `bin`": `template.json` uses `sourceName`, so the csproj **is** the scaffolding
source and any change propagates into every generated project. Instead:

1. `Mod.targets` and `Modlet.targets` gained a first-class switch — `<ModDeploy>false</ModDeploy>` makes Debug behave
   like Release and write to `bin\$(Configuration)\`. This is also a small step toward separating build from deploy.
2. Each template sets `ModDeploy=false` inside a `dotnet new` conditional:
   ```xml
   <!--#if (IsTemplate) -->
       <ModDeploy>false</ModDeploy>
   <!--#endif -->
   ```
   MSBuild treats those markers as ordinary XML comments, so the property is live in the template project itself;
   the template engine strips the whole block when scaffolding, so **generated mods deploy normally**.
3. Both `template.json` files declare the `IsTemplate` bool symbol, defaulting to `false`.
4. `Modlet.targets` now also excludes `.template.config\**\*` from `Content`, so template metadata is never shipped.

Verified: templates evaluate to `bin\Debug`, a stripped-conditional copy of each (simulating `dotnet new` output)
evaluates to the game `Mods\` folder, both templates build with exit 0 into `bin\Debug` only, the live `Mods\` folder
count is unchanged, and `template.json` is not copied to output. The stale empty `Mods\Template7DtDMod\` folder left
by earlier builds was removed.

**Not verified: `dotnet new` itself.** There is no .NET SDK on this machine (see §4), so the template engine could
not be exercised end-to-end — the conditional-block syntax and `symbols` block are unrun. Worth one real
`dotnet new 7dtdmod` on a machine that has the SDK.

Follow-ups deliberately kept out of this plan are gathered in **§0** at the top, so they don't get smuggled into a
phase.

## 4. Verification

There is no test suite, no .NET SDK, and no Visual Studio on this machine — but **Rider bundles MSBuild 18.7** at:

```
%LOCALAPPDATA%\JetBrains\Installations\Rider253_000\tools\MSBuild\Current\Bin\MSBuild.exe
```

That supports `-getProperty:` / `-getItem:`, which **evaluate** a project and print the result as JSON *without
running any target* — no compile, no copy, nothing written to the live game folder. This is the regression oracle,
and it makes each phase checkable without a build.

The diff itself is done by **`build/tools/compare-eval.py`** (Python 3.14 at `%LOCALAPPDATA%\Python\bin\python`).
It pairs each item's Identity with only its build-affecting metadata — comparing raw JSON also picks up MSBuild's
location-derived metadata (`FullPath`, `DefiningProject*`, timestamps), which necessarily differs between the repo
and the baseline worktree and buries the real diff. Exit code 0 = identical, 1 = differs, so it can gate a script.
It is a standalone tool; nothing in `build\*.props`/`*.targets` imports it.

Method used in Phase 1, repeat for every later batch:
1. `git worktree add --detach <scratch>/baseline HEAD` — a pristine pre-change tree. (MSBuild's
   `Directory.Build.props` discovery walks up from the project directory, and the scratchpad has none above it, so
   the baseline evaluates exactly as the repo did before the refactor.)
2. Evaluate before and after for: `OutputPath, LangVersion, DefineConstants, AssemblyName, RootNamespace,
   TargetFrameworkVersion, OutputType, DebugType, Optimize, DebugSymbols, PlatformTarget, WarningLevel, ErrorReport,
   FileAlignment, AppDesignerFolder` and items `Reference, Compile, Content`.
3. Diff. Also evaluate one **unconverted** project each round to prove the shared files stay inert for projects that
   have not opted in yet.

### Phase 1 results

| Check | Result |
| --- | --- |
| `StrongHorns` — 18 properties incl. `OutDir`/`TargetDir`/`TargetPath` | **all identical** to baseline |
| `StrongHorns` — `Compile` (5), `Content` (3) | **identical sets** — the `Config\**\*.xml` glob reproduced the explicit list exactly |
| `StrongHorns` — `Reference` | same 10 assemblies, same HintPaths, same `Private=False`; **one intended change**: the item labelled `System` that pointed at `mscorlib.dll` is now labelled `mscorlib` |
| `StrongLocks` (unconverted) | **byte-identical** evaluation before vs after |
| `StrongMining` — `Content` | 9 items, matching a clean checkout |
| **`StrongHorns` compile** | ✅ `-t:Rebuild` **exit 0**, redirected via `-p:ModsDir=C:\Temp\sdtd-verify`; output is exactly `StrongHorns.dll`, `.pdb`, `ModInfo.xml`, `README.md`, `Config\blocks.xml` |
| **`StrongMining` copy build** | ✅ exit 0, redirected; exactly the 9 correct files, no `bin\` leakage |
| `Local.props` override | ✅ `SdtdDir` and `ModsDir` both picked up; `git status` confirms it is ignored |
| `Local.props` precedence | ✅ `-p:ModsDir=` on the command line overrides `Local.props` |
| Missing-import guardrail | ✅ a project importing `Mod.targets` without `Mod.props` fails with the named error, not a wrong output folder |

This also confirms the `<Reference Include="@(GameAssembly)">` + `%(Identity)` metadata transform resolves correctly,
which was the one construct in the design that could not be checked by reading alone.

### Three bugs found while verifying

1. **`OutDir` latching** — see §2. Caught by the first real build; evaluation alone had reported `OutputPath` correct
   and would have shipped a design that wrote every DLL to `bin\Debug\`. Fixed by importing `Mod.targets` explicitly
   before `Microsoft.CSharp.targets`, and `Directory.Build.targets` was deleted.
2. **0Harmony resolved from `$(ModsDir)`** (pre-existing conflation, introduced into the shared file by me). Harmony
   belongs to the *game install*, not the *deploy destination*. With `-p:ModsDir=` redirected it broke immediately
   (`CS0246: HarmonyLib`), and it would have broken `PrismaCoreFixes`, which deploys to the dedicated server but
   compiles against the client's Harmony. Now `$(SdtdHarmonyDir)`, derived from `$(SdtdDir)`.
3. **Modlets shipped their own `bin\`** — the old glob `Include="**/*" Exclude="*.csproj"` matched **27** items in
   `StrongMining`, **18** of them stale build output, all copied into the live `Mods\StrongMining` on every build.
   The tell was `bin\Release\bin\Release\...` in that project: each Release build re-copying its own output one level
   deeper. `Modlet.targets` excludes `bin\**\*;obj\**\*;.ai\**\*` → the correct 9. Applies to all 10 modlets.

Also fixed incidentally: the old modlet condition `'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'` yields an **empty
`OutputPath`** when `Platform` isn't passed (building a modlet's `.csproj` directly rather than via the `.sln`). The
shared version defaults `Configuration` and does not depend on `Platform`.

### Phase 2a findings

**`BountifulQuests` is the one code mod that used a glob for `Content`:**
`<Content Include="**\*.xml;**\*.csv;**\*.md">`. That is why `Config\dialogs.xml` never appears as a literal
`Content` entry — it was shipped by the glob all along. *(An earlier pass here reported it as "missing from the
csproj and not shipped"; that was a false positive from a survey grep that only matched `Config\...` literals.
Corrected.)*

The real problem with that glob is the same one the modlets have: `**\*.xml` also matches `bin\` and `obj\`, which
is why the live `Mods\BountifulQuests\` contains a `bin\Release\bin\Release\` tree. Converting it to the shared
`ModInfo.xml` + `README.md` + `Config\**\*` set preserves the same three files in a clean tree — verified identical
against the baseline — and stops the leak.

**The bin-leak reached the live install.** ✅ **Cleaned up 2026-07-25.** Eight deployed mod folders held stray `bin\`
trees — `Hades` (46 files), `ZZZZZZZZZZ_StrongholdTweaks` (42), `StrongMining` (18), `AECInternationalMarketFixes`
(14), `PootPavillion` (14), `PlayerSpawnedTraders` (8), `BountifulQuests` (6), `Z_AECVehiclesFixes` (6): 154 files,
~35 MB. Removed, along with the stale `ProjectZFixes.dll`/`.pdb`. Phase 2/4 stop new ones appearing.

Checks run before deleting: no game or server process running; scope restricted to this repo's own 29 deployed mod
folders (the live `Mods\` also holds ~55 third-party mods — none had `bin\`/`obj\`, so none were touched); and every
file in every `bin\` tree was confirmed to be a duplicate of repo source. Two exceptions surfaced and were run down:
`StrongholdTweaks`'s stale tree still held `Config\entitygroups.xml` and
`Config\spawning_progressive_biome_difficulty.xml`, deleted from that project in commit `845d932` when they moved to
`ProgressiveBiomes`. The game reads `Config\` only at a mod's root, never inside `bin\`, so they were inert.
`Hades\Worlds\` — the un-versioned world data its disabled `Clean` target protects — sits at the mod root, outside
`bin\`, and is intact (16 files).

### Phase 3 findings

**`PrismaCoreFixes` was compiling as C# 7.3.** The original survey flagged it as the one project missing
`<LangVersion>`; the evaluation diff confirms the effective value was `7.3`, not the `9` every other project uses.
It is now `9` and still builds clean. This is the only intentional compilation change in the whole refactor.

Each outlier's distinguishing trait survived, verified by evaluation *and* by a redirected build:
- `StrongMods` — `000000-` prefix reproduced via `ModLoadPrefix`; `Noemax.GZip` added with a one-line
  `<GameAssembly Include="Noemax.GZip" />`; `Docs\*.md` still ship.
- `BloodRain` — the Cronos reference keeps **no** `<Private>` element, so `Cronos.dll` *and* `Cronos.xml` still copy
  into the mod folder; `packages.config` preserved as `None`.
- `PrismaCoreFixes` — `PlatformTarget=x86` held (Mod.props sets AnyCPU before the body, the body wins), and
  `<ModsDir>$(SdtdServerDir)\Mods</ModsDir>` sends it to the **dedicated server** install while `PrismaCore.dll`
  resolves from `$(SdtdServerDir)\Dependencies_3.0\`. Confirmed the un-redirected `OutDir` is still the server path.

**The `-p:BuildProjectReferences=false` workaround is no longer needed.** With `StrongMods` converted, a redirected
build of `AutoCollectLoot` now sends its `ProjectReference` output to the scratch folder too — verified `StrongMods.dll`
appeared under `C:\Temp\sdtd-verify\000000-StrongMods\` and the live copy's timestamp was unchanged.

**Watch the shell quoting.** In bash, `-p:ModsDir=C:\\Temp\\sdtd-verify` loses its backslashes and MSBuild reads it
as a *relative* path, silently writing output into `<Project>\Tempsdtd-verify\` instead of the scratch folder — the
build still reports success. Use forward slashes and quote the whole switch: `"-p:ModsDir=C:/Temp/sdtd-verify"`.

### Phase 4 findings

Five modlets collapsed to a single `<Import>` line. The three with special needs kept them locally:

- `Hades` — `<ModletCleanEnabled>false</ModletCleanEnabled>`, replacing a commented-out `Clean` target. Verified
  both directions against a redirected copy: `-t:Clean` on Hades leaves all 23 files intact, while the same target
  on `PootPavillion` removes its folder as before. That protects the un-versioned `Worlds\` data.
- `StrongholdTweaks` — the only modlet that installs somewhere other than `Mods\`. It keeps its own `CopySaves`
  target (`AfterTargets="Build"`) plus `<ModletContentExclude>Saves\**\*</ModletContentExclude>`, rather than
  complicating `Modlet.targets` for one case. `SavesOutputPath` is now overridable, so it can be tested without
  writing into the player's real saves folder — verified the live `%APPDATA%\7DaysToDie\Saves` was untouched.
- `AECVehiclesFixes`, `ProgressiveBiomes` and `Hades` each lost a `<Content Update="...">` setting
  `CopyToOutputDirectory=PreserveNewest`. **That metadata was always inert**: the modlet `Copy` task copies
  `@(Content)` wholesale and never reads it. It is the only difference the evaluation diff reports for those three,
  and the deployed file sets are unchanged.

Deployed file counts match the evaluated `Content` counts exactly for all eight (7/7, 3/3, 23/23, 4/4, 7/7, 5/5, 9/9,
19/19), which also confirms the copy-if-newer condition — now used by every modlet, where six previously used
`SkipUnchangedFiles` — copies a full tree correctly. No `bin\`/`obj\` and no `.template.config` leaked into any
deploy.

**Every project in the repo is now on the shared build.**

### Gap in this plan: `ProjectZFixes`

`ProjectZFixes` was missed when the phases were drafted — the repo has **21** code-mod-shaped projects and the
phase table only accounts for 20. It is a hybrid: a code-mod csproj (`Microsoft.Common.props` +
`Microsoft.CSharp.targets`, `OutputType=Library`, deploys to `Mods\ZZ_ProjectZFixes`) with **no `.cs` files, no
`<Compile>` items and no `<Reference>` items at all** — XML-only content that nonetheless emits an empty 3.5 KB
`ProjectZFixes.dll` into the live install. It also lacks `LangVersion`, like `PrismaCoreFixes`.

✅ **Resolved — converted to a modlet** (decided 2026-07-25), since that is what it actually is. Now 8 lines with
`<ModLoadPrefix>ZZ_</ModLoadPrefix>`. Deploys the same 7 files as before (`ModInfo.xml`, `README.md`, 5 under
`Config\`) and no longer emits the empty assembly; build verified redirected. **Manual cleanup needed:** the stale
`ProjectZFixes.dll` and `.pdb` in the live `Mods\ZZ_ProjectZFixes\` must be deleted by hand, otherwise the game
keeps loading a do-nothing assembly.

That brings the repo to 19 code mods + 12 modlets (was 21 + 10; `ProjectZFixes` moved, and `StrongMining` was a
modlet all along).

## 5. Decisions — settled 2026-07-25

1. **Location of this doc** — root `.ai/`. Repo-wide plans have no single owning project; per-project plans continue
   to live in `<Project>/.ai/` per CLAUDE.md.
2. **Load-order prefixes** — **keep verbatim.** `ModLoadPrefix` carries the existing `000000-` / `Z_` / `ZZ_` /
   `ZZZZZZZZZZ_` strings unchanged. Renaming would rename live folders under the game's `Mods\`, orphaning deployed
   copies and risking duplicate loads. This is what keeps the refactor a provable no-op.
3. **SDK-style migration** — **deferred to a separate pass.** This plan is its prerequisite. Rationale: this refactor
   is verifiable by evaluated-property diff alone; SDK-style changes assembly-info generation and output-path shape,
   so it needs real compile verification, which is not currently runnable from the agent shell (see §4).
4. **Game path** — per-machine override + `SDTD_HOME` env var, defaulting to the current hardcoded path.
   *Revised during Phase 1:* the override file is `Local.props`, not `Directory.Build.user.props` — see §2. Decided
   alongside dropping auto-import entirely.
5. **Stale index entries** — resolved; see §6.

## 6. Stale index entries — resolved 2026-07-25

`Hades_World/Hades_World.csproj` and `ProjectZBiomeSpawnAdjustments/ProjectZBiomeSpawnAdjustments.csproj` were staged
as added (`AD`) with their directories already gone. Both were byte-identical, unmodified copies of
`Template7DtDModlet.csproj` (951 bytes), never committed, never in `StrongMods.sln`, with no accompanying source —
abandoned scaffolding. Dropped via `git rm --cached`. Nothing lost; the content is reproducible from the template.

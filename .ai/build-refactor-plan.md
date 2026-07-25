# Plan: share common `.csproj` logic across the repo

Status: **proposed — not started.** Scope: build files only. No C# changes, no behavior changes.

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
Directory.Build.props # auto-imported by the 21 code mods -> imports GamePaths + Mod.props
Directory.Build.targets # auto-imported -> imports Mod.targets
Directory.Build.user.props  # optional, gitignored, per-machine game path override
```

Why this split rather than only `Directory.Build.props`: the modlets are bare `<Project>` files with no
`Microsoft.Common.props` import, so **MSBuild's auto-import does not reach them**. They need one explicit
`<Import>`, and `build/GamePaths.props` gives both shapes a single source of truth for the install path.

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
      <HintPath>$(ModsDir)\0_TFP_Harmony\0Harmony.dll</HintPath>
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

### After: a typical code mod
```xml
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="..." />
  <PropertyGroup>
    <ProjectGuid>{...}</ProjectGuid>
    <RootNamespace>StrongHorns</RootNamespace>
    <AssemblyName>StrongHorns</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..." />   <!-- unchanged -->
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```
~95 lines → ~15. `StrongMods` adds `<ModLoadPrefix>000000-</ModLoadPrefix>` + one `GameAssembly` line;
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
| 0 | Capture a **property/item baseline**: for each project, record evaluated `OutputPath`, `DefineConstants`, `LangVersion`, `TargetFrameworkVersion` and the resolved `Reference` set to a text file in the scratchpad. This is the regression oracle for every later phase. | none (script only) | 0 |
| 1 | Add `build/*.props|targets`, `Directory.Build.props|targets`, gitignore `Directory.Build.user.props`. Convert **one** pilot code mod (`StrongHorns` — no outlier traits) and **one** pilot modlet (`StrongMining`). Re-run the baseline; diff must be empty except intended fixes. | ~6 new, 2 edited | +150 / −170 |
| 2 | Convert remaining plain code mods in batches of ~5: (a) `AutoCloseDoors`, `StrongBoxes`, `StrongFill`, `StrongLocks`, `LootDiagnostics` (b) `AuthZ`, `BountifulQuests`, `CustomChatCommands`, `StrongUtils`, `DisableLAN` (c) `QuestUnlockFixes`, `DynamicFeralSense`, `DynamicLandClaimCount`, `ChatCommandHelper`, `AutoCollectLoot`. Diff the baseline after each batch. | 15 | −80/batch |
| 3 | Convert the 4 outliers one at a time: `StrongMods` (load prefix + Noemax + `.ai` exclude), `BloodRain` (Cronos), `PrismaCoreFixes` (x86 + server path + **gains `LangVersion` 9**, so it recompiles — verify separately), `Template7DtDMod`. | 4 | −80 |
| 4 | Convert the 9 remaining modlets + `Template7DtDModlet`. | 10 | −150 |
| 5 | Update `CLAUDE.md` ("Building" and "Adding a new mod" sections) and the two `dotnet new` templates so scaffolding produces the short form. | 3 | +40 |

Optional follow-ups, **not** in this plan — call them out now so they don't get smuggled in:
- **SDK-style migration** (`<Project Sdk="Microsoft.NET.Sdk">`, `net481`) — would delete the `Compile` lists and
  `ProjectGuid`s entirely and let `BloodRain` use `PackageReference`. Needs `GenerateAssemblyInfo=false` (the
  `Properties/AssemblyInfo.cs` files still exist) and `AppendTargetFrameworkToOutputPath=false`. The layout above is
  a prerequisite and survives that migration unchanged, so this is the right ordering either way.
- **Separating build from deploy** (build to `bin\`, deploy via a target/script) — the reason `Debug` is currently
  unbuildable while the game runs. After this refactor the deploy root is a single property, so
  `-p:ModsDir=<somewhere>` already redirects a whole build; a `Deploy` target is a small follow-on.
- Refasmer reference assemblies, test project — as previously discussed.

## 4. Verification

There is no test suite, and **no .NET SDK, MSBuild, or Rider install is reachable from the agent shell on this
machine** (`dotnet`, `msbuild`, `%ProgramFiles%\dotnet`, VS 2022, and JetBrains Toolbox all absent from PATH//disk
as far as the agent can see) — so compilation must be verified by the user in Rider, or by pointing me at the
MSBuild path.

Two-tier verification:
1. **No-compile diff (agent-runnable, if MSBuild is reachable).** `msbuild <proj> -preprocess:out.xml` and
   `-getProperty:` / `-getItem:Reference` produce the fully-evaluated project without building. Compare against the
   Phase-0 baseline; a correct refactor is a **no-op** at this level. This catches every path/property/reference
   regression without touching the live game folder.
2. **Compile check that does not deploy.** Because Debug output is now `$(ModsDir)\...`, a build can be redirected
   with `-p:ModsDir=<scratchpad>` — so compilation can be verified without writing into the live `Mods\` folder or
   colliding with a running server. Per standing preference I will **ask before running any build**.

## 5. Decisions — settled 2026-07-25

1. **Location of this doc** — root `.ai/`. Repo-wide plans have no single owning project; per-project plans continue
   to live in `<Project>/.ai/` per CLAUDE.md.
2. **Load-order prefixes** — **keep verbatim.** `ModLoadPrefix` carries the existing `000000-` / `Z_` / `ZZ_` /
   `ZZZZZZZZZZ_` strings unchanged. Renaming would rename live folders under the game's `Mods\`, orphaning deployed
   copies and risking duplicate loads. This is what keeps the refactor a provable no-op.
3. **SDK-style migration** — **deferred to a separate pass.** This plan is its prerequisite. Rationale: this refactor
   is verifiable by evaluated-property diff alone; SDK-style changes assembly-info generation and output-path shape,
   so it needs real compile verification, which is not currently runnable from the agent shell (see §4).
4. **Game path** — **`Directory.Build.user.props` + `SDTD_HOME` env var**, defaulting to the current hardcoded path.
   Adds `Directory.Build.user.props` to `.gitignore` in Phase 1.
5. **Stale index entries** — resolved; see §6.

## 6. Stale index entries — resolved 2026-07-25

`Hades_World/Hades_World.csproj` and `ProjectZBiomeSpawnAdjustments/ProjectZBiomeSpawnAdjustments.csproj` were staged
as added (`AD`) with their directories already gone. Both were byte-identical, unmodified copies of
`Template7DtDModlet.csproj` (951 bytes), never committed, never in `StrongMods.sln`, with no accompanying source —
abandoned scaffolding. Dropped via `git rm --cached`. Nothing lost; the content is reproducible from the template.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A monorepo of ~25 mods for the game **7 Days to Die** (a dedicated-server / Unity title). Each top-level directory
(except `build`, `Template*` and `packages`) is one independent mod, and each is a separate C# class-library project
(`.csproj`) targeting **.NET Framework 4.8.1**, **C# LangVersion 9**. All projects are listed in `StrongMods.sln`. See
`README.md` for the one-line description of each mod.

A shipped mod is a directory in the game's `Mods/` folder containing a compiled DLL, a `ModInfo.xml` manifest, and
optionally a `Config/` folder of XML patches. Most projects here are code mods; some are XML-only ("modlets"). The two
`Template7DtD*` directories are `dotnet new` templates for scaffolding a new mod of each kind, not shippable mods
themselves.

## Building

### Shared build files

Every project gets its settings from `build/`. Individual `.csproj` files carry only what is unique to them —
`ProjectGuid`, the `Compile` list, and any deviation. A modlet is 4 lines; the median project is 16; the largest
(`StrongUtils`, 43) is long only because of its `Compile` list.

| File | Role |
| --- | --- |
| `build/GamePaths.props` | The **one** place the game install path lives. Defines `$(SdtdDir)`, `$(SdtdServerDir)`, `$(SdtdManagedDir)`, `$(SdtdHarmonyDir)`, `$(ModsDir)`. Not imported directly by projects — the two entry points below pull it in. |
| `build/Mod.props` | Code-mod defaults. Imported **before** the project body, so the body overrides it. |
| `build/Mod.targets` | Code-mod references, content and `OutputPath`. Imported **after** the body. |
| `build/Modlet.targets` | The whole build for an XML-only modlet: a content copy plus `Clean`. |
| `build/tools/compare-eval.py` | Verification helper; not imported by MSBuild. See *Verifying* below. |

**Nothing is auto-imported — there is deliberately no `Directory.Build.props`/`.targets`, and adding one is a
mistake.** `Microsoft.Common.CurrentVersion.targets` derives `OutDir`/`TargetDir` from `$(OutputPath)` *during
evaluation*, so a `Directory.Build.targets` is imported too late: `$(OutputPath)` reads back correct while `OutDir`
stays latched at the `bin\` fallback and the assembly lands in the wrong place. Import position is therefore
explicit and load-bearing. The header comment in `build/Mod.targets` has the full story.

A code mod imports the props file after `Microsoft.Common.props` and the targets file before
`Microsoft.CSharp.targets`:

```xml
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="..." />
<Import Project="..\build\Mod.props" />
  <!-- ProjectGuid, Compile items, any ModLoadPrefix / ModsDir / GameAssembly -->
<Import Project="..\build\Mod.targets" />
<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
```

A modlet imports one file: `<Import Project="..\build\Modlet.targets" />`.

### References

Game assemblies resolve from `$(SdtdManagedDir)`, and `0Harmony.dll` from `$(SdtdHarmonyDir)` — derived from
`$(SdtdDir)`, **not** from `$(ModsDir)`, so redirecting the deploy target never breaks compilation. **There is no
NuGet restore for these**; the game must be installed for a build to resolve references, and `build/Mod.targets`
raises one readable error if it is not. To add a game assembly to a project: `<GameAssembly Include="Noemax.GZip" />`.

`BloodRain` has the repo's one real NuGet dependency (Cronos). It is a `PackageReference`, so **any standard
toolchain restores it** — `dotnet build` restores implicitly, `msbuild -restore` does it in one invocation, and IDEs
restore on load. No `nuget.exe`, no `packages.config`, and a fresh clone builds. The gitignored `packages/` folder is
a leftover from the old `packages.config` mechanism; nothing reads it, and it will not exist on a fresh clone.

The reference is deliberately routed through `GeneratePathProperty` plus an explicit `<Reference>`:

```xml
<PackageReference Include="Cronos" Version="0.11.0" GeneratePathProperty="true" ExcludeAssets="all" />
<Reference Include="Cronos">
  <HintPath>$(PkgCronos)\lib\net45\Cronos.dll</HintPath>
</Reference>
```

**Do not simplify that to a bare `<PackageReference>` while the project is non-SDK.** A legacy project turns restored
assets into references via `ResolveNuGetPackageAssets`, which ships with full MSBuild but **not** with the .NET SDK.
A bare `PackageReference` therefore builds fine under `msbuild.exe` and fails with `CS0246` under `dotnet build` —
which restores the package correctly and then ignores it. Routing through a `HintPath` works under both, and keeps
`Cronos.dll`/`Cronos.xml` copying into the mod folder. The SDK-style migration will remove the need for this shape.

### Deploying

**Debug builds deploy straight into the live game**: `OutputPath` is `$(ModsDir)\$(ModDeployName)\`, so building in
Debug *is* the install step. Release builds go to `bin\Release\`.

- `<ModLoadPrefix>ZZ_</ModLoadPrefix>` prefixes the deploy folder to force load order.
- `<ModsDir>$(SdtdServerDir)\Mods</ModsDir>` targets the dedicated server instead (`PrismaCoreFixes` does this).
- `<ModDeploy>false</ModDeploy>` never deploys; Debug goes to `bin\Debug\` (both templates do this).
- `-p:ModsDir=...` on the command line redirects an entire build, which is how to **compile without touching the
  live install**. Prefer this when the game or server may be running.

```bash
dotnet build StrongMods.sln -c Debug                                  # build & deploy everything
dotnet build DynamicFeralSense/DynamicFeralSense.csproj -c Debug      # one mod
dotnet build StrongMods.sln -c Debug -p:ModsDir=C:/Temp/verify        # build without deploying
```

Per-machine overrides (a different install path, a permanent redirect) go in a gitignored `Local.props` in the repo
root — copy `Local.props.sample`. Precedence: `-p:` → `Local.props` → `SDTD_HOME` → the default.

### Verifying

There is no test project or linter. Two levels beyond running the game:

1. **Evaluation diff, no build.** `msbuild <proj> -getProperty:... -getItem:...` prints a project's resolved settings
   as JSON without running any target — no compile, no copy, nothing written to the game. Diff that against a
   `git worktree` of `HEAD` to prove a `.csproj` change is a no-op. `build/tools/compare-eval.py` does the diff;
   its docstring has the usage and the pitfalls. **Always query `OutDir`/`TargetDir`, not just `OutputPath`.**
2. **A real build**, redirected with `-p:ModsDir=...` so it cannot disturb the live install.

**StrongMods loads first**, via `<ModLoadPrefix>000000-</ModLoadPrefix>` — the prefix forces it ahead of other mods
in load order, which matters because it replaces the XML patcher (see below).

## Architecture

### The mod entry point (Harmony)

Every code mod exposes a class implementing the game's `IModApi` interface. Its `InitMod(Mod)` constructs a
`Harmony` instance and calls `harmony.PatchAll(...)` to apply every `[HarmonyPatch]` in the assembly. This is the
near-universal shape (see `StrongMods/ModApi.cs`, `DynamicFeralSense/HarmonyPatches.cs`). Mods change game behavior in
two ways:

- **Harmony patches** on game types — prefixes/postfixes, and transpilers for surgical IL edits
  (`DynamicFeralSense/HarmonyPatches.cs` is a good transpiler example using `CodeMatcher`).
- **XML config patches** in a `Config/` folder, using vanilla XPath patch commands (`append`, `set`, `remove`, …)
  plus the `<foreach>` extension below.

Game types (`ConsoleCmdAbstract`, `Mod`, `Log`, `GameManager`, `SdtdConsole`, `WorldStaticData`, entity classes, etc.)
come from the referenced `Assembly-CSharp.dll` — they are not in this repo. `Log.Out/Warning/Error` is the game's
logger; prefix messages with `[ModName]`.

### `StrongMods` — the core project

This is the foundational mod other mods depend on (only cross-project reference in the repo:
`AutoCollectLoot` → `StrongMods` via `ProjectReference`). It provides two things:

1. **A breadth-first XML patcher** (`BreadthFirstXmlPatcher.cs`). Vanilla patches file-major (every mod's patch for
   `items.xml`, then every mod's patch for `entityclasses.xml`, …), which makes cross-file reads during patching
   unreliable. StrongMods replaces `WorldStaticData.LoadAllXmlsCo` (via Harmony) with a mod-major pass:
   for each mod in load order, patch every file. The class doc-comment explains the three-phase design in detail.
   **Consequence for load order:** a `<foreach>` can see vanilla XML and any mod *earlier* in load order, but not mods
   *after* it.

2. **The `<foreach>` XML-patch templating engine** (`XmlPatchMethodForeach.cs`) — loop/`<bind>` table/`<function>`
   constructs usable inside patch files. **`StrongMods/Docs/foreach.md` is the complete spec** (it ships as mod
   content); read it before touching foreach logic. C# helper functions callable from patches must be tagged with
   `[XmlPatchFunction]` (`XmlPatchFunctionAttribute.cs`) and be `public static`, return `string`, take only
   `string` params.

### `StrongUtils` — shared administration/modding grab-bag

Not a library the others link against — it's its own standalone mod bundling many small server features and reusable
pieces. Notable shared infrastructure worth reusing:

- `ConfigManager.cs` — singleton (`ConfigManager.Instance`, `Init(dir)`) that registers XML config files with defaults
  and optional hot-reload via `FileSystemWatcher`.
- `Commands/` — server console commands, each a `ConsoleCmdAbstract` subclass (see
  `Commands/GracefulShutdownCommand.cs` for the standard shape: `getCommands`, `getDescription`, `getHelp`,
  `Execute`). The game auto-discovers these; no registration needed.
- `KeyValueStore/` — a small persistence abstraction (`IKeyValueStore`, XML-backed impl).
- Chat helpers (`Chat.cs`), audit logging (`StrongAudit.cs`), server lifecycle hooks (`ServerLifecycle.cs`).

## Conventions

- **Formatting is enforced by `.editorconfig`** (2-space indent, LF, max line 120, `charset=utf-8`, K&R-style braces —
  `csharp_new_line_before_open_brace = none`). `var` only when the type is apparent; use language keyword types (`int`,
  not `Int32`); avoid `this.` qualification; constants in `PascalCase`.
- **In Markdown, readability outranks the 120-column limit.** Wrap prose at 120, but **Markdown table rows are
  exempt** — a table is easier to scan than the list it would become, so never reflow a table, convert one to
  bullets, or truncate its cells just to fit the limit. `.editorconfig` cannot express this (it has no notion of
  "inside a table"), so the rule lives here. Same applies to long URLs and code-block lines that cannot be broken.
- **Don't fake a table with consecutive `Label: value` lines.** Markdown joins adjacent lines into one paragraph, so
  they render as an unreadable run-on. Use a real table, or a bullet per field — never bare label lines. This
  applies especially to status/metadata headers at the top of a doc.
- **Namespaces match the project/assembly name.** `build/Mod.props` defaults `RootNamespace` and `AssemblyName` to
  `$(MSBuildProjectName)`, so a project should not set them; the directory name *is* the mod name.
- `ModInfo.xml` is UTF-8-with-BOM and declares `Name`, `Version`, `DisplayName`, `Description`, `Author`
  (`str0ngh34rt`). Bump `Version` when shipping behavior changes.
- AI artifacts such as specs and handoff docs can be found in the `.ai/` directory of the relevant project, or in the
  repo-root `.ai/` when the work spans the whole repo (e.g. `.ai/build-refactor-plan.md`).
- **The backlog lives in GitHub Issues, not in documents.** A plan doc explains *why* — the design, the options
  weighed, the verification. The issue carries the work and its status. **Never add a status or follow-on table to a
  doc:** it becomes a second tracker, and two trackers always drift. Raise work as an issue and cite it by number.
  The older plans keep a `§0` crosswalk purely because their prose cites legacy `F` identifiers; that table maps IDs
  to issues and deliberately carries no status.
- While most projects have little or no docs yet, we strive to put a README.md in the root of each project and
  supporting detailed docs in its `Docs/` directory

## Adding a new mod

Scaffold from a template (`Template7DtDMod` for a code mod, `Template7DtDModlet` for XML-only), then add the project
to `StrongMods.sln`. The template already imports the shared build files, so there is **no** reference block,
property group or `OutputPath` to copy, and no `Content` entries to declare — `ModInfo.xml`, `README.md`,
`Config\**\*` and `Docs\**\*` are picked up automatically for code mods, and a modlet ships its whole directory.

Scaffold into the repo root: the imports are relative (`..\build\...`), so a project one level down resolves them.

For a code mod, add each new `.cs` file to the `Compile` list — these are classic `.csproj` files, so there is no
globbing. Deviate from the defaults only where needed, above the `Mod.targets` import: `ModLoadPrefix` for load
order, `ModsDir` to target the dedicated server, `GameAssembly` for an extra game DLL.

The templates set `<ModDeploy>false</ModDeploy>` inside a `<!--#if (IsTemplate) -->` block so they never install
themselves into the game. `dotnet new` strips that block, so generated projects deploy normally — leave it alone.

## Agent Workflow & Workstyle Constraints

**Core Directive: Small, Atomic Changes**
You must strictly adhere to principles for creating small, reviewable, and single-focused changes. Every code generation
cycle must produce self-contained edits.

**Strict Limits & Constraints**

* **Filesystem Scope:** Work only within this project directory and the 7 Days to Die install directories (read-only,
  for vanilla configs and game DLLs). Treat everything else as out of scope.
* **Size Target:** Aim for ~100 lines of changed code (excluding auto-generated files or structural configuration
  boilerplate).
* **Hard Stop:** Do not modify more than 250 lines of code across a single iteration loop.
* **Single Focus:** Address exactly ONE logical bug fix, ONE task, or ONE discrete component feature. Never combine
  functional changes with refactoring.
* **Isolation:** When tasked with updating a standalone mod, do not modify the foundational `StrongMods` core project
  unless explicitly requested.
* **Git**
  * Stage only the files you intentionally modified.
  * Do not commit, push, or rewrite Git history. These are also blocked by permission deny rules.
* **Issues**
  * File and update issues with the `gh` CLI, against
    [Strongheart-Games/StrongMods](https://github.com/Strongheart-Games/StrongMods/issues).
  * Label with a `type:` facet, plus `scope:repo-wide` or `mod:<Name>` for where it applies. Priority is **not** a
    label — ranking lives on the Project board so there is only one ordering.
  * Resolve by **closing**, never by deleting. Deleting and transferring issues are blocked by permission deny rules,
    and the bot account lacks the admin rights to do either.
  * Agents authenticate as a dedicated bot account, configured per machine via `GH_CONFIG_DIR`. Do not assume that
    identity is active — confirm with `gh auth status` before writing, since an unset variable silently falls back to
    the human owner's credentials.

### Required Agent Workflow

**1. Planning Phase**

* Before making edits that touch multiple files, write a brief, itemized plan and save it in the relevant project's
  `.ai/` directory (e.g., `DynamicFeralSense/.ai/plan.md`). Do not pollute the root directory.
* Request explicit human validation on this plan if the proposed changes will exceed the 100-line target.

**2. Implementation Phase**

* Do not batch multiple independent modifications. If you notice an unrelated bug or a refactoring opportunity while
  coding, leave it alone and note it in a summary instead of fixing it now.
* Keep any structural modifications or configuration updates isolated from your core logic implementation.

**3. Verification Phase**

* There is no automated test suite or linter for this repository. Verification is handled via compilation.
* You must run the specific build command for the mod you are working on to ensure it compiles perfectly against the
  game's DLLs (e.g., `dotnet build <ProjectName>/<ProjectName>.csproj -c Debug`).

**4. Handoff & Review Phase**

* Upon a successful build, explicitly PAUSE your workflow.
* Present a brief, clear summary of the changes made.
* Wait for manual code review and explicit human approval before taking any further action. Do not automatically commit,
  push, or move on to the next task.

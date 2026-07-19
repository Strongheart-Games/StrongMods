# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A monorepo of ~25 mods for the game **7 Days to Die** (a dedicated-server / Unity title).
Each top-level directory (except the `Template*` and `packages` dirs) is one independent mod, and each
is a separate C# class-library project (`.csproj`) targeting **.NET Framework 4.8.1**, **C# LangVersion 9**.
All projects are listed in `StrongMods.sln`. See `README.md` for the one-line description of each mod.

A shipped mod is a directory in the game's `Mods/` folder containing a compiled DLL, a `ModInfo.xml`
manifest, and optionally a `Config/` folder of XML patches. Most projects here are code mods; some are
XML-only ("modlets"). The two `Template7DtD*` directories are `dotnet new` templates for scaffolding a new
mod of each kind, not shippable mods themselves.

## Building

Projects reference the game's managed DLLs directly via absolute `HintPath`s under
`C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\`, and the `0Harmony.dll` from the game's
`Mods\0_TFP_Harmony\`. **There is no NuGet restore for these**; the game must be installed at that path
for a build to resolve references. `packages/` holds the handful of real NuGet deps (e.g. Cronos).

**Debug builds deploy straight into the live game.** Each `.csproj`'s Debug `OutputPath` points at the
game's `Mods\<ModName>` folder — so building in Debug *is* the install step. `ModInfo.xml` (and any doc/config
`Content`) is copied next to the DLL via `CopyToOutputDirectory`. Release builds go to the local `bin\Release\`.

Build with MSBuild or `dotnet build` (the repo uses classic-style `.csproj`, not SDK-style):

```bash
dotnet build StrongMods.sln -c Debug              # build & deploy everything
dotnet build DynamicFeralSense/DynamicFeralSense.csproj -c Debug   # one mod
```

There is no test project or linter step in this repo. Verification is done by running the game/server.

**StrongMods loads first.** Its Debug `OutputPath` is `Mods\0000_StrongMods` — the `0000_` prefix forces it
ahead of other mods in load order, which matters because it replaces the XML patcher (see below).

## Architecture

### The mod entry point (Harmony)

Every code mod exposes a class implementing the game's `IModApi` interface. Its `InitMod(Mod)` constructs a
`Harmony` instance and calls `harmony.PatchAll(...)` to apply every `[HarmonyPatch]` in the assembly. This is
the near-universal shape (see `StrongMods/ModApi.cs`, `DynamicFeralSense/HarmonyPatches.cs`). Mods change game
behavior in two ways:

- **Harmony patches** on game types — prefixes/postfixes, and transpilers for surgical IL edits
  (`DynamicFeralSense/HarmonyPatches.cs` is a good transpiler example using `CodeMatcher`).
- **XML config patches** in a `Config/` folder, using vanilla XPath patch commands (`append`, `set`, `remove`, …)
  plus the `<foreach>` extension below.

Game types (`ConsoleCmdAbstract`, `Mod`, `Log`, `GameManager`, `SdtdConsole`, `WorldStaticData`, entity classes,
etc.) come from the referenced `Assembly-CSharp.dll` — they are not in this repo. `Log.Out/Warning/Error` is the
game's logger; prefix messages with `[ModName]`.

### `StrongMods` — the core project

This is the foundational mod other mods depend on (only cross-project reference in the repo:
`AutoCollectLoot` → `StrongMods` via `ProjectReference`). It provides two things:

1. **A breadth-first XML patcher** (`BreadthFirstXmlPatcher.cs`). Vanilla patches file-major (every mod's patch
   for `items.xml`, then every mod's patch for `entityclasses.xml`, …), which makes cross-file reads during
   patching unreliable. StrongMods replaces `WorldStaticData.LoadAllXmlsCo` (via Harmony) with a mod-major pass:
   for each mod in load order, patch every file. The class doc-comment explains the three-phase design in detail.
   **Consequence for load order:** a `<foreach>` can see vanilla XML and any mod *earlier* in load order, but not
   mods *after* it.

2. **The `<foreach>` XML-patch templating engine** (`XmlPatchMethodForeach.cs`) — loop/`<bind>` table/`<function>`
   constructs usable inside patch files. **`StrongMods/Docs/foreach.md` is the complete spec** (it ships as mod
   content); read it before touching foreach logic. C# helper functions callable from patches must be tagged with
   `[XmlPatchFunction]` (`XmlPatchFunctionAttribute.cs`) and be `public static`, return `string`, take only
   `string` params.

### `StrongUtils` — shared administration/modding grab-bag

Not a library the others link against — it's its own standalone mod bundling many small server features and
reusable pieces. Notable shared infrastructure worth reusing:

- `ConfigManager.cs` — singleton (`ConfigManager.Instance`, `Init(dir)`) that registers XML config files with
  defaults and optional hot-reload via `FileSystemWatcher`.
- `Commands/` — server console commands, each a `ConsoleCmdAbstract` subclass (see
  `Commands/GracefulShutdownCommand.cs` for the standard shape: `getCommands`, `getDescription`, `getHelp`,
  `Execute`). The game auto-discovers these; no registration needed.
- `KeyValueStore/` — a small persistence abstraction (`IKeyValueStore`, XML-backed impl).
- Chat helpers (`Chat.cs`), audit logging (`StrongAudit.cs`), server lifecycle hooks (`ServerLifecycle.cs`).

## Conventions

- **Formatting is enforced by `.editorconfig`** (2-space indent, LF, max line 120, `charset=utf-8`, K&R-style
  braces — `csharp_new_line_before_open_brace = none`). `var` only when the type is apparent; use language
  keyword types (`int`, not `Int32`); avoid `this.` qualification; constants in `PascalCase`.
- **Namespaces match the project/assembly name.** Each project sets `RootNamespace` = `AssemblyName` = mod name.
- `ModInfo.xml` is UTF-8-with-BOM and declares `Name`, `Version`, `DisplayName`, `Description`, `Author`
  (`str0ngh34rt`). Bump `Version` when shipping behavior changes.
- AI artifacts such as specs and handoff docs can be found in the `.ai/` directory of the relevant project.
- While most projects have little or no docs yet, we strive to put a README.md in the root of each project and
  supporting detailed docs in its `Docs/` directory

## Adding a new mod

Scaffold from a template (`Template7DtDMod` for a code mod, `Template7DtDModlet` for XML-only), then add the
project to `StrongMods.sln`. Set the Debug `OutputPath` to the game's `Mods\<ModName>` folder (copy an existing
`.csproj`'s reference block and property groups — the DLL `HintPath`s are identical across projects). Mark
`ModInfo.xml` (and any `Config/` files or docs) as `Content` with `CopyToOutputDirectory=PreserveNewest`.

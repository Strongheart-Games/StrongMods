# AGENTS.md

This file provides guidance to coding agents (e.g. Claude Code) when working with code in this repository.

The repo's purpose and vocabulary live in @CONTEXT.md — read it before making design decisions; this file carries the
working rules.

**CONTEXT.md is human-authored and read-only to agents.** It is the human's own voice and judgment — the baseline the
rest of the repo is measured against — so an agent editing it, *or drafting prose a human then pastes in*, destroys the
one thing it exists to be. `.claude/settings.json` denies `Edit`/`Write` on it, but the rule is the fence, not the deny:
never route around it with a shell command, a patch file, or a git operation. If something in it reads wrong, stale, or
missing, say so in a sentence or file an issue — and leave the wording to the human.

## Communication

When a response refers to a GitHub issue, link the issue number directly to that issue. When it refers to a posted
comment, link the reference directly to that comment.

## What this repo is

A monorepo of ~25 mods for the game **7 Days to Die** (a dedicated-server / Unity title). Each top-level directory
(except `build`, `Template*`, `packages`, the `Tests` project and `StrongDev`) is one independent mod, and each is a
separate SDK-style C# class-library project (`Microsoft.NET.Sdk`, `net481`), **C# LangVersion 9**. All projects are
listed in `StrongMods.sln`. See `README.md` for the one-line description of each mod.

A shipped mod is a directory in the game's `Mods/` folder containing a compiled DLL, a `ModInfo.xml` manifest, and
optionally a `Config/` folder of XML patches. Three project shapes exist: **code mods**, XML-only **modlets**, and
**overlays** — projects deploying into directories the repo doesn't fully manage, such as `Hades` (content beside
World-Editor-authored world binaries) and `StrongholdSaves` (config in the game's `Saves/` tree); see
`build/Overlay.targets` for their semantics. The two `Template7DtD*` directories are `dotnet new` templates for
scaffolding a new mod, not shippable mods themselves.

## Building

For builds, restores, tests, MSBuild evaluation, build failures, or changes to shared build logic, follow
`.agents/skills/build-and-test/SKILL.md`.

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

### `StrongMods` — the foundational runtime mod

This is the foundational mod other mods depend on (only cross-project reference in the repo:
`AutoCollectLoot` → `StrongMods` via `ProjectReference`). It provides two things:

**StrongMods loads first**, via `<ModLoadTier>First</ModLoadTier>` (the `000000-` prefix). The tier places it ahead of
other mods because it replaces the XML patcher.

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

Not a library the others link against — it's its own standalone mod bundling many small server features alongside
infrastructure worth reusing (`ConfigManager`, `KeyValueStore`, `Chat`, `StrongAudit`, `ServerLifecycle`). **
`StrongUtils/README.md` has the inventory**; read it before writing something the mod already provides.

Its `Commands/` folder is also the reference for server console commands generally: each is a `ConsoleCmdAbstract`
subclass (`Commands/GracefulShutdownCommand.cs` shows the standard shape — `getCommands`, `getDescription`,
`getHelp`, `Execute`), and the game auto-discovers them with no registration.

## Conventions

- **Formatting is enforced by `.editorconfig`** (2-space indent, LF, max line 120, `charset=utf-8`, K&R-style braces —
  `csharp_new_line_before_open_brace = none`). `var` only when the type is apparent; use language keyword types (`int`,
  not `Int32`); avoid `this.` qualification; constants in `PascalCase`.
- **In Markdown, readability outranks the 120-column limit.** Wrap prose at 120, but **Markdown table rows are
  exempt** — a table is easier to scan than the list it would become, so never reflow a table, convert one to bullets,
  or truncate its cells just to fit the limit. `.editorconfig` cannot express this (it has no notion of
  "inside a table"), so the rule lives here. Same applies to long URLs and code-block lines that cannot be broken.
- **Don't fake a table with consecutive `Label: value` lines.** Markdown joins adjacent lines into one paragraph, so
  they render as an unreadable run-on. Use a real table, or a bullet per field — never bare label lines. This applies
  especially to status/metadata headers at the top of a doc.
- **Spell out "byte order mark"; never the bare acronym "BOM".** The acronym collides with "bill of materials" (as in
  SBOM), so it forces disambiguation on every read. Write "byte order mark (U+FEFF)" where the code point helps. Applies
  to prose, comments, commit messages, and tool/lint output; literal identifiers such as the `.editorconfig` value
  `utf-8-bom` stay as-is.
- **Namespaces match the project/assembly name.** The SDK defaults `RootNamespace` and `AssemblyName` to
  `$(MSBuildProjectName)`, so a project should not set them; the directory name *is* the mod name.
- **Names must survive leaving their context.** A file or class name is read in places its declaration can't follow —
  planning docs, commit messages, grep output, error text, chat — and the name alone must say what the thing is when met
  there. The test: write the bare name in a doc for a reader who has not opened the file; do they know what kind of
  thing it names? `Ctx` fails (*which* of the dozen kinds of context?);
  `SmokeTestCtx` passes. `GameRoom` failed; `PatcherHost` passes. Carry the category word wherever the category is what
  disambiguates (`*Host` on every isolated game-assembly host — `Tests/Fixtures/PatcherHost.cs`
  defines the concept); abbreviate only what is unambiguous repo-wide (`Sdtd` is; `Ctx` was not). Where a fully
  self-identifying name is impractical, every out-of-file reference carries qualification instead (`SmokeTests.Ctx`, a
  path). Existing violations are grandfathered — #64 enumerates them for one-by-one fixes — but a name a change already
  touches conforms as part of that change, and new names conform always.
- `ModInfo.xml` is UTF-8 without a byte order mark and declares `Name`, `Version`, `DisplayName`, `Description`,
  `Author` (`str0ngh34rt`). Bump `Version` when shipping behavior changes.
- **The backlog lives in GitHub Issues, not in documents.** A plan doc explains *why* — the design, the options weighed,
  the verification. The issue carries the work and its status. **Never add a status or follow-on table to a doc:** it
  becomes a second tracker, and two trackers always drift. Raise work as an issue and cite it by number. The older plans
  keep a `§0` crosswalk purely because their prose cites legacy `F` identifiers; that table maps IDs to issues and
  deliberately carries no status.
- While most projects have little or no docs yet, we strive to put a README.md in the root of each project and
  supporting detailed docs in its `Docs/` directory

## Agent Workflow & Workstyle Constraints

* **Small, Atomic Changes** -- You must strictly adhere to principles for creating small, reviewable, and single-focused
  changes. Every code generation cycle must produce self-contained edits.

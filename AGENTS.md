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

### Shared build files

Every project gets its settings from `build/`. Individual `.csproj` files carry only what is unique to them — the
canonical code mod is 4 lines (the `Sdk` attribute plus the two imports), and only genuine deviations add lines.
`.cs` files are globbed by the SDK; there are no `Compile` lists and no `ProjectGuid`s.

| File                                            | Role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `build/GamePaths.props`                         | The **one** place 7DtD paths live, split into two roots (#23): the **game tree** `$(SdtdDir)` — what everything compiles and tests against, resolved from the declared version (under `packages/` by default; `vendor/` via the explicit `-p:SdtdTreeSource=vendor` escape; `-p:SdtdDir` overrides outright) — deriving `$(SdtdManagedDir)`, `$(SdtdHarmonyDir)`, `$(SdtdConfigDir)`; and the **install** `$(SdtdInstallDir)` — this machine's live game — deriving `$(ModsDir)`, `$(SdtdSavesDir)`, `$(SdtdServerDir)`. Redirecting the game tree never moves the deploy destination. Not imported directly by projects — the entry points below pull it in. |
| `build/GameVersions.props`                      | The declared game versions (#23): repo-default `SdtdUnit` / `SdtdDevVersion` / `SdtdTestVersions`, and the version registry (`SdtdGameVersionMap`, `label=packageVersion` pairs — branch heads only). Per-mod pins do **not** live here — a deviating mod declares them in its own `.csproj`, ABOVE its entry-point props import. Pulled in by `GamePaths.props`.                                                                                                                                                                                                                                                                                             |
| `build/Mod.props`                               | Code-mod defaults. Imported **before** the project body, so the body overrides it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `build/Mod.targets`                             | Code-mod references, content and `OutputPath`. Imported **after** the body.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `build/Modlet.targets`                          | The whole build for an XML-only modlet: stages content to `bin\`, plus `Clean`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `build/Deploy.targets`                          | The shared `Deploy` target (mirror install into the live game). Pulled in by `Mod.targets` and `Modlet.targets`, like `GamePaths.props`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `build/XmlLint.targets` + `build/XmlLint.cs`    | XML well-formedness lint for each project's `ModInfo.xml` + `Config\**\*.xml`, run inside every build. Pulled in by all three entry points, like `Deploy.targets`; the `.cs` is the task body, compiled at build time by `RoslynCodeTaskFactory` — it belongs to no project. Bypass: `-p:XmlLintEnabled=false`. Project XML needs no pass — MSBuild parses it to build at all (`.ai/xml-lint-plan.md` §2).                                                                                                                                                                                                                                                    |
| `build/Overlay.props` + `build/Overlay.targets` | The overlay entry-point pair: protective-additive `Deploy` with `MirrorOnDeploy` scoped mirroring into a declared `DeployRoot`. A props/targets **sandwich** like code mods, because an overlay's body *references* shared path properties — see the `Overlay.props` header for the incident that makes the order load-bearing. Never combined with the other entry points.                                                                                                                                                                                                                                                                                   |
| `build/tools/compare-eval.cs`                   | Verification helper; not imported by MSBuild. See *Verifying* below.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |

**Nothing is auto-imported — there is deliberately no `Directory.Build.props`/`.targets`, and adding one is a mistake.**
Import position is explicit and load-bearing. ADR-0001 (`docs/adr/0001-no-auto-imported-msbuild-files.md`) has the
mechanism, the rejected alternatives and the two incidents that decided it.

A code mod imports the props file as the first element of its body and the targets file as the last; the SDK's implicit
`Sdk.props`/`Sdk.targets` imports bracket the whole body, so the sandwich holds:

```xml

<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\build\Mod.props" />
  <!-- deviations only: ModLoadTier, ModsDir, GameAssembly items, PlatformTarget, PackageReference -->
  <Import Project="..\build\Mod.targets" />
</Project>
```

A modlet imports one file: `<Import Project="..\build\Modlet.targets" />`. An overlay uses a **two-import sandwich**
(`..\build\Overlay.props` first, `..\build\Overlay.targets` last) with `<DeployRoot>` and its
`<MirrorOnDeploy>` declarations between them — the props import must come first because `DeployRoot` references shared
path properties, which would otherwise expand empty.

### References

**Everything compiles against the game's own assemblies** — game types from `$(SdtdManagedDir)`, `0Harmony.dll`
from `$(SdtdHarmonyDir)` (derived from `$(SdtdDir)`, **not** from `$(ModsDir)`, so redirecting the deploy target never
breaks compilation), *and framework types too*: `build/Mod.props` sets `FrameworkPathOverride` to the game's Managed
folder, so no .NET Framework targeting pack is needed anywhere. **This is a necessity, not a preference**, and it means
what compiles is what the runtime actually has — ADR-0002 (`docs/adr/0002-compile-against-the-games-assemblies.md`) has
the pilot that settled it and the API that ruled out the alternative. A **game tree** must be present to build — by
default the declared version's restored package tree (`dotnet restore build/GameAssemblies.csproj --packages packages
--configfile build/GameAssemblies.nuget.config`, once per version; needs a read PAT in `PACKAGES_READ_TOKEN`); a game
*install* is needed only to deploy or to vendor. `build/Mod.targets` raises one readable, per-source error if the tree
is missing. To add a game assembly to a project: `<GameAssembly Include="Noemax.GZip" />`.

**Every project needs a NuGet restore before its first build** (SDK-style projects require the assets file even with no
packages). This is automatic in practice: `dotnet build` restores implicitly, IDEs restore on load, and bare `msbuild`
needs `-restore`. A build without one fails with a single readable `NETSDK1004` "run a restore" error. Only `BloodRain`
actually pulls a package — Cronos, a bare `<PackageReference>`, fetched from nuget.org once per machine. Its csproj also
sets `CopyDocumentationFilesFromPackages=true` so `Cronos.xml` keeps deploying beside `Cronos.dll`, as it always has —
the SDK skips package doc files by default.

Full `MSBuild.exe` (as opposed to `dotnet build`) resolves `Microsoft.NET.Sdk` only if a .NET SDK is discoverable on the
machine.

### Deploying

**Building never touches a live install.** Every build stages the shippable mod folder to `bin\$(Configuration)\`;
installing is the explicit `Deploy` target (`build/Deploy.targets`, shared by both project shapes):

```bash
dotnet build StrongMods.sln -c Debug                                  # build everything; writes only bin\/obj\
dotnet build StrongMods.sln -c Debug -t:Deploy                        # build and install into the live game
dotnet build DynamicFeralSense/DynamicFeralSense.csproj -c Debug -t:Deploy   # one mod
```

For mods and modlets, `Deploy` **mirrors** staging into `$(ModsDir)\$(ModDeployName)\`: source is authoritative for
content *and existence*, so a file removed from source is deleted from the deployed folder at the next deploy (announced
in the build log). Mirroring assumes the repo manages the whole deploy folder — a project deploying into a directory
with unmanaged content is an **Overlay** instead: its `Deploy` is protective-additive (copy if absent or newer, never
overwrite newer live edits, never delete) except inside its declared `MirrorOnDeploy` directories/files, where mirror
semantics apply scoped. `Hades`' live prefab edits and world binaries survive its deploys by construction.

- `<ModLoadTier>AfterDependencies</ModLoadTier>` sets deploy-folder load order by *intent* — tiers `First`,
  `AfterDependencies`, `Last`, `LocalConfig` map to the literal prefixes in `build/Deploy.targets`, whose header comment
  records the verified sort facts (the game's comparison is culture-aware, not ordinal). Raw
  `<ModLoadPrefix>` remains the escape hatch; setting both is a build error.
- `<ModsDir>$(SdtdServerDir)\Mods</ModsDir>` targets the dedicated server instead.
- `<IsDeployable>false</IsDeployable>` marks a project that never deploys (both templates; `Tests`).
- `-p:ModsDir=...` redirects the deploy *destination* — for testing the deploy step itself against scratch. Plain builds
  no longer need it for safety, and since the two-root split `-p:SdtdDir` cannot move the deploy destination: a deploy
  during a vendor-mode build goes to the normal install, never into the tree.
- Deploy verifies the **destination install's version** (#37): its `Assembly-CSharp.dll` must hash-match one of the
  mod's declared `SdtdTestVersions` trees, else the deploy is refused with the declared list in the message. The unit
  follows the destination (a server-targeted deploy checks server trees). Redirected destinations with no game assembly
  above them skip the check. Escape hatch: `-p:SdtdSkipInstallVersionCheck=true`.
- `Clean` touches only the `bin\` staging, never a live install. Removing a deployed mod entirely is a manual act.
- Release deploys too: `-c Release -t:Deploy`.

Per-machine overrides (a different install path, a permanent redirect) go in a gitignored `Local.props` in the repo
root — copy `Local.props.sample`. Precedence: `-p:` → `Local.props` → `SDTD_HOME` → the default.

### Building without the game

Plain builds need no game install at all since #23: they resolve each mod's declared version under the gitignored
`packages/` tree (restored from the private feed — see *References*). `-p:SdtdDir` remains the explicit escape for
one-off checks against any tree. `build/tools/vendor.cs` copies a unit's assemblies and vanilla `Data/Config` into the
gitignored `vendor/` tree (`vendor/game/<label>/`,
`vendor/dedicated-server/<label>/` — see its header comment for labels and provenance); vendoring is the *publishing*
procedure, and `-p:SdtdTreeSource=vendor` the temporary pre-publish escape. Any such tree, or a live install of either
unit, works as a build root:

```bash
dotnet build StrongMods.sln -c Debug -p:SdtdDir=vendor/game/V3.1.0-b13
```

`build/GamePaths.props` detects which layout `$(SdtdDir)` is (the game and the dedicated server name their data
directory differently). Building against a vendored tree is safe by construction: builds stage to `bin\` only, and the
deploy destination derives from the install root, never from `-p:SdtdDir` (the two-root split). **Never commit or
publish anything under `vendor/`**: the repo is public and those are licensed game files
(`.ai/f5b-game-assembly-packages.md` §2).

### CI, packages, and publishing

Vendored trees also ship as **private** NuGet packages (`7DtD.Assemblies.Game`,
`7DtD.Assemblies.DedicatedServer`) on the org's GitHub Packages feed — private always, never repo-linked; the contents
are licensed game files. The full design and leak model live in `.ai/ci-feed-and-workflow.md`.
`.github/workflows/build-and-test.yml` restores the **whole registry** (repo secret `PACKAGES_READ_TOKEN`, the bot's
read token; `build/GameAssemblies.csproj` derives its `PackageDownload` list from
`build/GameVersions.props`) and builds the solution against **both** units on every push, each project resolving its own
declared version — the standing compile-against-both check (#21), per-pin meaningful (#37). A separate non-blocking
**advisory** lane builds everything against the newest registry version — the migration board during a transition; red
there never gates main. Workflows must never upload artifacts or run
`-t:Deploy`. `check-for-new-game-version.yml` polls Steam's branch heads daily via anonymous SteamCMD and, once its
shadow soak ends, files a tracking issue when a release lands.

Publishing a new game version is one human-run command on a machine with licensed installs:
`dotnet run build/tools/release.cs` (guardrails decide whether there is anything to publish; one prompt for the in-game
version label; `--commit` pushes the published-state record to main). **Publishing ≠ adopting**:
consumed versions are declared in `build/GameVersions.props` (and per-mod pins), edited by a human when adopting — a
registry row nothing declares is a dead pin the closure test rejects, which is why `release.cs`
prints the reminder instead of editing declarations. Published state lives in `build/ci/game-versions.json`. The tools —
`steam_check`,
`vendor`, `pack`, `push`, `release` — are C# file-based apps under `build/tools/` (`dotnet run
build/tools/<tool>.cs -- --selftest`; #36 decided all tools are C# — with `compare-eval`, no Python remains). Feed
hygiene is `push.cs`'s job: idempotent directory pushes, keep-latest-build-per-`major.minor.patch`
retention, and GitHub latest-tag reconciliation. The C# rule is about maintained code: anything checked in is C#.
Disposable experiment scripts under `.scratch/` may use whatever is fastest (Python included); an experiment that
graduates into the repo is ported when it lands.

### Verifying

Three levels beyond running the game:

1. **Evaluation diff, no build.** `msbuild <proj> -getProperty:... -getItem:...` prints a project's resolved settings as
   JSON without running any target — no compile, no copy, nothing written to the game. Diff that against a
   `git worktree` of `HEAD` to prove a `.csproj` change is a no-op. `build/tools/compare-eval.cs` does the diff; its
   header comment has the usage and the pitfalls. **Always query `OutDir`/`TargetDir`, not just `OutputPath`.**
2. **A real build** — inherently safe: builds stage to `bin\` and cannot disturb a live install. Every build also lints
   `ModInfo.xml` and `Config\**\*.xml` for XML well-formedness (`build/XmlLint.targets`), so a malformed patch file
   fails the build instead of failing at game load. To verify the *deploy step* itself, run `-t:Deploy` with
   `-p:ModsDir=.scratch\...` (and `-p:SdtdSavesDir=...` for the StrongholdSaves overlay). A relative value resolves
   against the directory the command was run from, the same as `-p:SdtdDir`.
3. **The test suite** — `Tests` (modern .NET, not a mod; the single home for every runner-based test in the repo):
   `dotnet test StrongMods.sln -c Debug`
   resolves every mod's Harmony patch targets — `[HarmonyPatch]` attributes and `[PatchTargetManifest]`-published
   programmatic targets — against the unit `$(SdtdDir)` points at (live install, or a vendored tree via
   `-p:SdtdDir=vendor/...`). Failure messages carry the version tested and near-miss signatures, so a target lost to a
   game update is diagnosed from the message alone. CI runs the suite against both units on every push.

**StrongMods loads first**, via `<ModLoadTier>First</ModLoadTier>` (the `000000-` prefix) — the tier forces it ahead of
other mods in load order, which matters because it replaces the XML patcher (see below).

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
- `ModInfo.xml` is UTF-8 with a byte order mark and declares `Name`, `Version`, `DisplayName`, `Description`, `Author`
  (`str0ngh34rt`). Bump `Version` when shipping behavior changes.
- **Docs have homes** — one self-contained HTML review document carries planning, decisions, verification, and handoff
  for each new or actively revised effort: `.ai/reviews/YYYY-MM-DD-<effort>-review.html` (per project, or repo-root for
  repo-wide work). Start from `docs/agents/review-document-template.html`. Historical effort docs stay where they are;
  durable decisions are ADRs in `docs/adr/`. `docs/agents/domain.md` has the layout and how to choose.
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
* **Issues** -- the backlog lives in GitHub Issues on
  [Strongheart-Games/StrongMods](https://github.com/Strongheart-Games/StrongMods/issues), driven with the `gh` CLI.
  `docs/agents/issue-tracker.md` is the whole contract: the bot identity to confirm before writing, the label facets,
  and the apply-only / close-never-delete rules. Read it before filing or labelling anything.

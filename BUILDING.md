# Building StrongMods

This guide covers a fresh machine, routine builds, and the shared MSBuild design. Building stages files under `bin/`
and `obj/`; it never installs a mod into a live game.

## Quick start

### Requirements

| Requirement | Details |
|---|---|
| Operating system | Windows or Linux. CI verifies Ubuntu. The supported command-line path is the cross-platform `dotnet` CLI. |
| .NET SDK | Install .NET SDK `10.0.x`. `Tests` targets `net10.0`, and `build/tools/*.cs` uses C# file-based apps from .NET 10. No `global.json` pins a feature band. Confirm with `dotnet --version`. |
| Git | Needed to clone the repository and for evaluation comparisons against a worktree. A source archive is enough only for a basic build. |
| Network access | Restore must reach `https://api.nuget.org` and `https://nuget.pkg.github.com/Strongheart-Games`. |
| Private-feed access | Set `PACKAGES_READ_TOKEN` to a classic GitHub personal access token with `read:packages` and access to the Strongheart-Games packages. Fine-grained tokens do not support this NuGet registry. Keep the token out of commands and repository files. |
| Disk space | Allow space for both game and dedicated-server assembly trees for every declared version, plus normal NuGet and build output. The required size grows with `SdtdGameVersionMap`. |

No .NET workloads are required. A 7 Days to Die installation, Visual Studio, Rider, a .NET Framework targeting pack,
Steam, and SteamCMD are not required to build or test. The game assemblies supply framework references for the
`net481` mods.

### Fresh-machine build

Clone the repository, make `PACKAGES_READ_TOKEN` available to the process, and run from the repository root:

```powershell
git clone https://github.com/Strongheart-Games/StrongMods.git
cd StrongMods
dotnet restore build/GameAssemblies.csproj --packages packages --configfile build/GameAssemblies.nuget.config
dotnet build StrongMods.sln -c Debug
dotnet test StrongMods.sln -c Debug
```

The first restore downloads the licensed game trees from the private feed. The build then restores ordinary project
packages from `nuget.org` as needed. `BloodRain` is the only mod with a package dependency; `Tests` also has test
packages. A fresh setup works when the private restore, solution build, and test suite all succeed.

Build one project when full-solution coverage is unnecessary:

```powershell
dotnet build DynamicFeralSense/DynamicFeralSense.csproj -c Debug
```

## Project shapes and imports

Every project imports its build entry point explicitly. The repository deliberately has no
`Directory.Build.props` or `Directory.Build.targets`. Import order is load-bearing.

| Shape | Imports |
|---|---|
| Code mod | `build/Mod.props` first and `build/Mod.targets` last |
| XML-only modlet | `build/Modlet.targets` |
| Overlay | `build/Overlay.props` first and `build/Overlay.targets` last |

A normal code mod contains only its two imports. Put declarations that affect path resolution, such as a per-mod
`SdtdDevVersion`, above the props import. Put ordinary deviations between the imports. The SDK globs `.cs` files, so
projects do not carry `Compile` lists or `ProjectGuid` values.

## Shared build files

| File | Role |
|---|---|
| `build/GamePaths.props` | Resolves the build/test game tree separately from the live install. |
| `build/GameVersions.props` | Declares default versions, test versions, units, and the package-version registry. |
| `build/Mod.props` and `build/Mod.targets` | Define code-mod defaults, references, content, and staging output. |
| `build/Modlet.targets` | Stages XML-only modlet content and implements `Clean`. |
| `build/Overlay.props` and `build/Overlay.targets` | Stage overlays and define their separate deployment semantics. |
| `build/Deploy.targets` | Implements explicit mod and modlet deployment. Building does not invoke it. |
| `build/XmlLint.targets` and `build/XmlLint.cs` | Check shipped XML for well-formedness during every build. |
| `build/tools/compare-eval.cs` | Compares MSBuild evaluation results without building. |

Read the relevant file headers before changing shared build logic. They document extension points and order-sensitive
behavior next to the implementation.

## Game trees and restore

Compilation and tests use a **game tree**, selected through `$(SdtdDir)`. The default is the project's declared version
under `packages/`. `build/GameAssemblies.csproj` restores both game and dedicated-server packages for every row in
`SdtdGameVersionMap`. These licensed packages are private and must never enter commits, public packages, or CI
artifacts.

Use `-p:SdtdDir=<tree>` for a one-off build against any complete tree. Use `-p:SdtdTreeSource=vendor` only as the
temporary pre-publication path. A live installation is needed only for deployment or for creating licensed package
content. `Local.props` and `SDTD_HOME` configure install-side paths; neither is required for a plain build.

SDK projects also need their normal NuGet assets file. `dotnet build` restores it automatically. An IDE restores on
load. Bare `MSBuild.exe` needs `-restore` and must be able to discover a .NET SDK.

## Verification

Use the narrowest check that proves the change, then add broader checks when risk warrants them:

1. Query `dotnet msbuild <project> -getProperty:... -getItem:...` to inspect evaluation without running a target.
   Include `OutDir` and `TargetDir`, not only `OutputPath`. Use `build/tools/compare-eval.cs` for before/after results.
2. Run a real build. It compiles code, stages content, and checks `ModInfo.xml` plus `Config/**/*.xml`.
3. Run `dotnet test StrongMods.sln -c Debug` for repository-wide patch-target and runner-based tests.

Deployment is a separate operation. Use the explicit `Deploy` target only when installation is intended.

## Common failures

| Symptom | Check |
|---|---|
| Private restore returns `401` or `403` | Confirm `PACKAGES_READ_TOKEN`, `read:packages`, package access, and classic-token use. |
| Build reports a missing declared game tree | Run the private restore from the repository root and confirm both package IDs contain the declared version. |
| `NETSDK1004` reports a missing assets file | Run `dotnet restore` or build without `--no-restore`. |
| The SDK cannot target `net10.0` or run `build/tools/*.cs` | Install .NET SDK `10.0.x` and confirm the selected SDK with `dotnet --info`. |
| `MSBuild.exe` cannot resolve `Microsoft.NET.Sdk` | Install a .NET SDK or use `dotnet build`. A .NET Framework targeting pack is not the fix. |

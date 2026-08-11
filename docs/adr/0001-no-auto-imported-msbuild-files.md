# 0001. No auto-imported MSBuild files

**Status:** Accepted
**Date:** 2026-08-10 — retro-filed; the decision itself was made during the build refactor
(`.ai/build-refactor-plan.md`, §"No auto-import").

## Context

Shared build settings for ~30 projects have to live somewhere central. MSBuild's standard answer is
`Directory.Build.props` / `Directory.Build.targets`: drop them at the repo root and every project below picks them up
with no per-project edit.

That answer does not work here, and it cost a failed build to find out why.

`Microsoft.Common.CurrentVersion.targets` derives `OutDir`, `TargetDir` and `TargetPath` from `$(OutputPath)` **during
evaluation**, at the point it is imported. `Microsoft.Common.targets` imports `Directory.Build.targets` *after* that.
So setting `OutputPath` from a `Directory.Build.targets`:

- leaves `OutDir` latched at the `bin\$(Configuration)\` fallback, so the assembly is written to the wrong place —
  **while `$(OutputPath)` itself reads back correct**, which hides the fault from the obvious diagnostic; and
- can fail the build outright with `error : The BaseOutputPath/OutputPath property is not set for project ...`.

The other slot is no better. `Directory.Build.props` is imported *before* the project body, so it cannot see a
project-level `ModLoadPrefix`, `ModsDir` or `GameAssembly` — the per-project deviations that make a shared build worth
having in the first place.

The general shape of the problem: **auto-import fixes the position, and this repo needs both positions.** Defaults the
body overrides must land before it; logic that consumes the body must land after it. One file cannot be in two places,
and MSBuild does not let you choose.

## Decision

This repo contains **no auto-imported MSBuild files** — no `Directory.Build.props`, no `Directory.Build.targets`, no
`Directory.Build.rsp`. Every project imports its shared build files explicitly, as a sandwich around its own body:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\build\Mod.props" />    <!-- defaults; the body overrides them -->
  <!-- deviations only -->
  <Import Project="..\build\Mod.targets" />  <!-- consumes the body; sets OutputPath -->
</Project>
```

Modlets import one file (`Modlet.targets`); overlays use their own sandwich (`Overlay.props` / `Overlay.targets`).
Per-machine overrides live in a repo-root `Local.props`, imported behind an `Exists()` guard and deliberately *not*
named `Directory.Build.user.props` — that prefix implies the auto-discovery this decision rejects.
`Directory.Build.rsp` was considered and rejected separately: response files are an `MSBuild.exe` command-line feature
that IDEs building in-process do not read, so it cannot carry a setting the IDE needs.

Adding a `Directory.Build.*` file to this repo is a defect, not a convenience.

## Consequences

Wanted:

- Both project shapes use the **same mechanism**. The alternative had code mods configured by auto-import and modlets
  by explicit import, purely because modlets never import `Microsoft.Common.props`.
- The import *is* the opt-in, so a project without it is untouched. That is what let the migration proceed a few
  projects at a time without silently retargeting a live deploy folder.
- It survived the SDK-style migration (#9) unchanged — `Directory.Build.props` lands before the body there too.
- A project's entire build story is readable from the project file.

The cost: a new project that forgets the imports gets **nothing** rather than everything. Mitigated two ways —
`Mod.targets` raises a named error if `Mod.props` was not imported, and both `dotnet new` templates ship with the
imports already in place.

Import position became a load-bearing invariant rather than a style preference, and a second incident proved that
independently: a `<DeployRoot>$(ModsDir)\Hades</DeployRoot>` written *above* the `Overlay.props` import froze the
reference empty, `$(ModsDir)\Hades` became `\Hades`, and a deploy landed in `C:\Hades` (2026-07-30). Prose defending
this measurably rots — #52's comment asserted the opposite of the truth for its whole life — so the rule is
executable: `Tests/ProjectConventionTests.cs` rule B (#54) fails any project whose entry points sit in the wrong
order.

Verification corollary: querying `$(OutputPath)` does not reveal an `OutDir` latch. When proving a `.csproj` change is
a no-op, always query `$(OutDir)` and `$(TargetDir)` as well.

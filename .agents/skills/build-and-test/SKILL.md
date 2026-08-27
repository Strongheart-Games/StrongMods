---
name: build-and-test
description: >
  Build, restore, test, inspect, or change StrongMods projects and shared MSBuild logic. Use for build failures,
  MSBuild evaluation, and safe build verification. Do not use for deployment or game-assembly version management.
---

# Build and test StrongMods

Use [BUILDING.md](../../../BUILDING.md) as the build-system source of truth. Read **Quick start** before a restore or
fresh-machine build. Read **Project shapes and imports**, **Shared build files**, and the relevant build-file headers
before changing a project shape or shared MSBuild logic.

## Scope and safety

- Prefer one-project build or test scope when it proves the requested change. Use the solution for repository-wide
  verification or when the affected dependency boundary is unclear.
- A plain build stages under `bin/` and `obj/`. It does not authorize `Deploy`.
- Use `deploy-mod` for installation or deployment verification.
- Use `manage-game-assemblies` for missing private trees, version declarations, unit selection, CI version coverage,
  vendoring, publishing, or adoption.

## Workflows

For a routine build or test, select the configuration and game unit required by the task, run the documented command,
and report the exact scope, unit, configuration, and result.

For a build failure, preserve the first actionable error. Distinguish SDK or project restore failure, missing game tree,
compile failure, XML lint failure, and test failure before changing files.

For a project or shared-build change, inspect evaluation before and after. Include `OutDir` and `TargetDir`, then run a
real build because evaluation does not compile. Run the test suite when the change can affect project-wide inputs,
assembly resolution, XML handling, or patch-target verification.

Work is complete when the requested scope succeeds, the selected tree and unit are correct, no deployment occurred,
and the reported evidence covers every build-system behavior the change could affect.

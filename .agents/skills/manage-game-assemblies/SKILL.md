---
name: manage-game-assemblies
description: >
  Manage the licensed 7 Days to Die assembly trees used by StrongMods. Use when restoring or selecting a game tree,
  changing Sdtd version declarations, troubleshooting missing assemblies, updating CI version coverage, or vendoring,
  publishing, and adopting a game release.
---

# Manage game assemblies

Use the build-file and tool header comments as the current command reference. Before changing declarations or paths,
read `build/GameVersions.props` and `build/GamePaths.props`. Before running a tool, read its header and run its
`--selftest` when available.

## Invariants

- A **game tree** supplies every assembly and vanilla configuration file used to compile and test. `$(SdtdDir)` is its
  root. Game types, Harmony, and framework types all come from this tree; do not introduce a .NET Framework targeting
  pack. `build/Mod.props` enforces this with `FrameworkPathOverride`.
- A **game install** is live runtime state used for deployment. It is also the licensed source for vendoring. Its paths
  derive from `$(SdtdInstallDir)`. Redirecting `$(SdtdDir)` never redirects deployment.
- Restored private packages under `packages/` are canonical. The `vendor/` tree is a temporary pre-publish source or an
  explicit escape through `-p:SdtdTreeSource=vendor`.
- Game assemblies are licensed. Keep `vendor/` gitignored. Keep `7DtD.Assemblies.*` packages private and unlinked from
  the public repository. Never expose either through commits, public packages, or CI artifacts.
- Publishing records available packages. Adoption changes the versions consumed by builds and tests. Treat them as
  separate operations.

## Restore declared versions

`build/GameAssemblies.csproj` derives exact `PackageDownload` versions from `SdtdGameVersionMap`. One restore fetches
both releasable units for every registry row:

```powershell
dotnet restore build/GameAssemblies.csproj --packages packages --configfile build/GameAssemblies.nuget.config
```

`PACKAGES_READ_TOKEN` must contain a read PAT accepted by the private GitHub Packages feed. Keep credentials out of the
command, repository, and NuGet configuration.

A restore is complete when both package IDs contain every package version in `SdtdGameVersionMap`:

- `7DtD.Assemblies.Game`
- `7DtD.Assemblies.DedicatedServer`

If a package is missing, first distinguish a missing token, an undeclared package version, and unit skew. Do not add a
fallback source or grant broader CI permissions to hide the failure.

## Select a tree or declared version

- Use `-p:SdtdDir=<tree>` for a one-off build or test against any complete game tree.
- Use `-p:SdtdTreeSource=vendor` only for the temporary declared-version escape. Packages remain canonical.
- Use `SdtdUnit` for `game` versus `dedicated-server`.
- Use `SdtdDevVersion` for compilation and `SdtdTestVersions` for the supported test set. The development version must
  be in the test set.
- Keep repo defaults and `SdtdGameVersionMap` in `build/GameVersions.props`.
- Put a per-mod pin in that mod's `.csproj`, above its entry-point props import. Use literal values there.

Registry rows use `label=packageVersion` pairs. Labels are branch heads. Use the four-part package version produced by
`build/tools/pack.cs`; do not derive it with an ad hoc text replacement.

## Vendor, publish, or adopt a release

Read [references/publishing.md](references/publishing.md) completely before vendoring assemblies, creating or pushing
packages, changing published state, or adopting a new version.

## Maintain CI coverage

`.github/workflows/build-and-test.yml` restores the whole registry and builds and tests both releasable units. Each
project resolves its own declared version. Preserve the non-blocking advisory lane that forces all projects onto the
newest registry version during migrations.

`.github/workflows/check-for-new-game-version.yml` detects new Steam branch heads and files a tracking issue. It does
not vendor, publish, or adopt a release.

CI must never upload the restored trees or packages as artifacts. It must never run `-t:Deploy`. Fork pull requests
must not receive private-feed credentials; do not grant `GITHUB_TOKEN` package access as a workaround.

## Verify changes

For a declaration or adoption change, restore the whole registry and then build and test both units. A change is
complete only when every declared label maps to a restored package, the development versions belong to their test sets,
no unused registry row remains, and both unit runs pass.

# Vendor, publish, and adopt game assemblies

These operations handle licensed files or change external state. Keep each operation within the user's request.
Restoring, vendoring, or adopting does not authorize a package push. Publishing does not authorize `--commit`.

## Vendor a unit

Read the header of `build/tools/vendor.cs`, then run its self-test:

```powershell
dotnet run build/tools/vendor.cs -- --selftest
```

Vendor a complete unit from its licensed installation:

```powershell
dotnet run build/tools/vendor.cs -- --unit game --label V<major>.<minor>[.<patch>]-b<build>
dotnet run build/tools/vendor.cs -- --unit dedicated-server --label V<major>.<minor>[.<patch>]-b<build>
```

Use `--install-dir` only for an explicit non-default installation. Use `--force` only after confirming that replacing
the existing labeled tree is intended.

The tool copies the complete Managed, Harmony, and vanilla `Data/Config` inputs. It writes provenance and hashes to
`manifest.json`. Do not cherry-pick assemblies or edit the resulting tree by hand.

## Publish a release

Publishing is a human-run operation on a machine with licensed installations. Give the command to the user and help
interpret its output; do not run the live release on the user's behalf. The supported entry point is:

```powershell
dotnet run build/tools/release.cs -- [--dry-run] [--steamcmd] [--steam-user <name>] [--commit]
```

Before the human runs it, read the header and run:

```powershell
dotnet run build/tools/release.cs -- --selftest
```

`release.cs` checks Steam state, verifies or updates the local installations, asks once for the in-game version label,
vendors and packs stale units, and delegates feed hygiene to `build/tools/push.cs`. `--dry-run` stops before any package
push. `PACKAGES_WRITE_TOKEN` supplies the write PAT when the tool does not prompt for it.

Use `--commit` only when the user explicitly wants the published-state change committed and pushed. Without it, the
tool still updates `build/ci/game-versions.json` after a successful package push.

Use `build/tools/vendor.cs`, `pack.cs`, or `push.cs` directly only for a specific repair or backfill. Read the selected
tool's header and run its self-test first. `push.cs` owns duplicate handling, retention, and GitHub latest-tag
reconciliation. Never bypass its safeguards with a direct package push.

Maintained game-assembly tools are C# file-based apps under `build/tools/`. Disposable experiments under `.scratch/`
may use another language, but port any experiment that becomes maintained repository code to C#.

The operation is complete when the intended private packages exist, feed retention and latest-tag reconciliation
succeed, and `build/ci/game-versions.json` matches the published release. It does not adopt the release.

## Adopt a published release

Adoption is a separate repository change:

1. Add or replace the `SdtdGameVersionMap` row in `build/GameVersions.props`.
2. Update repo defaults or the intended per-mod pins. A per-mod pin stays above its entry-point props import.
3. Keep every development version in its test-version set. Leave no registry row that nothing declares.
4. Restore the whole registry through `build/GameAssemblies.csproj`.
5. Build and test both `game` and `dedicated-server` units.

Adoption is complete only when the declared map, restored packages, defaults, per-mod pins, and both unit runs agree.

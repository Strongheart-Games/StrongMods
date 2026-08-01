# Tests

The single home for every test in this repo that runs on a .NET test runner. It is tooling, not a mod: it deploys
nothing, and it runs on modern .NET, loading the game's assemblies headlessly — no game launch involved.

## Where verification lives

Not everything that verifies the repo is in this project. The full map:

| Verification                                                             | Where it lives                                   | When it runs             |
|--------------------------------------------------------------------------|--------------------------------------------------|--------------------------|
| XML well-formedness of `ModInfo.xml` + `Config\**\*.xml`                 | `build/XmlLint.targets`                          | every build              |
| Build-tool selftests                                                     | `dotnet run build/tools/<tool>.cs -- --selftest` | manually, and in CI      |
| Deploy-shape checks (planned, #42)                                       | CI workflow step                                 | every push               |
| **Everything runner-based** — smoke tests today, behavioral suites later | **this project**                                 | `dotnet test`, and in CI |

New runner-based tests land here, in a folder per area, rather than in per-mod test projects — the expensive
infrastructure (game-assembly loading, diagnostics) is shared. Planned next: the `<foreach>` spec conformance and
breadth-first patcher suites (#43).

## What is here today

The Harmony patch-target smoke tests (#14): every `[HarmonyPatch]` class in every code mod, and every target published
through a `[PatchTargetManifest]` (see the StrongMods README on programmatic patches), must resolve against the game
assemblies under test — plus a conformance test that fails any mod patching programmatically without publishing a
manifest, and a permanent negative control proving the suite can fail.

## Running

```bash
dotnet test StrongMods.sln -c Debug
```

builds every mod and runs the suite against the default unit (the live game install). The unit under test is whatever
`$(SdtdDir)` resolves to — same knob, precedence, and layout detection as the build — so other units and versions are
one property away (paths are baked in at build time, so switching triggers a rebuild of this project):

```bash
dotnet test Tests/Tests.csproj -c Debug -p:SdtdDir=vendor/dedicated-server/V3.1.0-b14
dotnet test Tests/Tests.csproj -c Debug -p:SdtdDir=vendor/game/V3.0.1-b4
```

CI runs the suite against both units on every push.

## Reading a failure

Failure messages are version-stamped and carry near-miss diagnosis — the version tested, the signature sought, and what
actually exists under that name — so a target lost to a game update is diagnosed from the message alone. **A red test
right after a game update is the suite doing its job**, not a bug in the suite: the target changed or vanished in that
version, and the patch (or the mod's supported-version claim, see #45/#23) needs updating.

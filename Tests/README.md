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

The `<foreach>` spec conformance suite (#43), covering every clause of `StrongMods/Docs/foreach.md`, and
`ProjectConventionTests`, which reads the repo's `.csproj` files rather than running anything.

## Adding a fixture project

Some tests need a helper assembly rather than a helper class — `Stubs\` (a stand-in `UnityEngine.CoreModule`) and
`FunctionMod\` (a real mod carrying `[XmlPatchFunction]` methods) are the two so far. Both are separate projects
nested under `Tests\`, and both need the same three things:

1. **`<ProjectReference … ReferenceOutputAssembly="false" />` from `Tests.csproj`.** Builds the fixture, but keeps
   it out of the test output directory — anything sitting there is reachable by the *smoke* tests' load context,
   which must keep seeing the real game assemblies. The conformance room loads fixtures by path instead
   (`AssemblyMetadata` carries the directory).
2. **`<Compile Remove="…\**" />` in `Tests.csproj`.** Otherwise the SDK's glob compiles the nested project's
   sources *and* its generated `obj\` files into the test assembly. This one hides: it only breaks once that
   `obj\` folder exists, so the first build after adding a project succeeds and the second fails.
3. **A `ProjectReference` to every repo project it compiles against**, even when the reference itself comes from a
   `HintPath` — `ReferenceOutputAssembly="false"` orders the build without changing what is compiled, and
   `SkipGetTargetFrameworkProperties="true"` is needed when the two target different frameworks. Without it
   nothing orders the dependency and a clean checkout can fail (#51). `ProjectConventionTests` enforces this one.

## What can be tested headlessly

The mod and game assemblies are built for the game's old framework, but the test process can load **and
execute** them. The governing rule: old-framework code runs on the modern engine until it touches something
that isn't there. That gives three tiers:

| Tier | Examples | Headless? |
|------|----------|-----------|
| Pure managed logic | the `<foreach>` engine, breadth-first patcher algorithms, XML handling | **Yes** — unit tests welcome; this is #43's territory |
| Brushes the engine indirectly | anything calling `Log.*`, config singletons | **Yes, with a seam** — shim or Harmony-patch the touchpoint in the harness first |
| Engine-dependent behavior | world, entities, rendering — anything needing a running game | **No** — that is in-game verification, a different track |

## The two Harmonys

Harmony and its MonoMod helpers each exist in two *flavors*: one compiled for the game's old framework (what
the game runs on) and one for modern .NET (what the tests run on). Same versions, same behavior — different
flavor per engine, and the old flavor of MonoMod crashes if executed on the modern engine. Who uses what:

| Situation | Engine | 0Harmony used | MonoMod used |
|-----------|--------|---------------|--------------|
| The deployed game | Mono (old framework) | the game's own, from `Mods/0_TFP_Harmony` | the game's own, same folder |
| `dotnet test` | modern .NET | **still the game's own** — loaded from the tree under test, so tests exercise that unit's exact Harmony | modern-flavor NuGet builds of the same versions the game's 0Harmony asks for |

Two rules keep this deterministic (details in the `Tests.csproj` and `GameTree` comments): everything that must
*execute* lives in the test process's main load context in exactly one copy, and nothing is ever copied out of
the game folder into the build output. The test project deploys nothing, so none of this affects what the game
itself runs.

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

A relative path is resolved against the directory you run from (`build/GamePaths.props`), so run these from the
repo root.

CI runs the suite against both units on every push.

## Reading a failure

Failure messages are version-stamped and carry near-miss diagnosis — the version tested, the signature sought, and what
actually exists under that name — so a target lost to a game update is diagnosed from the message alone. **A red test
right after a game update is the suite doing its job**, not a bug in the suite: the target changed or vanished in that
version, and the patch (or the mod's supported-version claim, see #45/#23) needs updating.

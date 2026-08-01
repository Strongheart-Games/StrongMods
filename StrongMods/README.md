# StrongMods

Modding tools from Strongheart.

* The core library other mods in this collection build on — install it alongside them, not on its own.
* Replaces the vanilla XML patcher with a **breadth-first** one: vanilla patches file-major (every mod's
  `items.xml`, then every mod's `entityclasses.xml`, …), which makes cross-file reads unreliable. StrongMods patches
  mod-major instead — every file for one mod, then the next mod.
* Adds the `<foreach>` XML-patch templating engine: loops, `<bind>` tables and `<function>` blocks usable inside any
  mod's patch files, so repetitive XML can be generated from XML you didn't write. See [
  `Docs/foreach.md`](Docs/foreach.md) for the complete spec.
* Because of the breadth-first ordering, a `<foreach>` can see vanilla XML and any mod *earlier* in load order, but
  never a mod that loads *after* it.
* On case-insensitive filesystems (Windows) it enforces the case-sensitivity rules a Linux server would apply, logging
  path-casing mismatches and unloading mods whose `ModInfo.xml` casing is wrong — so a mod that would break on a Linux
  dedicated server breaks the same way locally.
* Validates the `<Dependencies>` extension to `ModInfo.xml`: mods can declare the game versions and other mods (with
  NuGet-style version ranges like `[1.2,2.0)`) they require, and StrongMods unloads any mod whose requirements are not
  met — completely, before its code, XML patches, or localization take effect — with a clear log message like
  `SomeMod requires StrongUI [1.2,2.0), found 1.1`. Unloading cascades to dependents. Mods whose folders sort before
  `000000-StrongMods` load too early to be unloaded; their violations are still reported. See
  [`Docs/dependencies.md`](Docs/dependencies.md) for how to declare dependencies in your mod.
* Exposes `[XmlPatchFunction]` for C# helpers callable from patch files (must be `public static`, return
  `string`, and take only `string` parameters).
* Adds a `ServerOnlyClass` property to `blocks.xml` that lets modders specify a custom server-side block class. The
  property is ignored by clients, enabling server-only block behavior without requiring the client to have the custom
  class.

## Programmatic Harmony patches

Prefer `[HarmonyPatch]` attribute classes: the repo's test suite (`Tests`) discovers them by enumeration and verifies
their targets still exist in the game assemblies on every run. When a patch *must* be applied programmatically — calling
`harmony.Patch(...)` directly, e.g. to reuse one transpiler across several targets — the call is invisible to that
enumeration, so the targets have to be published instead: a
`public static IEnumerable<MethodBase>` method tagged `[PatchTargetManifest]`, which the patching code itself enumerates
(keeping the published and patched lists identical) and the test suite invokes headlessly. The attribute is inert —
nothing calls a tagged method automatically. See the doc comment in
[`PatchTargetManifestAttribute.cs`](PatchTargetManifestAttribute.cs) for the contract and a usage example; a conformance
test fails any mod that patches programmatically without publishing a manifest. The repo currently has no programmatic
patch site — the last one was converted to categorized attribute patches (#44) — which is the preferred state.

## Installation

* Copy the `StrongMods/` directory into `Mods/`, renamed so it sorts first — the build deploys it as
  `Mods/000000-StrongMods`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/000000-StrongMods/ModInfo.xml`, otherwise the
  mod won't be loaded
* **It must load before every mod that uses it**, because it replaces the XML patcher; the `000000-` prefix is what
  guarantees that
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; each feature (breadth-first patcher, `<foreach>`, case-sensitivity checks,
  dependency validation) is toggled in code and all are on by default, except the case-sensitivity checks which only
  activate on a case-insensitive filesystem

## Changelog

### 1.0.0

* Added `<Dependencies>` validation: mods can declare required game versions and mods in `ModInfo.xml`; violators are
  unloaded (or reported, when they load before StrongMods). See `.ai/modinfo-dependencies-v1-spec.md` for the spec.
* Extracted the mod-unloading machinery (`ModUnloader`) shared by the case-sensitivity checks and dependency validation;
  unload log messages now include the reason

### 0.0.1

* Initial release
* Breadth-first XML patcher, `<foreach>` templating engine, and Linux case-sensitivity enforcement
* Only works against 7DtD v3.x

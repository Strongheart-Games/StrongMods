# StrongMods

Modding tools from Strongheart.

* The core library other mods in this collection build on — install it alongside them, not on its own.
* Replaces the vanilla XML patcher with a **breadth-first** one: vanilla patches file-major (every mod's
  `items.xml`, then every mod's `entityclasses.xml`, …), which makes cross-file reads unreliable. StrongMods
  patches mod-major instead — every file for one mod, then the next mod.
* Adds the `<foreach>` XML-patch templating engine: loops, `<bind>` tables and `<function>` blocks usable inside
  any mod's patch files, so repetitive XML can be generated from XML you didn't write.
  See [`Docs/foreach.md`](Docs/foreach.md) for the complete spec.
* Because of the breadth-first ordering, a `<foreach>` can see vanilla XML and any mod *earlier* in load order,
  but never a mod that loads *after* it.
* On case-insensitive filesystems (Windows) it enforces the case-sensitivity rules a Linux server would apply,
  logging path-casing mismatches and unloading mods whose `ModInfo.xml` casing is wrong — so a mod that would
  break on a Linux dedicated server breaks the same way locally.
* Exposes `[XmlPatchFunction]` for C# helpers callable from patch files (must be `public static`, return
  `string`, and take only `string` parameters).

## Installation

* Copy the `StrongMods/` directory into `Mods/`, renamed so it sorts first — the build deploys it as
  `Mods/0000_StrongMods`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/0000_StrongMods/ModInfo.xml`,
  otherwise the mod won't be loaded
* **It must load before every mod that uses it**, because it replaces the XML patcher; the `0000_` prefix is
  what guarantees that
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now; each feature (breadth-first patcher, `<foreach>`,
  case-sensitivity checks) is toggled in code and all are on by default, except the case-sensitivity checks
  which only activate on a case-insensitive filesystem

## Changelog

### 0.0.1

* Initial release
* Breadth-first XML patcher, `<foreach>` templating engine, and Linux case-sensitivity enforcement
* Only works against 7DtD v3.x

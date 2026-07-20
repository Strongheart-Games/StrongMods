# Template7DtDModlet

One sentence describing what this modlet does, matching the `Description` in `ModInfo.xml`.

* Replace these bullets with the concrete specifics and limitations of the modlet.
* Keep them factual — list the actual XML changes made (items, blocks, recipes, entities, localization).
* Note which vanilla or third-party XML this patches, and any mod it depends on or must load after.

## Installation

* Copy the `Template7DtDModlet/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/Template7DtDModlet/ModInfo.xml`, otherwise
  the modlet won't be loaded
* This modlet is XML-only and ships no DLL
* Dedicated servers:
  * Install on the server
  * EAC-friendly
  * Install on clients as well if this modlet adds or renames items, blocks, recipes, or localization — otherwise
    clients will see raw localization keys or fail to resolve the new content
* All other deployments:
  * Deploy to host (in single-player this is your game)
* Load order:
  * If this modlet patches XML belonging to another mod, it must load *after* that mod
  * Load order follows the folder name, so prefix the folder (e.g. `Z_Template7DtDModlet`) to force it later in the
    order — set this via the Debug `OutputPath` in the `.csproj`
* There are no configuration options for now

## Changelog

### 0.0.1

* Initial public release
* Only works against 7DtD v3.x
* Tested against 7DtD v3.0.1 b4

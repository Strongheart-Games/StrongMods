# PrismaCoreFixes

Fixes for critical issues in PrismaCore.

* Requires PrismaCore. If PrismaCore is not loaded, the mod logs a message and applies no patches, so it is harmless
  but useless on its own.
* PrismaCore's chat filter swallows *every* chat message that begins with its command prefix, including commands that
  belong to other mods. This mod patches the filter so that only PrismaCore's own commands are consumed; anything else
  starting with the prefix is passed back to the game and other mods can handle it.
  * The recognised PrismaCore commands are: `ft`, `ftw`, `mv`, `mvw`, `tb`, `rt`, `get`, `listwp`, `setwp`, `delwp`,
    `ls`, `bag`, `day7`, `hostiles`, `bed`, `loctrack`, and `bubble`.
  * Command matching is case-insensitive, and the prefix is read from PrismaCore's own settings, so a custom prefix
    works.

## Installation

* Copy the `PrismaCoreFixes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/PrismaCoreFixes/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options

## Changelog

### 1.0.0

* Initial release

# ChatCommandHelper

Help players make better use of chat commands.

* Replies to unrecognized `/commands` with a whisper instead of letting them fall through silently.
* Hidden commands are treated as if they don't exist: the player is told the command is unrecognized and the command
  never reaches vanilla or any other mod.
* Privileged commands are gated on a player CVar; players without it get a whisper explaining they aren't authorized.
* Commands marked as async get a "Processing command: ..." whisper, so players know a slow command was accepted.
* Requests coming from the server itself (no player entity) are always treated as authorized.
* The rejection message shown for privileged commands is currently hard-coded flavor text and is not configurable.

## Installation

* Copy the `ChatCommandHelper/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/ChatCommandHelper/ModInfo.xml`, otherwise the
  mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled

## Configuration

The mod ships no config of its own. It reads four optional properties from `worldglobal.xml` under
`/worldglobal/environment`, which you add from your own modlet. All are re-read whenever the XML is (re)loaded.

* `chat_command_helper_hidden_commands` — comma-separated command names to hide
* `chat_command_helper_privileged_commands` — comma-separated command names that require authorization
* `chat_command_helper_async_commands` — comma-separated command names that are acknowledged rather than answered
* `chat_command_helper_authorized_cvar` — the player CVar checked for privileged commands (default `strongsworn`);
  any non-zero value authorizes the player

Command names are listed without the leading `/` and are matched case-insensitively.

## Changelog

### 1.0.0

* Initial release

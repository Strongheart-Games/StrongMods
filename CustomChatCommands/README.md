# CustomChatCommands

Define custom chat commands in XML, with permission gating and hot reload.

* Each command declares a `trigger` (matched against the first word of the chat message), optional comma-separated
  `aliases`, and a list of actions to run.
* Three action types: `console` (runs a server console command), `whisper` (private reply to the sender), and
  `broadcast` (global chat message).
* Action text supports the placeholders `{Name}`, `{EntityId}`, `{PlatformId}` and `{EOSId}`; they are case-sensitive.
* Access is gated by `minAdminLevel` and by optional `cvar` requirements; `cvar` is the only requirement type
  implemented so far.
* An unauthorized player sees the `OnUnauthorized` actions if any are defined; otherwise the command is silently
  ignored, as if it didn't exist.
* The config file is watched and reloaded on change, so commands can be edited without restarting the server.

## Installation

* Copy the `CustomChatCommands/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/CustomChatCommands/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled

## Configuration

Commands are defined in `custom_chat_commands.xml`, in the `StrongMods` directory of your save game
(`<SaveGameRootDir>/StrongMods/custom_chat_commands.xml`). If the file doesn't exist it is created on first run,
containing commented-out examples of each feature.

Note that `minAdminLevel` is an upper bound on the player's permission level number, following the game's convention
that lower numbers mean more privilege. It defaults to `1000`, i.e. everyone. Players flagged as admin bypass the
level check entirely.

## Changelog

### 0.0.1

* Initial pre-release

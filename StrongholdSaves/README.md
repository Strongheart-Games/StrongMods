# StrongholdSaves

Stronghold-specific configuration that lives in the game's `Saves/` tree rather than in `Mods/`.

* Not a mod: there is no `ModInfo.xml`, no DLL, and nothing here appears in the game's mod list. It is an
  **Overlay** project (`build/Overlay.targets`) — save/world-related config that installs into the game-managed
  save directory, which `Mods/` content should not own.
* Currently ships one file: `StrongMods/custom_chat_commands.xml`, the chat-command definitions (`/horde`,
  `/stronghold`, `/bed`, `/resetdrone`, `/resetme`) read from the save game directory by the custom-chat-command
  mods (`CustomChatCommands`, helpers in `StrongUtils`).
* Split out of `StrongholdTweaks` (issue #25): the config predates the mod templates and was conflated into the
  modlet. Like StrongholdTweaks, it is opinionated Stronghold-server configuration, not for general installation.
* Deploys **additively**: only the file above is managed (mirrored); everything else in `Saves/` — player saves,
  world data, runtime-written files — is never touched or deleted.

## Installation

* From the repo: `dotnet build StrongholdSaves/StrongholdSaves.csproj -t:Deploy` installs into the client/host
  saves directory (`%APPDATA%/7DaysToDie/Saves` by default; override with `-p:SdtdSavesDir=...`)
* By hand: copy the `StrongMods/` directory into the game's `Saves/` directory
* Dedicated servers:
  * Copy into the server's save-data location (its `UserDataFolder`, as configured in `serverconfig.xml`), or
    deploy with `-p:SdtdSavesDir=` pointed there
* Requires the mods that implement custom chat commands; without them the file is inert
* Configuration is the file itself — edit the XML to change the commands

## Changelog

### 1.0.0

* Split out of StrongholdTweaks as an Overlay project
* Content unchanged from StrongholdTweaks 13.0.0
* Only works against 7DtD v3.x

# DynamicLandClaimCount

Set Land Claim Count, which is normally global, on a per-player basis.

* Reads one or more player cvars and either adds them to, or overrides, the `LandClaimCount` from `serverconfig.xml`.
* A player who does not have the cvar set, or has it set to a negative value, falls back to the `serverconfig.xml`
  value.
* Players can type `/claims` in chat to be whispered their current claim usage, e.g. `Your land claims: 2 / 5`.
* The same whisper is sent automatically whenever a player places or removes a land claim block.
* Extra claims beyond a player's allowance are still removed by the vanilla cleanup, just against the adjusted count.

## Installation

* Copy the `DynamicLandClaimCount/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/DynamicLandClaimCount/ModInfo.xml`, otherwise
  the mod won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* Configuration lives in `Config/worldglobal.xml`, which appends two properties to `/worldglobal/environment`:
  * `dynamic_land_claim_count_cvar` — a comma-separated list of cvars to read. Defaults to
    `biomeWeatherItem1,biomeWeatherItem2,biomeWeatherItem3,biomeWeatherItem4`
  * `dynamic_land_claim_count_op` — `add` to add the cvar values to the `serverconfig.xml` count, or `override` to use
    the cvar value as the count. Defaults to `add`
  * The whisper text is in `Config/Localization.csv` under the `dynamic_land_claim_count_message` key, and supports the
    `{used}` and `{total}` placeholders

## Changelog

### 1.1.0

* Initial public release
* Only works against 7DtD v3.x

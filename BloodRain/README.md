# BloodRain

Blood Rains are like Blood Moons, but they trigger based on IRL time, for the convenience of the players.

* Blood rains are scheduled with a cron expression evaluated in the operating system's local time zone, and last a
  configurable number of real-life minutes.
* Vanilla blood moons are disabled: `BloodMoonFrequency` is forced to `0` at startup (with a warning logged if it was
  set to anything else), and the game's blood-moon checks are rerouted to the blood rain state.
* Players get a chat countdown every minute for the configured lead time, plus an optional second warning message of
  your choosing.
* While a blood rain is active, players receive the `buff_blood_rain` buff showing the remaining time, and a dedicated
  `bloodRain` weather profile is applied for the duration and reverted when it ends.
* Players present for the whole event receive the `buff_blood_rain_survived` buff; the vanilla "survive a blood moon"
  challenges are rewired to count blood rains survived instead. Dying, unloading, or logging out during a blood rain
  drops you from the survivor list.
* Blood rains are skipped entirely if the countdown would start before the configured minimum game day.
* In-game chat command `/bloodrain` (alias `/br`) whispers the time until the next blood rain, or until the current one
  ends.
* Server console command `bloodrain` (alias `br`) supports `start [duration_irl_minutes]`, `skip`, and `stop`.

## Installation

* Copy the `BloodRain/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/BloodRain/ModInfo.xml`, otherwise the mod
  won't be loaded
* Dedicated servers:
  * Server-side only — the buffs, weather, and challenge changes are XML the server pushes to clients, and every icon
    and sound they reference is a vanilla asset
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* Configuration lives in `Config/worldglobal.xml`, in the `blood_rain` property class:
  * `schedule_irl` — a cron expression (minute, hour, day of month, month, day of week) in the OS time zone. Defaults
    to `0 20 * * *`, i.e. daily at 8 PM. Leave empty to disable scheduled blood rains
  * `duration_irl_minutes` — how long a blood rain lasts. Defaults to `15`
  * `countdown_irl_minutes` — how long before the start to begin warning players. Defaults to `15`
  * `min_game_day` — skip the blood rain if the countdown starts before this game day. Defaults to `7`
  * `second_warning_message` — optional extra message broadcast after each countdown warning. Empty by default
  * `party_enemy_count_max` — max active enemies per spawn party during a blood rain. Defaults to `30`

## Changelog

### 1.1.0

* Added the `/bloodrain` (`/br`) chat command, whispered to the requestor only
* Added `party_enemy_count_max` configuration for the max enemy count per blood rain party
* Only works against 7DtD v3.x

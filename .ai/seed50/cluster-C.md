# Cluster C — loot / quest / time / network-behavior mods

Seed analysis for #50. Scope: V3.1.0 (b14). Paper analysis only — read from source `.cs` + `Config/**`; nothing booted.
Tags: [T1] server-side telnet-drivable · [T2] client→server protocol · [IL] patch-target resolution · [UNIT] off-game
logic · [LINT] XML well-formedness · [IN-GAME] needs #49 runner / real client.

---

### AutoCollectLoot  (shape: code + xml; side: server)
Suppresses the vanilla loot-bag drop and instead routes the bag's contents to a chosen player. `Entity.DropBagServer`
prefix: if the dying entity carries `buff_auto_collect_loot`, `TryCollect` picks a recipient and either adds the
substitute item to a local player's `bag` or (remote player) spawns a transient `EntityItem` and sends
`NetPackageEntityCollect`; the prefix returns `!collected`, so a successful collect suppresses the drop. A buff is
attached in `EntityAlive.OnAddedToWorld` (horde zombies always; others only when `IsEnabledNow()`). `LootItems` maps
entity-class name → `ItemClass` via the `AutoLootSubstituteFor` item property; the XML `<foreach>` (uses StrongMods'
engine — hence the `ProjectReference`) generates one `AutoLoot_<container>` item per non-`cntDropBag` loot container.

- [IL] Patch targets `Entity.DropBagServer`, `EntityAlive.OnAddedToWorld`, `WorldEnvironment.OnXMLChanged` all resolve
  against b14 game assemblies → each patch still binds. Tool: existing `Tests/` IL resolver. (existing)
- [UNIT] `LootItems.TryGetLootItem`: given an `ItemClass` set where one item has `AutoLootSubstituteFor="zombieX"`,
  a lookup of `"zombieX"` returns that item and an unknown name returns false. Tool: GAP — needs a way to construct/seed
  `ItemClass.nameToItem` off-game (the stub does not populate game item tables); today only reachable with real game
  data. (GAP)
- [T1] Suppression: with the mod loaded and a zombie killed by a telnet-spawned means, its loot bag does NOT appear as a
  world drop and the collect is logged. Tool: GAP — needs a telnet-scriptable "kill an entity and read back whether a
  dropped-bag TileEntity/EntityItem exists near the death position". (GAP)
- [T1] Config toggle: `autoloot disable` then a kill → the bag drops normally again; `autoloot` with no args reports the
  current `Enabled`/`EnabledOutsideBloodMoon`. Tool: existing Tier-1 telnet console (the state-report path is pure
  console I/O). (existing for the report; the drop-observation half is the GAP above)
- [IN-GAME] Remote-recipient path: a real connected client with a full inventory receives the `NetPackageEntityCollect`
  and the item lands in inventory (or the 20-min fallback `EntityItem` persists). Can't be headless — needs a spawned
  player entity holding inventory and rendering collection; Tier-2 has no `entityId`/spawn. (GAP → #49)
- [LINT] `Config/items.xml` (foreach-bearing) + `worldglobal.xml` are well-formed. Tool: existing build XML lint;
  structural validity of the *foreach-expanded* result is not covered. (existing / partial)

Notable gaps: (1) off-game construction of `ItemClass`/`nameToItem` for loot-mapping unit tests; (2) a telnet-scriptable
"did a loot bag drop into the world at a death position" observation primitive; (3) foreach-expansion output validation
beyond raw well-formedness.

---

### BloodRain  (shape: code + xml; side: server)
Replaces vanilla blood-moon scheduling with a real-world clock schedule. A Cronos `CronExpression` drives
`NextStartTime`; `GameManager.Update` prefix pumps `BloodRain.Update()`, which warns players on a shrinking countdown,
starts/stops the event, gates the first event behind `min_game_day`, and forces `bloodRain`/`default` weather.
`GameUtils.IsBloodMoonTime` and `World.IsWorldEvent` are prefixed to report blood-rain state; a transpiler on
`AIDirectorBloodMoonParty.InitParty` swaps the hard-coded `30` enemy cap for `party_enemy_count_max`. `OnGameAwake`
forces vanilla `BloodMoonFrequency` to 0. **All timing reads `DateTime.Now` directly — no injectable clock.**

- [UNIT] Cron scheduling: `GetNextStartTime("0 20 * * *", from=X)` yields the next 8 PM local after X;
  `min_game_day` gate — a start whose countdown fires while `WorldDay < min_game_day` is skipped, not started. Tool:
  GAP — the logic is testable in principle but reads `DateTime.Now`/`TimeZoneInfo.Local` internally, so it needs a
  **clock/timezone seam** (inject an `IClock`/`Func<DateTime>`) before any deterministic unit test is possible. This is
  the cluster's clearest "deterministic time control is a tool/refactor requirement" case. (GAP)
- [UNIT] Pure formatting helpers, already side-effect-free given inputs: `FormatTimeSpan` (0/1/2 hrs+mins pluralization)
  and `TimeSpanExtensions.ToDynamicReadableString` (first significant unit + one more only when primary < 3). Tool:
  existing off-game unit harness — these take `TimeSpan` args, no clock needed. (existing, GAP-free)
- [IL] Targets resolve: `GameManager.Update`, `World.IsWorldEvent`, `WorldEnvironment.OnXMLChanged`, the three
  `EntityAlive` events, `AIDirectorGameStagePartySpawner.SetupGroup`, and especially the **overload-sensitive**
  `GameUtils.IsBloodMoonTime((int,int),int,int,int)` and the `AIDirectorBloodMoonParty.InitParty` transpiler's inner
  match (`FastMin` + `enemyActiveMax` store). Tool: existing IL resolver — high value because the transpiler and the
  overload pin are exactly what a game update silently breaks. (existing)
- [T1] Admin control: `bloodrain start 2` sets blood-rain state true, broadcasts the start message, forces `bloodRain`
  weather; `bloodrain stop` clears it and restores default weather. Tool: existing Tier-1 telnet (drive the command,
  assert via a state-reporting command or log). Reading back weather state may need a GAP observer. (existing / partial)
- [T1] BloodMoonFrequency override: after `GameAwake`, `EnumGamePrefs.BloodMoonFrequency == 0` even when the server
  config set it nonzero. Tool: GAP — needs a telnet-readable `GamePrefs` value (a `getgamepref`-style read). (GAP)
- [LINT] `worldglobal.xml`, `challenges.xml`, `biomes.xml`, `buffs.xml` well-formed. Tool: existing build lint.
  (existing)

Notable gaps: (1) **clock/timezone injection seam** — without it BloodRain's core scheduling logic is untestable
off-game and non-deterministic on-game; the biggest single tool/refactor requirement in this cluster; (2) telnet-
readable `GamePrefs`/weather-state observers for the T1 assertions.

---

### BountifulQuests  (shape: code + xml; side: server)
**The C# is currently a no-op.** `ReplaceCompletedQuests` is an empty class and the one Harmony patch
(`QuestEventManager.GetQuestList` postfix) contains only `// TODO: Implement`. The *actual* shipped behavior is entirely
in `Config/dialogs.xml`: it moves the `resetquests` response from the trader admin menu into the player-facing menu, and
removes the `jobsnone` response plus the `QuestStatus` requirement on `jobshave*` so a trader always offers jobs even
after one is accepted (the "accept multiple quests + reset offered list" feature).

- [IL] `QuestEventManager.GetQuestList(World, int, int)` postfix target resolves against b14. Tool: existing IL
  resolver — cheap, and it guards the signature the eventual implementation will hook. (existing; but see gap)
- [LINT] `dialogs.xml` is well-formed. Tool: existing build lint. (existing)
- [T1/IN-GAME] Dialog behavior: opening a trader dialog offers a job even when the player already holds an active quest
  from that trader, and `resetquests` appears in the player menu. Tool: GAP — trader dialog flow needs a real player
  interacting with an NPC dialog tree; no headless driver reaches it. (GAP → #49)
- Coverage note: **an assertion that GetQuestList is *unmodified* would be the truthful current test.** Any test named
  for "accept multiple quests" via the code path would be testing unimplemented behavior — the value here is a doc/test
  that pins the XML-only reality and flags the dormant TODO patch.

Notable gaps: (1) trader-dialog interaction driver (real player + NPC dialog tree) — headless-unreachable; (2) an
XPath-effect validator that asserts a `dialogs.xml` patch actually removed/added the intended nodes (stronger than
well-formedness, weaker than a live trader) — overlaps the planned #41 structural validator.

---

### QuestUnlockFixes  (shape: code; side: server)
Single transpiler on `QuestEventManager.QuestUnlockPOI` that relocates the `prefabFromWorldPos.lockInstance` null-check/
branch to immediately after `prefabFromWorldPos` is stored, so a null prefab (e.g. player who unlocked a POI then quit)
no longer NREs and blocks full logout. A diagnostic `Prefix` logs when `prefabFromWorldPos` is null.

- [IL] The transpiler's whole existence is IL-shaped: it must find `ldloc prefabFromWorldPos`, the
  `PrefabInstance.lockInstance` load + branch, and the `stloc prefabFromWorldPos` insertion point. If any moves, the
  `ThrowIfInvalid` fires at patch time. Tool: existing IL resolver / patch-apply test — **highest-value test for this
  mod**; it is pure transpiler and brittle by nature. (existing)
- [UNIT] The reordering's *effect* — that the null-guard now precedes the deref — is a property of emitted IL. Tool:
  GAP — verifying transpiler output semantics (not just that it applied) needs a "apply patch, then assert the branch
  now dominates the deref" IL-diff harness; no such tool exists. (GAP)
- [T1] Full-logout regression: a player who accepts/unlocks a POI quest and then disconnects fully leaves the server
  (no lingering session) even when the prefab is gone. Tool: GAP — needs a telnet-driven "unlock POI as player, then
  force disconnect, assert clean logout"; requires a player entity with quest state → currently IN-GAME. (GAP → #49)

Notable gaps: (1) transpiler-*effect* verification (IL-diff / post-patch semantic assert), distinct from target
resolution; (2) player-quest + logout lifecycle driver.

---

### LootDiagnostics  (shape: code; side: server)
Pure observability: a postfix on `EntityClass.LootDropPick` logs `<entity> dropped loot <lootEntityClass>` for every
loot roll. No behavior change.

- [IL] `EntityClass.LootDropPick` postfix (with `ref int __result`) resolves against b14. Tool: existing IL resolver.
  (existing)
- [T1] Log emission: after a kill/loot roll, the server log contains a `[LootDiagnostics] ... dropped loot ...` line.
  Tool: existing Tier-1 telnet + log scraping — but see gap: it depends on triggering a loot roll headlessly. Tool:
  GAP — a telnet primitive to force a loot roll / kill an entity server-side. (existing log-scrape; GAP for the trigger)

Notable gaps: shares AutoCollectLoot's need for a telnet-scriptable "kill entity / force loot roll" trigger.

---

### DisableLAN  (shape: code; side: server)
Prefix on `LANMasterServerAnnouncer.AdvertiseServer` that skips creating the LAN listener (returns false) while still
invoking `_onServerRegistered()`, so nothing binds UDP port 11000. Already relied on by the headless-server harness to
avoid the ~3s LAN-announce cost and the Windows firewall prompt (see `StrongDev/.ai/headless-server-testing.md` §6).

- [IL] `LANMasterServerAnnouncer.AdvertiseServer(Action)` prefix target resolves against b14 (note the `Platform.LAN`
  namespace + the single `Action` param — an overload/namespace-sensitive target). Tool: existing IL resolver.
  (existing)
- [T1] Port not bound: with the mod loaded, after startup nothing is listening on UDP 11000; the log shows
  `[DisableLAN] Skipping LAN listener creation`; server still reports itself registered (callback fired). Tool: GAP —
  needs a "assert no process/socket bound to UDP :11000" probe from the harness (a socket/port observer), plus the
  existing log scrape. This is the most directly testable behavioral claim in the cluster and the harness already
  demonstrates the setup. (partial existing + GAP)
- Testability note: because the harness *already* loads DisableLAN, it is the natural first candidate for a
  behavioral-negative test ("the thing that should NOT happen") — but proving a negative (no bind) is exactly what the
  current toolset lacks a primitive for.

Notable gaps: a socket/port-binding observer usable from the Tier-1 harness (assert a UDP/TCP port is *not* bound).

---

## Distinct tool gaps this cluster exposes

1. **Deterministic clock + timezone injection (BloodRain).** Core scheduling reads `DateTime.Now`/`TimeZoneInfo.Local`
   inline; it is neither unit-testable off-game nor deterministic on-game until a clock seam exists. Highest-leverage
   gap; part refactor, part test-harness capability.
2. **Off-game construction of game data tables** (`ItemClass.nameToItem`, `EntityClass.list`) for AutoCollectLoot's
   loot-mapping and lottery logic — the UnityEngine stub does not populate them.
3. **Telnet-scriptable "kill an entity / force a loot roll" trigger** + **"was a loot bag / EntityItem dropped near
   position P" observer** (AutoCollectLoot, LootDiagnostics).
4. **Socket/port-binding observer** — assert a given UDP/TCP port (11000) is or is not bound from the Tier-1 harness
   (DisableLAN).
5. **Telnet-readable `GamePrefs` / weather-state observers** for confirming server-state side effects (BloodRain's
   BloodMoonFrequency=0 and forced weather).
6. **Transpiler-*effect* verification (IL-diff / post-patch semantic assert)**, distinct from target resolution
   (QuestUnlockFixes, and BloodRain's InitParty transpiler) — today IL tests prove a patch *applies*, not that the
   rewritten IL does the intended thing.
7. **XPath-patch effect validator** (BountifulQuests dialogs.xml) — assert nodes were actually removed/added; overlaps
   planned #41.
8. **Real-player / NPC-dialog / quest-lifecycle driver** (BountifulQuests trader dialogs, QuestUnlockFixes logout,
   AutoCollectLoot remote recipient) — headless-unreachable, maps to the #49 in-game runner.

## Cross-cutting finding
**BountifulQuests' C# is a no-op** (empty class + `// TODO: Implement` postfix); its shipped behavior is 100% in
`dialogs.xml`. Any test framed around its code path would test unimplemented behavior — the honest test pins the XML
effect and flags the dormant patch.

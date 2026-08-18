# BountifulQuests: bigger, tunable trader offer lists — prototype plan

Covers #32 (bigger offer list) and #33 (tunable selection). Game facts this plan rests on are in
`vanilla-offer-generation.md`; read that first.

**Status: prototype.** Built unattended overnight on 2026-08-17 for review, not for a live server. Nothing here has run
in the game.

## Non-negotiables taken from the request

1. Server-side only. No new XUi controller types. XUi *XML* changes are allowed and are server-syncable.
2. Offer count is configurable, with bounds — minimum 4, maximum 64.
3. Configurable POI distance, and configurable per-quest-type probability.
4. A vanilla-versus-mod comparison report.

## Shape of the change

Two Harmony patches and two XML patches.

| Piece                                        | Kind             | Why                                                                                     |
|----------------------------------------------|------------------|-----------------------------------------------------------------------------------------|
| `EntityTrader.PopulateActiveQuests`          | Harmony prefix   | The only place the offer list is built. Replaced outright with a config-driven version. |
| `DynamicPrefabDecorator.GetRandomPOINearTrader` | Harmony prefix | The only place a POI is chosen. Replaced to honour a real distance window.              |
| `Config/dialogs.xml`                         | XML patch        | Adds `<quest_entry>` rows 7..63 to each `currentjobsN` statement.                       |
| `Config/XUi_InGame/windows.xml`              | XML patch        | Grows the `windowResponses` grid so the extra rows have somewhere to render.            |

The existing `QuestEventManager.GetQuestList` stub patch is **deleted**. It sits on a cache read, not on generation
(see the research note), so it is the wrong seam and has never done anything.

### Why replace rather than transpile

A transpiler could widen `distanceIndices.Length` and nothing else. Type weights, a distance window, and repeat
avoidance all need decisions vanilla never makes, so #33 needs the whole loop either way. Replacing once is smaller
than transpiling three times.

The cost is honest and should be recorded: the mod now owns a copy of a game algorithm and will drift on game updates.
The prefix keeps vanilla's observable contract — same return type, same `usedPOILocations` and `uniqueKeysUsed`
bookkeeping, same special-quest pass, same final distance sort — so a game-side change to *those* still shows up as a
test failure rather than as silent divergence.

### Why per-tier pages, not one flat list

`XUiC_QuestOfferWindow` removes an accepted quest by (tier, index-within-tier). A flat index removes the wrong quest.
Detail in the research note. So the configured count is **quests per tier page**, which is also the unit vanilla uses
(7) and the unit #32 is asking about.

## The config surface

One file, `Config/BountifulQuests.xml`, read at `InitMod` from the mod's own folder. XML, because every other knob a
7DtD admin touches is XML, and because it keeps the mod free of a dependency on `StrongUtils.ConfigManager` (a
different, standalone mod — not a library this one may link against).

```xml
<BountifulQuests>
  <offers per_tier="12" min_tier="1" max_tier="0" sort="distance" />
  <distance min="0" max="0" near="500" mid="1500" band_weights="3,2,1" />
  <types default_weight="1">
    <type match="*_clear"           weight="3" max="4" />
    <type match="*_clear_infested"  weight="2" max="2" />
    <type match="*_fetch"           weight="2" max="3" />
    <type match="*_fetch_clear"     weight="2" max="3" />
    <type match="*_restore_power"   weight="1" max="2" />
    <type match="*_buried_supplies" weight="1" max="2" />
  </types>
  <repeats avoid_offered="true" remember_completed="0" />
</BountifulQuests>
```

| Knob                       | Default    | Bounds        | Effect                                                                                        |
|----------------------------|------------|---------------|-------------------------------------------------------------------------------------------------|
| `offers/@per_tier`         | 7          | 4..64         | Quests generated and shown per tier page. 7 reproduces vanilla.                                |
| `offers/@min_tier`         | 1          | 1..6          | Lowest tier page still offered. Raise it to retire trivial low-tier work.                       |
| `offers/@max_tier`         | 0          | 0..6          | Highest tier page. `0` means "the player's current faction tier", which is vanilla.             |
| `offers/@sort`             | `distance` | see below     | `distance` (vanilla, nearest first), `tier`, `type`, or `none`.                                 |
| `distance/@min`            | 0          | 0..100000     | Metres from the trader area. A POI closer than this is rejected. `0` disables.                  |
| `distance/@max`            | 0          | 0..100000     | Metres from the trader area. A POI further than this is rejected. `0` disables. **New ability.**|
| `distance/@near`           | 500        | > 0           | Upper edge of the near band. Vanilla constant, now editable.                                    |
| `distance/@mid`            | 1500       | > `near`      | Upper edge of the mid band.                                                                     |
| `distance/@band_weights`   | `3,2,1`    | 3 integers    | How many of each page's offers prefer near / mid / far. Vanilla is effectively `3,1,3`.         |
| `types/@default_weight`    | 1          | >= 0          | Weight for a quest id no `<type>` row matches.                                                  |
| `type/@match`              | —          | glob on quest id | `*` and `?` wildcards, case-insensitive. Matches other mods' quests too.                     |
| `type/@weight`             | 1          | >= 0          | Relative draw weight. `0` removes the type from offers entirely.                                |
| `type/@max`                | 0          | >= 0          | Cap on that type per tier page. `0` means no cap.                                               |
| `repeats/@avoid_offered`   | `true`     | bool          | Never offer the same quest id twice on one page while another id is still eligible.             |
| `repeats/@remember_completed` | 0       | 0..1000       | Extra completed-POI memory beyond vanilla's per-tier list. `0` keeps vanilla behaviour.         |

Out-of-range values are clamped, logged once, and the mod carries on. A missing or unreadable file means "defaults",
and defaults mean "vanilla", so a broken config never bricks a server's questing.

### Knobs deliberately not offered

* Reward or difficulty scaling — #33 puts tier and difficulty spread explicitly out of scope.
* Per-trader overrides — real, but it needs a second config axis; raise separately if wanted.
* Hot reload — the file is read once at `InitMod`. A reload console command is a small follow-on, not prototype work.

## Distance is the knob with the most teeth

Vanilla cannot cap distance at all. The band walk in `GetRandomPOINearTrader` falls through to *any* band when the
preferred one is exhausted, which is why players on a sparse map get 2 km fetch quests they never asked for. The
replacement keeps the band preference but treats `distance/@min` and `distance/@max` as hard filters, and if nothing
in the window qualifies it returns no POI — that quest is simply not offered, rather than being offered at the wrong
distance.

That is a behaviour change worth flagging in the report: a tight window on a sparse map yields **short pages**, not
distant quests. That is the intended trade, but an admin must be told about it.

## Verification plan

What is achievable off-game:

1. `dotnet build BountifulQuests/BountifulQuests.csproj -c Debug` — must pass.
2. `dotnet test StrongMods.sln -c Debug` — the Harmony patch-target resolution suite. Both new targets must resolve
   against every declared test version. This is the gate that catches a renamed or re-signatured game method.
3. The XML lint that runs inside every build covers the two patch files.
4. A pure-C# selection harness: the weighting, capping, ordering and clamping logic is separated from all game types so
   it can be exercised without the game. Distribution over many draws is asserted, not eyeballed.

What is **not** achievable off-game, and must be an in-game pass (#49):

* That a trader actually renders 12 rows and that clicking row 9 accepts quest 9.
* That the XUi grid change survives being pushed to a client.
* That POI selection under a distance cap finds anything on a real world.

## Work order

1. Research note. (done)
2. This plan. (done)
3. Config model and loader, with clamping.
4. Selection core, free of game types, plus its harness.
5. The two Harmony patches wiring the core to the game.
6. The two XML patches.
7. Build, test, comparison report.

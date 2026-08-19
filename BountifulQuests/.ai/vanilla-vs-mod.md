# Vanilla vs BountifulQuests: what a server admin gains

Written 2026-08-17 against game tree `V3.1.0-b14`. Every "vanilla" claim below is read out of `Assembly-CSharp.dll` or
the shipped `Data/Config`, and cited in `vanilla-offer-generation.md`.

**Prototype.** Everything here builds and passes the repo suite. Nothing has been run in the game.

## The short version

| What an admin wants                                | Vanilla                                      | With this mod                                   |
|----------------------------------------------------|----------------------------------------------|-------------------------------------------------|
| More quests offered at once                        | 7 per tier page, fixed in C#                 | 4..64 configurable, 21 reachable on screen today |
| Fewer / more low-tier pages                        | Always tier 1 up to the player's tier        | `min_tier` and `max_tier`                       |
| Bias the quest-type mix                            | **Nothing.** Uniform draw over `quest_list` rows | Per-type weights, and per-type caps per page |
| Turn a quest type off                              | Delete rows from `quests.xml`                | `weight="0"`, no vanilla XML touched            |
| Cap how far quests send players                    | **Impossible.** No cap exists                | Hard `min` / `max` metres                       |
| Change the near / mid / far band edges             | Hardcoded 500 m and 1500 m                   | `near` and `mid`                                |
| Change how many offers prefer near vs far          | Hardcoded `{0,0,0,1,2,2,2}`                  | `band_weights`                                  |
| Order the page                                     | Nearest first, fixed                         | `distance`, `tier`, `type`, `none`              |
| Stop the same quest repeating on one page          | **Nothing**                                  | `avoid_offered`                                 |
| Avoid recently completed POIs                      | Yes — per trader, per tier, 7-day reset      | Unchanged                                       |
| Stack quests from several traders                  | Blocked until the active one is done         | Allowed (already shipped in 2.0.0)              |
| Reset the offered list on demand                   | Admin dialog only                            | Any player (already shipped in 2.0.0)           |

Client install required: **no**, in either case. That was already true and the old README said otherwise.

## Offer count, concretely

Vanilla builds up to 7 quests for **each** tier from 1 to the player's faction tier, and the dialog pages between
tiers. A tier-6 player is therefore already carrying up to 42 offers; they just see 7 at a time.

| Player faction tier | Vanilla total offers | With `per_tier="12"` | With `per_tier="12" min_tier="4"` |
|---------------------|----------------------|----------------------|-----------------------------------|
| 1                   | 7                    | 12                   | 0                                 |
| 3                   | 21                   | 36                   | 0                                 |
| 6                   | 42                   | 72                   | 36                                |

`min_tier` is the interesting one. It is the only way to stop a late-game player wading through tier-1 pages, and
vanilla has no equivalent.

## The ceiling nobody can design around

**A trader's tier-6 quest pool is one quest.**

Counting `trader_rekt_quests` in vanilla `quests.xml`, after removing the special trader-to-trader entry:

| Tier | Distinct quest kinds the trader can offer                                                                    |
|------|--------------------------------------------------------------------------------------------------------------|
| 1    | 3 — clear, fetch, buried supplies (plus the one-off intro)                                                    |
| 2    | 6 — clear, clear infested, fetch, fetch+clear, restore power, buried supplies                                 |
| 3    | 6 — same six                                                                                                  |
| 4    | 4 — clear, clear infested, restore power, fetch+clear                                                         |
| 5    | 3 — clear, clear infested, fetch+clear                                                                        |
| 6    | **1** — `tier6_clear_infested`                                                                                |

So a 12-offer tier-6 page is twelve infested clears at twelve different POIs, and no weighting can change that: there
is nothing to weight it against. Vanilla's own 7-offer tier-6 page has the same problem, just less of it.

What this means in practice:

* Weights earn their keep at **tiers 2 and 3**, where six kinds compete.
* `avoid_offered` earns its keep everywhere `per_tier` exceeds the pool, which above tier 3 is most settings.
* Genuine variety at tiers 5 and 6 needs **new quest content**, not tuning. That is a separate piece of work.
* `max_tier` is the blunt instrument that avoids the problem: cap pages at 3 or 4 and the mix stays varied.

This is the single most useful thing the research turned up, and it is worth deciding about before tuning anything.

## Distance: the largest real gain

Vanilla's three POI buckets — up to 500 m, 500 to 1500 m, beyond 1500 m — are a **preference with a silent fallback**.
`GetRandomPOINearTrader` starts at the preferred bucket and walks the other two when it comes up empty. So a trader
whose near bucket is thin hands out 2 km jobs, and no XML anywhere can stop it.

The mod makes the window hard. `max="800"` means no offer past 800 m, full stop.

The cost, stated plainly: on a sparse map a tight window returns **shorter pages**, not further quests. A page that
comes back with three offers instead of twelve is telling you the window is tighter than that trader's surroundings
support. That is the intended behaviour — offering the wrong distance would defeat the setting — but it is a real
trade and an admin should expect to widen the window or lower `per_tier` after watching one.

Recipes worth trying:

| Goal                        | Settings                                                      |
|-----------------------------|---------------------------------------------------------------|
| Keep the work walkable      | `max="700"` and `band_weights="1,0,0"`                        |
| Push players out of town    | `min="600"` and `band_weights="0,1,1"`                        |
| Vanilla, but no 2 km jobs   | `max="1500"` and everything else default                      |
| Tighter bands on a dense map| `near="300" mid="900"` with `band_weights="4,2,1"`            |

## Quest-type mix

Vanilla's mix is not a design, it is a **row count**. Selection is a uniform draw over whatever rows the trader's
`quest_list` declares for that tier, so tier 3 offers clear-type work about two-thirds of the time purely because
`tier3_clear`, `tier3_clear_infested` and `tier3_fetch_clear` are three of its six rows.

The mod replaces the row count with a weight, and adds a per-page cap so no single kind can take over a page even when
the dice like it.

```xml
<types default_weight="1">
  <type match="*_clear"           weight="3" max="4" />
  <type match="*_clear_infested"  weight="2" max="2" />
  <type match="*_fetch"           weight="3" max="4" />
  <type match="*_restore_power"   weight="2" max="2" />
  <type match="*_buried_supplies" weight="1" max="2" />
</types>
```

`match` is a glob over the quest id, not a fixed list, so quests from other mods are covered as long as their ids
follow a nameable pattern. Two rows matching the same id resolve by longest pattern, so row order never matters.

`weight="0"` retires a quest type without editing vanilla `quests.xml` — useful for buried supplies on a server where
that quest is unpopular.

## What was ruled out, and why

**Rewriting the offer list at `QuestEventManager.GetQuestList`.** That is where the mod's old stub patch sat. It is a
cache read: the list has already been built by the time it runs. The real seam is
`EntityTrader.PopulateActiveQuests`.

**One flat page holding every tier.** The game supports a tier-less `<quest_entry>`, so this looked easy. Accepting a
quest sends (tier, index-within-tier) to the server, so a flat index removes the wrong quest from the offer list. Not
worth the bug.

**64 quests on one screen.** The XUi canvas is 1080 tall and a response row is 40 px. Twenty-four rows is what fits at
a readable size, and three go to navigation. Reaching 64 means either a multi-column grid or sub-pages per tier;
sub-pages would put a permanent dead "more jobs" row on every trader, because no dialog requirement can test how many
quests a tier holds. Both routes are documented in `Docs/configuration.md`; neither is shipped.

## What this cost

The mod now owns a copy of a game algorithm. `EntityTrader.PopulateActiveQuests` and
`DynamicPrefabDecorator.GetRandomPOINearTrader` are both replaced outright, so a game update that changes how offers
are built will not show up as a compile error.

Two things limit the damage. The replacement keeps vanilla's observable contract — same tier loop, same used-POI
bookkeeping, same special-quest pass, same final sort — so divergence stays visible as a diff against
`vanilla-offer-generation.md`. And the repo's smoke suite resolves both patch targets against every declared game
version, so a renamed or re-signatured method fails the build rather than failing silently at runtime.

## What is verified, and what is not

Verified off-game:

* `dotnet build BountifulQuests/BountifulQuests.csproj -c Debug` — clean.
* `dotnet test StrongMods.sln -c Debug` — 269 passed. That includes resolving both new Harmony targets against
  `V3.1.0-b14` and `V3.0.1-b4`, and **replaying both XML patches against both vanilla trees with no warnings**. The
  game logs "did not apply" for a patch element that matches nothing and the suite fails on it, so every one of the 84
  dialog inserts and 4 XUi attribute writes really matched.
* 33 disposable harness checks over the selection logic — globbing, longest-pattern-wins, clamping, band schedule,
  the 3:1 weight ratio over 40 000 draws, per-type caps, exhaustion, repeat avoidance, single-quest removal. The
  harness was not retained; #131 tracks graduation into `Tests/ModLogic`.

**Not** verified, and needing an in-game pass (#49):

* That a trader renders more than 10 rows, and that the widened window sits sensibly on screen at 1006 px tall. The
  anchor arithmetic behind `pos="-825,503"` was read out of `XUi.updateAnchors`, not observed.
* That clicking row 9 accepts quest 9 — the removal path is per-tier and should be fine, but it is untested.
* That the settings file is written to the save folder on first run, and read from there on the next.
* That a distance window finds anything on a real map, and how short the pages get when it does not.

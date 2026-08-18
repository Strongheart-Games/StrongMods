# Configuring BountifulQuests

The mod reads one settings file:

```
<save game folder>/StrongMods/BountifulQuests.xml
```

It is written for you with the shipped defaults the first time the server starts, so the fastest way to see the format
is to start once and open the file. `Docs/BountifulQuests.default.xml` inside the mod folder is the same content, kept
as the reference copy.

The file lives beside the save, not inside the mod folder, for one reason: deploying the mod **mirrors** its folder, so
anything you edited under `Mods/BountifulQuests/` would be replaced the next time the mod is updated.

Settings are read once, at game awake. Changing them means restarting the server.

**Every default reproduces vanilla.** A missing file, an unreadable file, or a file with nothing in it all leave the
game behaving exactly as it does without the mod. Out-of-range values are pulled back into range and the correction is
written to the log, naming the setting and the value used.

## `<offers>`

| Attribute  | Range | Default | What it does                                                                        |
|------------|-------|---------|---------------------------------------------------------------------------------------|
| `per_tier` | 4..64 | 7       | Quests offered on one tier page. Vanilla is 7 and cannot be changed.                   |
| `min_tier` | 1..6  | 1       | Lowest tier page offered.                                                              |
| `max_tier` | 0..6  | 0       | Highest tier page. `0` means the player's own faction tier, which is what vanilla does. |
| `sort`     | —     | `distance` | `distance` (nearest first, vanilla), `tier`, `type`, or `none`.                     |

A trader offers `per_tier` quests on **each** tier page, and the player pages between tiers with the existing "previous
/ next tier" links. At the vanilla default that is 7 per page and up to 42 across a tier-6 player's six pages.

`min_tier` is the knob for a long-running server: raising it to 3 stops traders offering tier-1 and tier-2 work to
players who left that behind, without touching the tier system itself.

### Raising the display budget

`per_tier` accepts up to 64, but the shipped XML shows at most **21 per page**. The mod says so in the log if you set
more, and the extra quests are still generated and still real — no player can reach them in dialog.

Where 21 comes from: `Config/XUi_InGame/windows.xml` grows the trader dialog response list from 10 rows to 24, and
three of those rows go to "never mind" plus the previous-tier and next-tier links.

Two ways to go higher, neither of them shipped:

* **More rows per screen.** The XUi canvas is 1080 tall and the list already uses nearly all of it at 40 px per row.
  Shrinking rows buys a few more and costs readability. A multi-column grid (`cols="2"` on that grid, with a wider
  window) roughly doubles the budget and is the more promising direction.
* **Sub-pages per tier.** `Config/dialogs.xml` can define a second and third page per tier with its own listindex
  range, reached by a "more jobs" link. This works and needs no code, but the link cannot be hidden when there is
  nothing behind it — the game has no dialog requirement that tests how many quests a tier holds — so every trader
  would carry a dead row at the default page size.

## `<distance>`

| Attribute      | Range      | Default | What it does                                                                  |
|----------------|------------|---------|---------------------------------------------------------------------------------|
| `min`          | 0..100000  | 0       | Metres from the trader. A nearer POI is refused. `0` disables the floor.        |
| `max`          | 0..100000  | 0       | Metres from the trader. A further POI is refused. `0` disables the cap.         |
| `near`         | > 0        | 500     | Upper edge of the near band. Vanilla hardcodes 500.                            |
| `mid`          | > `near`   | 1500    | Upper edge of the mid band. Vanilla hardcodes 1500.                            |
| `band_weights` | 3 integers | `3,1,3` | How many of a page's offers prefer near, then mid, then far.                    |

**Vanilla has no distance cap.** It sorts POIs into three buckets, prefers one, and quietly falls back to the others
when the preferred bucket has nothing left. That is why a trader in a thin region hands out 2 km jobs nobody asked for.

`min` and `max` are hard. Set `max="800"` and no quest past 800 m is ever offered.

The trade you are buying: on a sparse map a tight window means **shorter pages**, not further quests. If a page comes
back with three quests instead of twelve, the window is too tight for what is around that trader.

`band_weights` describes one cycle and repeats for as long as the page needs. `3,1,3` gives vanilla's pattern exactly.
`1,0,0` makes every offer prefer the near band; combined with a `max`, it is the "keep the work local" setting.

## `<types>`

```xml
<types default_weight="1">
  <type match="*_clear"           weight="3" max="4" />
  <type match="*_fetch"           weight="2" max="3" />
  <type match="*_buried_supplies" weight="0" />
</types>
```

| Attribute        | Default | What it does                                                                            |
|------------------|---------|-------------------------------------------------------------------------------------------|
| `default_weight` | 1       | Weight for a quest id no row matches.                                                     |
| `match`          | —       | Case-insensitive glob over the quest id. `*` and `?`, anchored at both ends.               |
| `weight`         | 1       | Relative chance of being drawn. `0` removes that type from offers entirely.                |
| `max`            | 0       | Most of that type on one tier page. `0` means uncapped.                                    |

**Vanilla has no weighting.** It draws uniformly from the rows a trader's `quest_list` declares for that tier, so the
mix you get is an accident of how many rows each kind of quest happens to have in `quests.xml`.

Anchoring is what makes the table read the way it looks: `*_clear` matches `tier3_clear` and **not**
`tier3_clear_infested`. When two rows both match an id — `tier2_fetch_clear` matches `*_clear` and `*_fetch_clear` —
the **longer** pattern wins, so where you put a row in the file never changes the result.

Because `match` is a glob over the quest id and not a fixed list, quests added by other mods are covered too, as long
as their ids follow a pattern you can name.

The vanilla ids worth knowing:

| Quest kind                    | Ids                                                        |
|-------------------------------|------------------------------------------------------------|
| Clear                         | `tier1_clear` .. `tier5_clear`                             |
| Clear, infested               | `tier2_clear_infested` .. `tier6_clear_infested`           |
| Fetch                         | `tier1_fetch`, `tier2_fetch`, `tier3_fetch`                |
| Fetch and clear               | `tier2_fetch_clear` .. `tier5_fetch_clear`                 |
| Restore power                 | `tier2_restore_power` .. `tier4_restore_power`             |
| Buried supplies               | `tier1_buried_supplies` .. `tier3_buried_supplies`, `intro_buried_supplies` |

Trader-to-trader work (`tier2_nexttrader`, `tier3_nexttrader`) carries a unique key, shows on its own dialog page, and
is **not** part of the weighted mix — weights and caps do not apply to it, exactly as in vanilla.

## `<repeats>`

| Attribute       | Default | What it does                                                        |
|-----------------|---------|-----------------------------------------------------------------------|
| `avoid_offered` | `false` | Prefer not to put the same quest id on a page twice.                  |

Shipped off so the defaults stay vanilla. It is the single biggest variety win once `per_tier` is larger than the
number of quest kinds a tier has: with 12 offers drawn from six kinds, vanilla's uniform draw will hand you the same
clear quest four times.

It stays a preference, not a rule. When every remaining candidate has already appeared, the page fills with repeats
rather than ending short.

This is separate from the POI memory the game already keeps: a POI you completed for a trader is not re-offered by that
trader until the tier is exhausted or seven in-game days pass (tiers 4-6 only). The mod does not change that.

## Reading the log

At startup the mod writes one line naming everything it resolved:

```
[BountifulQuests] 12 offers per tier page, tiers 1-current, distance 0-900m, sort distance. Settings: .../BountifulQuests.xml
```

If a value was corrected, a `WRN` line names the setting, what you wrote, and what is being used instead. If
`per_tier` is beyond what the dialog can show, a `WRN` line says that too.

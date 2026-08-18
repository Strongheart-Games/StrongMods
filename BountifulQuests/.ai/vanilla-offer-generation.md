# How vanilla builds a trader's quest offer list

Source read: `Assembly-CSharp.dll`, game tree `V3.1.0-b14` (`packages/7dtd.assemblies.game/3.1.0.14`), decompiled with
`ilspycmd`. Line numbers below are decompiler line numbers, not shipped-source line numbers.

This is a research note for #32 and #33. It records **what the game does today**, so a later reader can tell a design
decision from a game fact.

## The call path

| Step | Where                                                         | What happens                                                                                 |
|------|---------------------------------------------------------------|----------------------------------------------------------------------------------------------|
| 1    | `NetPackageNPCQuestList.ProcessPackage` (server branch)       | Client asks for the list. Server calls `GetQuestList`; on a miss, `PopulateActiveQuests`.     |
| 2    | `EntityTrader.PopulateActiveQuests`                            | **Generates** the list. Server only.                                                         |
| 3    | `Quest.SetupPosition` → `ObjectiveRandomPOIGoto.SetupPosition` | Picks the POI for one quest.                                                                 |
| 4    | `DynamicPrefabDecorator.GetRandomPOINearTrader`                | Picks a POI out of a distance band.                                                          |
| 5    | `QuestEventManager.SetupQuestList`                             | Caches the list per (trader, player) and sends it to the client.                             |
| 6    | `NetPackageNPCQuestList.SendQuestPacketsToPlayer`              | Ships `QuestPacketEntry[]`; the count is an `Int32`, so there is **no wire cap** on size.    |
| 7    | `EntityTrader.SetActiveQuests` (client)                        | Rebuilds `activeQuests` from the packet.                                                     |
| 8    | `DialogResponseQuest` (client)                                 | One `<quest_entry>` in `dialogs.xml` resolves to one entry of `activeQuests`.                |

`QuestEventManager.GetQuestList` — the seam the shipped stub patch already sits on — is **only a cache read**. It
returns the stored list, clears it on trader-reset or after 24000 world ticks, and generates nothing. Growing or
re-ordering the offer list at that seam means rewriting a list that has already been built.

## The generator: `EntityTrader.PopulateActiveQuests`

```
for tier i = 1 .. currentTier                     // currentTier = QuestJournal.GetCurrentFactionTier(faction), 1..6
    candidates = questDictionary[i] filtered by StartStage/EndStage vs faction points, and QuestEntry.CheckRequirement
    num = 0
    for attempt k = 0 .. 99
        preferredDistanceIndex = distanceIndices[num]
        pick a uniformly random candidate
        skip if that QuestClass is already at its MaxQuestCount   // vanilla ships max_quest_count=0 = unlimited
        skip if rand.RandomFloat >= QuestEntry.Prob               // vanilla ships no prob attribute, so Prob=1
        skip if the quest carries the clear tag and EnemySpawnMode is off
        if quest.SetupPosition(...) succeeded                     // a POI was found and reserved
            num++
        break when num >= distanceIndices.Length                  // == 7
then append every eligible special quest (unique_key set), e.g. tier2_nexttrader
then sort the whole list by distance from the player, nearest first
```

Facts worth naming:

* `distanceIndices` is `readonly int[7] { 0, 0, 0, 1, 2, 2, 2 }` (`EntityTrader.cs:138`). It does two jobs at once: its
  **length is the per-tier offer cap (7)**, and its **values are the distance-band schedule** — offers 1-3 prefer the
  near band, offer 4 the mid band, offers 5-7 the far band.
* The tier loop runs `1..currentTier`, and `num` resets per tier. A tier-6 player is therefore offered up to
  **7 quests per tier across 6 tiers = 42**, not 7 in total. The player sees one tier at a time; the dialog pages
  between them.
* `usedPOILocations` grows as the loop runs (`ObjectiveRandomPOIGoto.cs:128`), so one POI never appears twice in the
  same offer list.
* There is no weighting anywhere. Selection is `rand.RandomRange(candidates.Count)` — uniform over the quest entries
  the trader's `quest_list` declares for that tier. A tier whose list names two clear variants and one fetch is biased
  toward clear quests purely by how many rows the XML has.

## Distance

`QuestEventManager.SetupTraderPrefabList` sorts every POI in the world into exactly three buckets by distance from the
**trader area**, with hardcoded edges:

| Band index | Distance from trader area |
|------------|---------------------------|
| 0          | up to 500 m               |
| 1          | 500 m to 1500 m           |
| 2          | over 1500 m               |

`GetRandomPOINearTrader` starts at `trader.PreferredDistanceIndex` and, on failure, walks the other two bands
`(index + 1) % 3`. So a band is not a cap — it is a preference that silently falls back to any other band. There is
**no distance cap in vanilla at all**, and no way to express one in XML: the band edges are C# constants.

## Repeat avoidance

Vanilla has some, keyed on the trader area, stored per player in `QuestJournal.TraderData` (`QuestTraderData`):

* `CompletedPOIByTier` records POIs the player already did for that trader, per tier.
* `PopulateActiveQuests` feeds those into `usedPOILocations`, so they are not re-offered.
* `QuestTraderData.CheckReset` wipes tiers 4-6 after **7 in-game days**. `ClearTier` also wipes a tier when
  `EntityTrader.UpdateLocations` finds the player has used every POI that tier has.

What it does **not** do: remember quest *types*, remember what is currently on offer, or apply any memory to tiers 1-3
except the exhaustion rule.

## The display path, and where the real limits are

Three separate caps stack. Only one of them is in C#.

| Cap                            | Vanilla value | Where it lives                                                                | Server-side changeable? |
|--------------------------------|---------------|---------------------------------------------------------------------------------|-------------------------|
| Quests generated per tier      | 7             | `distanceIndices.Length`, C#                                                    | Harmony patch only      |
| `<quest_entry>` rows per tier  | 7             | `dialogs.xml`, statements `currentjobs1` to `currentjobs6`                      | **Yes** — XML patch     |
| Dialog response rows on screen | 10            | `XUi_InGame/windows.xml`, `windowResponses` → `<grid name="items" rows="10">`   | **Yes** — XML patch     |

`XUiC_DialogResponseList.Init()` counts the `XUiC_DialogResponseEntry` children the grid XML produced and never renders
more than that. `XUiV_Grid.RepeatCount` is `Columns * Rows`, so `rows="N"` on that grid *is* the row budget. Surplus
rows bind `showresponse=false` and hide themselves, so over-provisioning is safe.

The XUi virtual canvas is 1080 tall (the tallest vanilla window is `height="1080"`). At the vanilla `cell_height="40"`
that is about **24 usable rows** per page after the 46 px header, and the page must also fit `nevermind` plus the
deduplicated `prev` and `next` responses. Reaching 64 rows on one page is not possible without shrinking cells below a
readable size, so 64 quests have to arrive as pages, not as one list.

## Server-side-only: confirmed

`WorldStaticData.cs:114` and the surrounding table declare `_sendToClients: true` for `dialogs`, `quests`, `npc`,
`traders`, **and** `XUi_InGame/windows`, `XUi_InGame/xui`, `XUi_InGame/styles`, `XUi_InGame/templates`. The server
compresses its patched copy and pushes it to each joining client (`WorldStaticData.SendXmlsToClient` →
`NetPackageConfigFile`).

So both XML halves of this mod — the dialog rows and the XUi grid size — are server-side only. **The current
`BountifulQuests/README.md` is wrong** where it says the mod "must be installed on both the server and every client".
Correct that when this work lands.

`XUi_Common/controls` is *not* in the sync table, so new controller types cannot be introduced server-side. Nothing
here needs one.

## The flat-list trap

`DialogResponseQuest` supports a tier-less `<quest_entry listindex="N"/>` (no `tier` attribute): it indexes
`activeQuests` directly. That looks like an easy way to put every tier on one page.

It breaks accepting a quest. `XUiC_QuestOfferWindow` sends `NetPackageNPCQuestList.Setup(giver, player,
Quest.QuestClass.DifficultyTier, (byte)listIndex)`, and the server removes the `listIndex`-th quest **counting only
quests of that tier**. A flat `listIndex` against a per-tier count removes the wrong quest. Per-tier pages avoid this
entirely, which is why the design keeps them.

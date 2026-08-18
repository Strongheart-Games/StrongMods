using System;
using System.Collections.Generic;
using UnityEngine;

namespace BountifulQuests {
  /// <summary>
  /// Builds a trader's offer list. Replaces <c>EntityTrader.PopulateActiveQuests</c> outright.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is a rewrite of a game algorithm, not a tweak to it, so it carries a maintenance cost: a game update that
  /// changes how offers are built will not show up here as a compile error. It keeps vanilla's observable contract on
  /// purpose — same tier loop, same used-POI bookkeeping, same special-quest pass, same final sort — so what does
  /// change stays visible in a diff against <c>.ai/vanilla-offer-generation.md</c>.
  /// </para>
  /// <para>
  /// Runs on the server only. The generated list reaches clients through <c>NetPackageNPCQuestList</c>.
  /// </para>
  /// </remarks>
  public static class OfferBuilder {
    public static List<Quest> Build(EntityTrader trader, EntityPlayer player, int currentTier, int questFactionPoints) {
      if (trader.questDictionary.Count == 0) {
        trader.PopulateQuestList();
        if (trader.questDictionary.Count == 0) {
          return null;
        }
      }

      var config = ModApi.Config;
      var spawnsEnabled = GameStats.GetBool(EnumGameStats.EnemySpawnMode);
      var offers = new List<Quest>();
      trader.uniqueKeysUsed.Clear();
      trader.usedPOILocations.Clear();

      var traderPos = trader.traderArea == null
        ? new Vector2(trader.position.x, trader.position.z)
        : new Vector2(trader.traderArea.Position.x, trader.traderArea.Position.z);

      if (currentTier == -1) {
        currentTier = player.QuestJournal.GetCurrentFactionTier(trader.NPCInfo.QuestFaction);
      }

      if (questFactionPoints == -1) {
        questFactionPoints = player.QuestJournal.GlobalFactionPoints;
      }

      var traderData = player.QuestJournal.GetTraderData(traderPos);
      traderData?.CheckReset(player);

      var lowestTier = Math.Max(1, config.MinTier);
      var highestTier = config.MaxTier == 0 ? currentTier : Math.Min(config.MaxTier, currentTier);
      for (var tier = lowestTier; tier <= highestTier; tier++) {
        if (!trader.questDictionary.TryGetValue(tier, out var tierEntries)) {
          continue;
        }

        CarryForwardUsedPOIs(trader, player, traderData, traderPos, tier);
        FillTierPage(trader, player, config, tierEntries, questFactionPoints, spawnsEnabled, offers);
      }

      AddSpecialQuests(trader, player, currentTier, questFactionPoints, offers);
      Order(offers, player, config.Sort);
      return offers;
    }

    /// <summary>
    /// Vanilla's per-tier POI memory: POIs the player already completed for this trader are off the table, unless the
    /// tier has been exhausted, in which case the whole tier's memory is cleared and the client is told.
    /// </summary>
    private static void CarryForwardUsedPOIs(EntityTrader trader, EntityPlayer player, QuestTraderData traderData,
                                             Vector2 traderPos, int tier) {
      var usedPOIs = player.QuestJournal.GetUsedPOIs(traderPos, tier);
      if (usedPOIs == null) {
        return;
      }

      if (!trader.UpdateLocations(tier, usedPOIs)) {
        trader.usedPOILocations.AddRange(usedPOIs);
        return;
      }

      if (traderData == null) {
        return;
      }

      traderData.ClearTier(tier);
      if (!(player is EntityPlayerLocal)) {
        SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
          NetPackageManager.GetPackage<NetPackageNPCQuestList>().SetupClear(player.entityId, traderPos, tier),
          _onlyClientsAttachedToAnEntity: false, player.entityId);
      }
    }

    private static void FillTierPage(EntityTrader trader, EntityPlayer player, OfferConfig config,
                                     List<QuestEntry> tierEntries, int questFactionPoints, bool spawnsEnabled,
                                     List<Quest> offers) {
      var candidates = new List<QuestEntry>();
      var questIds = new List<string>();
      foreach (var entry in tierEntries) {
        var beforeStart = entry.StartStage != -1 && entry.StartStage > questFactionPoints;
        var afterEnd = entry.EndStage != -1 && entry.EndStage < questFactionPoints;
        if (beforeStart || afterEnd || !entry.CheckRequirement(player)) {
          continue;
        }

        candidates.Add(entry);
        questIds.Add(entry.QuestID);
      }

      if (candidates.Count == 0) {
        return;
      }

      var draw = new OfferDraw(config, questIds);
      var attemptBudget = Math.Max(100, config.OffersPerTier * 15);
      for (var attempt = 0; attempt < attemptBudget && draw.Accepted < config.OffersPerTier; attempt++) {
        var index = draw.Draw(trader.rand.RandomDouble);
        if (index < 0) {
          break;
        }

        var entry = candidates[index];
        if (!(trader.rand.RandomFloat < entry.Prob)) {
          draw.Reject(index);
          continue;
        }

        // Set before SetupPosition: the POI picker reads it back off the trader, exactly as vanilla does.
        trader.preferredDistanceIndex = config.BandForSlot(draw.Accepted);

        var quest = entry.QuestClass.CreateQuest();
        quest.QuestGiverID = trader.entityId;
        quest.QuestFaction = trader.NPCInfo.QuestFaction;
        quest.SetPositionData(Quest.PositionDataTypes.QuestGiver, trader.position);
        quest.SetPositionData(Quest.PositionDataTypes.TraderPosition,
          trader.traderArea != null ? (Vector3)trader.traderArea.Position : trader.position);
        quest.SetupTags();

        if (!spawnsEnabled && quest.QuestTags.Test_AnySet(QuestEventManager.clearTag)) {
          draw.Reject(index);
          continue;
        }

        if (!quest.SetupPosition(trader, player, trader.usedPOILocations, player.entityId)) {
          draw.Reject(index);
          continue;
        }

        if (entry.QuestClass.SingleQuest) {
          draw.Remove(index);
        }

        draw.Accept(index);
        offers.Add(quest);
      }
    }

    /// <summary>
    /// Vanilla's special-quest pass, unchanged. These carry a unique_key (trader-to-trader work, the intro quest) and
    /// are not part of the tunable mix — they show on their own dialog page and are offered at most once.
    /// </summary>
    private static void AddSpecialQuests(EntityTrader trader, EntityPlayer player, int currentTier,
                                         int questFactionPoints, List<Quest> offers) {
      if (trader.specialQuestList == null) {
        return;
      }

      foreach (var entry in trader.specialQuestList) {
        var beforeStart = entry.StartStage != -1 && entry.StartStage > questFactionPoints;
        var afterEnd = entry.EndStage != -1 && entry.EndStage < questFactionPoints;
        if (beforeStart || afterEnd || !entry.CheckRequirement(player)) {
          continue;
        }

        var questClass = entry.QuestClass;
        if (questClass.UniqueKey != "" && trader.uniqueKeysUsed.Contains(questClass.UniqueKey)) {
          continue;
        }

        if (questClass.DifficultyTier - 1 > currentTier) {
          continue;
        }

        if (player.QuestJournal.FindCompletedQuest(questClass.ID,
              questClass.Repeatable ? trader.NPCInfo.QuestFaction : -1)) {
          continue;
        }

        for (var attempt = 0; attempt < 100; attempt++) {
          var quest = questClass.CreateQuest();
          quest.QuestGiverID = trader.entityId;
          quest.QuestFaction = trader.NPCInfo.QuestFaction;
          quest.SetPositionData(Quest.PositionDataTypes.QuestGiver, trader.position);
          quest.SetPositionData(Quest.PositionDataTypes.TraderPosition,
            trader.traderArea != null ? (Vector3)trader.traderArea.Position : trader.position);
          quest.SetupTags();
          if (quest.NeedsNPCSetPosition &&
              !quest.SetupPosition(trader, player, trader.usedPOILocations, player.entityId)) {
            continue;
          }

          offers.Add(quest);
          if (questClass.UniqueKey != "") {
            trader.uniqueKeysUsed.Add(questClass.UniqueKey);
          }

          break;
        }
      }
    }

    private static void Order(List<Quest> offers, EntityPlayer player, OfferSort sort) {
      switch (sort) {
        case OfferSort.Distance:
          offers.Sort((a, b) => (a.Position - player.position).sqrMagnitude
            .CompareTo((b.Position - player.position).sqrMagnitude));
          break;
        case OfferSort.Tier:
          offers.Sort((a, b) => a.QuestClass.DifficultyTier.CompareTo(b.QuestClass.DifficultyTier));
          break;
        case OfferSort.Type:
          offers.Sort((a, b) => string.CompareOrdinal(a.QuestClass.ID, b.QuestClass.ID));
          break;
        case OfferSort.None:
          break;
      }
    }
  }
}

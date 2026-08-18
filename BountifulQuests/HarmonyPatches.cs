using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BountifulQuests {
  /// <summary>
  /// Replaces the offer list a trader builds for a player.
  /// </summary>
  /// <remarks>
  /// This is the seam, not <c>QuestEventManager.GetQuestList</c> — that one only reads a cache that was already
  /// filled here. Server-only: on a client <c>PopulateActiveQuests</c> is never reached, because the list arrives in a
  /// <c>NetPackageNPCQuestList</c>.
  /// </remarks>
  [HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.PopulateActiveQuests))]
  public static class EntityTrader_PopulateActiveQuests_Patch {
    private static bool Prefix(EntityTrader __instance, EntityPlayer player, int currentTier, int questFactionPoints,
                               ref List<Quest> __result) {
      __result = OfferBuilder.Build(__instance, player, currentTier, questFactionPoints);
      return false;
    }
  }

  /// <summary>
  /// Replaces POI selection so the configured distance window is enforced instead of merely preferred.
  /// </summary>
  [HarmonyPatch(typeof(DynamicPrefabDecorator), nameof(DynamicPrefabDecorator.GetRandomPOINearTrader))]
  public static class DynamicPrefabDecorator_GetRandomPOINearTrader_Patch {
    private static bool Prefix(EntityTrader trader, FastTags<TagGroup.Global> questTag, byte difficulty,
                               List<Vector2> usedPOILocations, int entityIDforQuests,
                               BiomeFilterTypes biomeFilterType, string biomeFilter,
                               ref PrefabInstance __result) {
      __result = PoiPicker.Pick(trader, questTag, difficulty, usedPOILocations, entityIDforQuests, biomeFilterType,
        biomeFilter);
      return false;
    }
  }
}

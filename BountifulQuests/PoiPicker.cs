using System.Collections.Generic;
using UnityEngine;

namespace BountifulQuests {
  /// <summary>
  /// Chooses the POI a quest sends the player to. Replaces
  /// <c>DynamicPrefabDecorator.GetRandomPOINearTrader</c>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Vanilla cannot cap quest distance. It sorts POIs into three buckets with hardcoded 500 m and 1500 m edges, starts
  /// at the trader's preferred bucket, and on failure walks the other two — so a preference for near POIs silently
  /// becomes a 2 km trip when the near bucket is thin. This picker keeps the preference-then-walk shape but treats
  /// <c>distance/@min</c> and <c>distance/@max</c> as hard filters and re-bands by the configured edges.
  /// </para>
  /// <para>
  /// The trade an admin is buying: a tight window on a sparse map returns no POI, so the quest is simply not offered.
  /// Pages get shorter rather than quietly wrong.
  /// </para>
  /// </remarks>
  public static class PoiPicker {
    public static PrefabInstance Pick(EntityTrader trader, FastTags<TagGroup.Global> questTag, byte difficulty,
                                      List<Vector2> usedPOILocations, int entityIDforQuests,
                                      BiomeFilterTypes biomeFilterType, string biomeFilter) {
      var config = ModApi.Config;
      var questEvents = QuestEventManager.Current;
      var gameRandom = GameManager.Instance.World.GetGameRandom();
      if (trader.traderArea == null) {
        return null;
      }

      var traderPos = trader.traderArea.Position.ToVector3();
      var preferred = Mathf.Clamp(trader.PreferredDistanceIndex, 0, 2);

      // Three passes over the configured bands, starting at the preferred one. Each vanilla bucket is re-read every
      // pass because GetPrefabsForTrader shuffles it, which is where the randomness comes from.
      for (var step = 0; step < 3; step++) {
        var wantedBand = (preferred + step) % 3;
        for (var bucket = 0; bucket < 3; bucket++) {
          var prefabs = questEvents.GetPrefabsForTrader(trader.traderArea, difficulty, bucket, gameRandom);
          if (prefabs == null) {
            continue;
          }

          foreach (var prefab in prefabs) {
            var distance = Vector3.Distance(traderPos, prefab.boundingBoxPosition);
            if (!WithinWindow(config, distance) || BandOf(config, distance) != wantedBand) {
              continue;
            }

            if (DynamicPrefabDecorator.ValidPrefabForQuest(trader, prefab, questTag, usedPOILocations,
                  entityIDforQuests, biomeFilterType, biomeFilter)) {
              return prefab;
            }
          }
        }
      }

      return null;
    }

    private static bool WithinWindow(OfferConfig config, float distance) {
      if (config.MinDistance > 0f && distance < config.MinDistance) {
        return false;
      }

      return !(config.MaxDistance > 0f) || !(distance > config.MaxDistance);
    }

    private static int BandOf(OfferConfig config, float distance) {
      if (distance <= config.NearBandEdge) {
        return 0;
      }

      return distance <= config.MidBandEdge ? 1 : 2;
    }
  }
}

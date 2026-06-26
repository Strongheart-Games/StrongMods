using System;

namespace DynamicFeralSense {
  public class DynamicFeralSense {
    private static DateTime s_lastLogged = DateTime.MinValue;

    public static float CalcVisionMultiplier(EntityAlive entity) {
      return CalcSenseMultiplierForBiome(entity.biomeStandingOn);
    }

    public static float CalcNoiseMultiplier(EntityPlayer entity) {
      return CalcSenseMultiplierForBiome(entity.biomeStandingOn);
    }

    public static float CalcSenseMultiplierForBiome(BiomeDefinition biome) {
      var multiplier = EAIManager.CalcSenseScale();
      if (biome is null || multiplier == 0f) {
        return multiplier;
      }

      switch (biome?.m_BiomeType) {
        case BiomeDefinition.BiomeType.PineForest:
          return 0.0f * multiplier;
        case BiomeDefinition.BiomeType.burnt_forest:
          return 0.25f * multiplier;
        case BiomeDefinition.BiomeType.Desert:
          return 0.5f * multiplier;
        case BiomeDefinition.BiomeType.Snow:
          return 0.75f * multiplier;
        case BiomeDefinition.BiomeType.Wasteland:
          return multiplier;
        default:
          DateTime now = DateTime.Now;
          if (now - s_lastLogged > TimeSpan.FromSeconds(5)) {
            s_lastLogged = now;
            Log.Error($"[DynamicFeralSense] Unknown biome {biome?.m_BiomeType}");
          }
          return 0.2f * multiplier;
      }
    }
  }
}

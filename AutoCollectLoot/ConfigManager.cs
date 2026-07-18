using HarmonyLib;

namespace AutoCollectLoot {
  public static class ConfigManager {
    private static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties?.GetClass("auto_collect_loot");
      if (properties is not null) {
        properties.ParseBool("enable", ref AutoCollectLoot.Enabled);
        properties.ParseBool("enable_outside_blood_moons", ref AutoCollectLoot.EnabledOutsideBloodMoon);
        properties.ParseFloat("killshot_bonus_lottery_chance", ref AutoCollectLoot.KillshotBonusLotteryChance);
      }
    }

    [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
    public class WorldEnvironment_OnXMLChanged_Patch {
      private static void Postfix() {
        OnXMLChanged();
      }
    }
  }
}

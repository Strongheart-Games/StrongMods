using System.Reflection;
using HarmonyLib;

namespace BloodRain {
  [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
  public class GameManagerUpdatePatch {
    private static void Prefix() {
      BloodRain.Update();
    }
  }

  [HarmonyPatch(typeof(GameUtils), nameof(GameUtils.IsBloodMoonTime), typeof((int, int)), typeof(int), typeof(int),
    typeof(int))]
  public class GameUtilsIsBloodMoonTimePatch {
    private static bool Prefix(ref bool __result) {
      __result = BloodRain.IsBloodRainTime();
      return false;
    }
  }

  [HarmonyPatch(typeof(World), nameof(World.IsWorldEvent))]
  public class WorldIsWorldEventPatch {
    private static bool Prefix(ref bool __result) {
      __result = BloodRain.IsBloodRainTime();
      return false;
    }
  }

  [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
  public class WorldEnvironmentOnXMLChangedPatch {
    private static void Postfix() {
      BloodRain.OnXMLChanged();
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

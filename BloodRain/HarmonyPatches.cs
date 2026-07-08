using System.Reflection;
using HarmonyLib;

namespace BloodRain {
  [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
  public class GameManager_Update_Patch {
    private static void Prefix() {
      BloodRain.Update();
    }
  }

  [HarmonyPatch(typeof(GameUtils), nameof(GameUtils.IsBloodMoonTime), typeof((int, int)), typeof(int), typeof(int),
    typeof(int))]
  public class GameUtils_IsBloodMoonTime_Patch {
    private static bool Prefix(ref bool __result) {
      __result = BloodRain.IsBloodRainTime();
      return false;
    }
  }

  [HarmonyPatch(typeof(World), nameof(World.IsWorldEvent))]
  public class World_IsWorldEvent_Patch {
    private static bool Prefix(ref bool __result) {
      __result = BloodRain.IsBloodRainTime();
      return false;
    }
  }

  [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
  public class WorldEnvironment_OnXMLChanged_Patch {
    private static void Postfix() {
      BloodRain.OnXMLChanged();
    }
  }

  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAddedToWorld))]
  public class EntityAlive_OnAddedToWorld_Patch {
    private static void Postfix(EntityAlive __instance) {
      BloodRainChallenge.OnAddedToWorld(__instance);
    }
  }

  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnEntityUnload))]
  public class EntityAlive_OnEntityUnload_Patch {
    private static void Postfix(EntityAlive __instance) {
      BloodRainChallenge.OnEntityUnload(__instance);
    }
  }

  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnEntityDeath))]
  public class EntityAlive_OnEntityDeath_Patch {
    private static void Postfix(EntityAlive __instance) {
      BloodRainChallenge.OnEntityDeath(__instance);
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.GameAwake.RegisterHandler(BloodRain.OnGameAwake);
      ModEvents.ChatMessage.RegisterHandler(BloodRainChatCommand.OnChatMessage);
    }
  }
}

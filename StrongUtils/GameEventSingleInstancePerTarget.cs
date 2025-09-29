using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace StrongUtils {

  public class GameEventActionSequenceAdditionalData {
    public bool SingleInstancePerTarget = false;
  }

  public static class GameEventActionSequenceExtension {
    private static readonly ConditionalWeakTable<GameEventActionSequence, GameEventActionSequenceAdditionalData> Data = new();

    public static GameEventActionSequenceAdditionalData GetAdditionalData(this GameEventActionSequence entity) {
      return Data.GetOrCreateValue(entity);
    }
  }

  [HarmonyPatch(typeof(GameEventActionSequence), nameof(GameEventActionSequence.ParseProperties))]
  public class GameEventActionSequence_ParseProperties_Patch {
    private static void Prefix(GameEventActionSequence __instance, DynamicProperties properties) {
      properties.ParseBool("single_instance_per_target", ref __instance.GetAdditionalData().SingleInstancePerTarget);
    }
  }

  [HarmonyPatch(typeof(GameEventActionSequence), nameof(GameEventActionSequence.Clone))]
  public class GameEventActionSequence_Clone_Patch {
    private static void Postfix(GameEventActionSequence __instance, ref GameEventActionSequence __result) {
      __result.GetAdditionalData().SingleInstancePerTarget = __instance.GetAdditionalData().SingleInstancePerTarget;
    }
  }
}

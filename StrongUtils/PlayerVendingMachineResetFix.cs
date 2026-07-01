using HarmonyLib;

namespace StrongUtils {
  [HarmonyPatch(typeof(TraderInfo), nameof(TraderInfo.ResetInterval), MethodType.Getter)]
  public class TraderInfo_get_ResetInterval_Patch {
    private static bool Prefix(TraderInfo __instance, ref int __result) {
      if (!__instance.PlayerOwned) {
        return true;
      }

      __result = __instance.resetInterval;
      return false;
    }
  }

  [HarmonyPatch(typeof(TraderInfo), nameof(TraderInfo.ResetIntervalInTicks), MethodType.Getter)]
  public class TraderInfo_get_ResetIntervalInTicks_Patch {
    private static bool Prefix(TraderInfo __instance, ref int __result) {
      if (!__instance.PlayerOwned) {
        return true;
      }

      __result = __instance.resetIntervalInTicks;
      return false;
    }
  }
}

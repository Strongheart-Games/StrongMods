using HarmonyLib;

namespace AuthZ.Patches {
  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnUpdateLive))]
  public class EntityAliveOnUpdateLivePatch {
    private static void Postfix(EntityAlive __instance) {
      LandClaims.HandleViolations(__instance);
    }
  }
}

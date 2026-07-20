using HarmonyLib;

namespace LootDiagnostics {
  public static class LootDiagnostics {
    [HarmonyPatch(typeof(EntityClass), nameof(EntityClass.LootDropPick))]
    public static class EntityClass_LootDropPick_Patch {
      public static void Postfix(EntityClass __instance, ref int __result) {
        var loot = EntityClass.GetEntityClass(__result);
        Log.Out($"[LootDiagnostics] {__instance.entityClassName} dropped loot {loot.entityClassName}");
      }
    }
  }
}

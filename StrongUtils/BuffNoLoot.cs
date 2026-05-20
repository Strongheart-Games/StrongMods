using HarmonyLib;

namespace StrongUtils {
  public class BuffNoLoot {
    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.dropItemOnDeath))]
    public class EntityAlive_dropItemOnDeath_Patch {
      private static bool Prefix(ref EntityAlive __instance) {
        return !__instance.Buffs.HasBuff("buff_no_loot");
      }
    }
  }
}

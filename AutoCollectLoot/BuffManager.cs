using HarmonyLib;

namespace AutoCollectLoot {
  public static class BuffManager {
    public const string BuffAutoCollectLoot = "buff_auto_collect_loot";
    public const string BuffAutoCollectLootTimer = "buff_auto_collect_loot_timer";

    public static void OnEntityAliveAddedToWorld(EntityAlive entity) {
      if (!AutoCollectLoot.Enabled) {
        return;
      }

      var isHordeZombie = entity is EntityEnemy { IsHordeZombie: true };
      if (!isHordeZombie && !AutoCollectLoot.IsEnabledNow()) {
        return;
      }

      entity.Buffs.AddBuff(BuffAutoCollectLoot);
      if (!isHordeZombie) {
        // TODO: Refresh this buff periodically during horde night
        entity.Buffs.AddBuff(BuffAutoCollectLootTimer);
      }
    }
  }

  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAddedToWorld))]
  public static class EntityAlive_OnAddedToWorld_Patch {
    public static void Postfix(EntityAlive __instance) {
      BuffManager.OnEntityAliveAddedToWorld(__instance);
    }
  }

  [HarmonyPatch(typeof(Entity), nameof(Entity.DropBagServer))]
  public static class Entity_DropBagServer_Patch {
    public static bool Prefix(Entity __instance) {
      if (__instance is EntityAlive entity && entity.Buffs.HasBuff(BuffManager.BuffAutoCollectLoot)) {
        return !AutoCollectLoot.TryCollect(entity);
      }

      return true;
    }
  }
}

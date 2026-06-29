using HarmonyLib;

namespace StrongLocks {
  [HarmonyPatch(typeof(World), nameof(World.SpawnEntityInWorld))]
  public class World_SpawnEntityInWorld_Patch {
    private static void Postfix(Entity _entity) {
      // Vehicles are the only player-owned entities that don't auto-lock
      if (_entity is not EntityVehicle vehicle) {
        return;
      }

      vehicle.SetLocked(true);
      vehicle.SendSyncData(EntityVehicle.cSyncInteractAndSecurity);
    }
  }
}

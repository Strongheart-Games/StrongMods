using HarmonyLib;

namespace AuthZ {
  public class PveEnforcer {
    private static bool IsAuthorizedDamage(int targetEntityId, ClientInfo client) {
      Entity targetEntity = GameManager.Instance.World.GetEntity(targetEntityId);
      if (targetEntity is null) {
        return false;
      }

      if (targetEntity is EntityPlayer) {
        return targetEntityId == client.entityId;
      }

      if (targetEntity is EntityVehicle vehicle) {
        var hasAttached = false;
        var maxAttached = vehicle.GetAttachMaxCount();
        for (var slot = 0; slot < maxAttached; slot++) {
          Entity e = vehicle.GetAttached(slot);
          if (e is null) {
            continue;
          }

          if (e.entityId == client.entityId) {
            return true;
          }

          hasAttached = true;
        }

        return !hasAttached && vehicle.GetOwner().Equals(client.PlatformId);
      }

      return true;
    }

    [HarmonyPatch(typeof(NetPackageDamageEntity), nameof(NetPackageDamageEntity.ProcessPackage))]
    public class NetPackageDamageEntity_ProcessPackage_Patch {
      private static bool Prefix(ref NetPackageDamageEntity __instance) {
        if (ConnectionManager.Instance.IsClient) {
          return true;
        }

        var mode = (EnumPlayerKillingMode)GamePrefs.GetInt(EnumGamePrefs.PlayerKillingMode);
        if (mode != EnumPlayerKillingMode.NoKilling) {
          // TODO: Enforce other modes
          return true;
        }

        if (IsAuthorizedDamage(__instance.entityId, __instance.Sender)) {
          return true;
        }

        Entity targetEntity = GameManager.Instance.World.GetEntity(__instance.entityId);
        var target = targetEntity?.name ?? __instance.entityId.ToString();
        var sender = __instance.Sender.playerName;
        Log.Warning($"[AuthZ] PvE violation: {sender} sent a NetPackageDamageEntity for {target}");
        // TODO: return false to reject the violation, and maybe ban the sender
        return true;
      }
    }
  }
}

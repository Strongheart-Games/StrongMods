using System;
using System.Reflection;
using HarmonyLib;
using Platform.LAN;

namespace DisableLAN {
  [HarmonyPatch(typeof(LANMasterServerAnnouncer), nameof(LANMasterServerAnnouncer.AdvertiseServer))]
  public class LANMasterServerAnnouncer_AdvertiseServer_Patch {
    private static bool Prefix(Action _onServerRegistered) {
      Log.Out("[DisableLAN] Skipping LAN listener creation");
      _onServerRegistered();
      return false;
    }
  }

  public class DisableLAN : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

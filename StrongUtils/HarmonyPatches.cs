using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace StrongUtils {

  [HarmonyPatch(typeof(ConsoleCmdShutdown), nameof(ConsoleCmdShutdown.Execute))]
  public class ConsoleCmdShutdown_Execute_Patch {
    private static bool Prefix(ConsoleCmdShutdown __instance, List<string> _params, CommandSenderInfo _senderInfo) {
      new ShutdownHandler(__instance, _params, _senderInfo).Execute();
      return false;
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveAndCleanupWorld))]
  public class GameManager_SaveAndCleanupWorld_Patch {
    private static void Postfix() {
      // TODO: Reset regions, drones, vehicles, and turrets, when asked
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

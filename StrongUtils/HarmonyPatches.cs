using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace StrongUtils {
  [HarmonyPatch(typeof(SdtdConsole), nameof(SdtdConsole.executeCommand))]
  public class SdtdConsole_executeCommand_Patch {
    private static void Postfix(ref List<string> __result, string _command, CommandSenderInfo _senderInfo) {
      if (_senderInfo.NetworkConnection is not TelnetConnection) {
        return;
      }

      if (__result.Count == 0 || !__result[__result.Count - 1].StartsWith("*** ERROR:")) {
        __result.Add($"Done executing command '{_command}'.");
      }
    }
  }

  [HarmonyPatch(typeof(ServerStateAuthorizer), nameof(ServerStateAuthorizer.Authorize))]
  public class ServerStateAuthorizer_Authorize_Patch {
    private static void Postfix(ref (EAuthorizerSyncResult, GameUtils.KickPlayerData?) __result) {
      if (DenyAll.IsEnabled()) {
        __result = (EAuthorizerSyncResult.SyncDeny,
          new GameUtils.KickPlayerData(DenyAll.GetReason(), _customReason: DenyAll.GetMessage()));
      }
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

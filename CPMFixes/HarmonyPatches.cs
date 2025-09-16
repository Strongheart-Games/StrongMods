using System.Collections.Generic;
using System.Reflection;
using CSMM_Patrons;
using CSMM_Patrons.CustomCommands;
using HarmonyLib;

namespace CPMFixes {
  [HarmonyPatch(typeof(ChatFilter), nameof(ChatFilter.Exec))]
  public class ChatFilterExecPatch {
    public static readonly List<string> CpmChatCommands = new List<string> {
      "ft", "ftw", "mv", "mvw", "tb", "rt", "get", "listwp", "setwp", "delwp", "ls", "bag", "day7", "hostiles",
      "bed", "loctrack", "bubble"
    };

    private static void Postfix(string _message, ref ModEvents.EModEventResult __result) {
      if (!IsCpmChatCommand(_message)) {
        __result = ModEvents.EModEventResult.Continue;
      }
    }

    private static bool IsCpmChatCommand(string message) {
      if (message is null || !message.StartsWith(CpmSettings.Instance.CPMPrefix)) {
        return false;
      }

      var command = message.Substring(CpmSettings.Instance.CPMPrefix.Length);

      var iSpace = command.IndexOf(' ');
      if (iSpace > 0) {
        command = command.Substring(0, iSpace);
      }

      return CpmChatCommands.Contains(command);
    }
  }

  [HarmonyPatch(typeof(ShutdownBA), nameof(ShutdownBA.Execute))]
  public class ShutdownBA_Execute_Patch {
    private static void Prefix(ConsoleCmdShutdown __instance, List<string> _params, CommandSenderInfo _senderInfo) {
      if (_params.ContainsCaseInsensitive("resetregions")) {
        ModEvents.GameShutdown.RegisterHandler(ResetRegions);
        _params.Remove("resetregions"); // CPM won't recognize the argument, so remove it
      } else if (_params.ContainsCaseInsensitive("stop")) {
        ModEvents.GameShutdown.UnregisterHandler(ResetRegions);
      }
    }

    private static void ResetRegions(ref ModEvents.SGameShutdownData _data) {
      Log.Out("[CPMFixes] Resetting regions");
      SdtdConsole.Instance.ExecuteSync("resetregions", null);
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      if (!ModManager.ModLoaded("1CSMM_Patrons")) {
        Log.Out("[CPMFixes] CPM not loaded, aborting patching process.");
        return;
      }

      var harmony = new Harmony(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

using System.Collections.Generic;
using System.Reflection;
using CSMM_Patrons;
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
      return CpmChatCommands.Contains(command);
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

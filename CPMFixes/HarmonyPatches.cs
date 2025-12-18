using System.Collections.Generic;
using System.Reflection;
using PrismaCore;
using PrismaCore.CustomCommands;
using HarmonyLib;

namespace CPMFixes {
  [HarmonyPatch(typeof(ChatFilter), nameof(ChatFilter.Exec))]
  public class ChatFilterExecPatch {
    public static readonly List<string> CpmChatCommands = new List<string> {
      "ft", "ftw", "mv", "mvw", "tb", "rt", "get", "listwp", "setwp", "delwp", "ls", "bag", "day7", "hostiles", "bed",
      "loctrack", "bubble"
    };

    private static void Postfix(string _message, ref ModEvents.EModEventResult __result) {
      var command = "";
      if (TryGetCommand(_message, ref command) && !CpmChatCommands.ContainsCaseInsensitive(command)) {
        __result = ModEvents.EModEventResult.Continue;
      }
    }

    private static bool TryGetCommand(string message, ref string command) {
      if (message is null || !message.StartsWith(PrismaCoreSettings.Instance.PrismaCorePrefix)) {
        return false;
      }

      command = message.Substring(PrismaCoreSettings.Instance.PrismaCorePrefix.Length);

      var iSpace = command.IndexOf(' ');
      if (iSpace > 0) {
        command = command.Substring(0, iSpace);
      }

      return true;
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      if (!ModManager.ModLoaded("PrismaCore")) {
        Log.Out("[CPMFixes] CPM not loaded, aborting patching process.");
        return;
      }

      var harmony = new Harmony(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

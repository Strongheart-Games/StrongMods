using System.Collections.Generic;
using HarmonyLib;
using PrismaCore;

namespace PrismaCoreFixes {
  // Use strings in the HarmonyPatch() annotation to avoid runtime errors if PrismaCore is not present
  [HarmonyPatch("PrismaCore.ChatFilter", "Exec")]
  public class ChatFilterExecPatch {
    private static readonly List<string> PrismaCoreChatCommands = new List<string> {
      "ft", "ftw", "mv", "mvw", "tb", "rt", "get", "listwp", "setwp", "delwp", "ls", "bag", "day7", "hostiles", "bed",
      "loctrack", "bubble"
    };

    private static void Postfix(string _message, ref ModEvents.EModEventResult __result) {
      var command = "";
      if (TryGetCommand(_message, ref command) && !PrismaCoreChatCommands.ContainsCaseInsensitive(command)) {
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
}

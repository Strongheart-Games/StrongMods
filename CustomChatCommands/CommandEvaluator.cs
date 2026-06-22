using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomChatCommands {
  public static class CommandEvaluator {
    public static bool CheckRequirements(ClientInfo cInfo, ChatCommand command) {
      var playerAdminLevel = GameManager.Instance.adminTools.Users.GetUserPermissionLevel(cInfo);
      if (playerAdminLevel > command.MinAdminLevel) {
        return false;
      }

      if (command.Requirements.Count <= 0) {
        return true;
      }

      if (!GameManager.Instance.World.Players.dict.TryGetValue(cInfo.entityId, out EntityPlayer localPlayer)) {
        return false;
      }

      foreach (CommandRequirement req in command.Requirements) {
        if (!req.Type.Equals("cvar", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        var currentCVarValue = localPlayer.GetCVar(req.Name);
        if (!Mathf.Approximately(currentCVarValue, req.Value)) {
          return false;
        }
      }

      return true;
    }

    public static void ExecuteActionList(List<CommandAction> actions, ClientInfo cInfo) {
      foreach (CommandAction action in actions) {
        var message = CommandProcessor.ReplaceVariables(action.CommandText, cInfo);

        switch (action.Type) {
          case ActionType.Console:
            SdtdConsole.Instance.ExecuteSync(message, null);
            break;
          case ActionType.Whisper:
            cInfo.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message, null,
              EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported));
            break;
          case ActionType.Broadcast:
            GameManager.Instance.ChatMessageServer(cInfo, EChatType.Global, -1, message, null, EMessageSender.None);
            break;
          case ActionType.Unknown:
          default:
            throw new ArgumentOutOfRangeException();
        }
      }
    }
  }
}

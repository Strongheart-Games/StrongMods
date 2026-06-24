using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomChatCommands {
  public static class CommandEvaluator {
    public static bool CheckRequirements(ChatCommandSender sender, ChatCommand command) {
      EntityPlayer player = sender.GetEntityPlayer();
      if (player is null) {
        return false;
      }

      if (!player.IsAdmin) {
        if (sender.ClientInfo is null) {
          return false;
        }
        AdminTools adminTools = GameManager.Instance.adminTools;
        var playerAdminLevel = adminTools is null ? 0 : adminTools.Users.GetUserPermissionLevel(sender.ClientInfo);
        if (playerAdminLevel > command.MinAdminLevel) {
          return false;
        }
      }

      if (command.Requirements.Count <= 0) {
        return true;
      }

      foreach (CommandRequirement req in command.Requirements) {
        if (!req.Type.Equals("cvar", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        var currentCVarValue = player.GetCVar(req.Name);
        if (!Mathf.Approximately(currentCVarValue, req.Value)) {
          return false;
        }
      }

      return true;
    }

    public static void ExecuteActionList(List<CommandAction> actions, ChatCommandSender sender) {
      foreach (CommandAction action in actions) {
        var message = CommandProcessor.ReplaceVariables(action.CommandText, sender);

        switch (action.Type) {
          case ActionType.Console:
            List<string> output = SdtdConsole.Instance.ExecuteSync(message, null);
            foreach (var line in output) {
              Log.Out($"[CustomChatCommands] {line}");
            }
            break;
          case ActionType.Whisper:
            if (sender.ClientInfo is null) {
              GameManager.Instance.ChatMessageClient(EChatType.Whisper, -1, message, new List<int> {sender.EntityId}, EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported);
            } else {
              GameManager.Instance.ChatMessageServer(sender.ClientInfo, EChatType.Whisper, -1, message, new List<int> {sender.EntityId}, EMessageSender.None);
            }
            break;
          case ActionType.Broadcast:
            if (sender.ClientInfo is null) {
              GameManager.Instance.ChatMessageClient(EChatType.Global, -1, message, null, EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported);
            } else {
              GameManager.Instance.ChatMessageServer(sender.ClientInfo, EChatType.Global, -1, message, null, EMessageSender.None);
            }
            break;
          case ActionType.Unknown:
          default:
            throw new ArgumentOutOfRangeException();
        }
      }
    }
  }
}

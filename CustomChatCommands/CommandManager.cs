using System;
using System.Collections.Generic;
using System.Xml;

namespace CustomChatCommands {
  public static class CommandManager {
    public static readonly Dictionary<string, ChatCommand> CommandsCache = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadCommandsFromXml(string filePath) {
      CommandsCache.Clear();
      try {
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(filePath);

        XmlNodeList commandNodes = xmlDoc.SelectNodes("/CustomChatCommands/Command");
        if (commandNodes == null) {
          return;
        }

        foreach (XmlNode cmdNode in commandNodes) {
          if (cmdNode.Attributes == null) {
            continue;
          }

          var trigger = cmdNode.Attributes["trigger"]?.Value;
          if (string.IsNullOrEmpty(trigger)) {
            continue;
          }

          var description = cmdNode.Attributes["description"]?.Value ?? string.Empty;

          var minAdmin = 1000;
          if (cmdNode.Attributes["minAdminLevel"] != null) {
            int.TryParse(cmdNode.Attributes["minAdminLevel"].Value, out minAdmin);
          }

          var newCommand = new ChatCommand {
            Trigger = trigger,
            Description = description,
            MinAdminLevel = minAdmin
          };

          XmlNode reqBlock = cmdNode.SelectSingleNode("Requirements");
          if (reqBlock is not null) {
            foreach (XmlNode reqNode in reqBlock.SelectNodes("Requirement")!) {
              var type = reqNode.Attributes!["type"]?.Value;
              var name = reqNode.Attributes["name"]?.Value;
              float.TryParse(reqNode.Attributes["value"]?.Value ?? "0", out var val);

              if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(name)) {
                newCommand.Requirements.Add(new CommandRequirement { Type = type, Name = name, Value = val });
              }
            }

            ParseActionList(reqBlock.SelectNodes("OnUnauthorized/Action"), newCommand.UnauthorizedActions);
          }

          ParseActionList(cmdNode.SelectNodes("Execute/Action"), newCommand.Actions);

          CommandsCache[trigger] = newCommand;
        }
      } catch (Exception ex) {
        Log.Error($"[CustomChatCommands] Error parsing CustomChatCommands XML: {ex.Message}");
      }
      Log.Out($"[CustomChatCommands] Loaded {CommandsCache.Count} commands: {string.Join(",", CommandsCache.Keys)}");
    }

    private static void ParseActionList(XmlNodeList nodes, List<CommandAction> targetList) {
      if (nodes == null) {
        return;
      }

      foreach (XmlNode actionNode in nodes) {
        if (actionNode.Attributes == null) {
          continue;
        }

        var typeString = actionNode.Attributes["type"]?.Value;
        var text = actionNode.InnerText;

        if (Enum.TryParse(typeString, true, out ActionType parsedType) && !string.IsNullOrEmpty(text)) {
          targetList.Add(new CommandAction { Type = parsedType, CommandText = text });
        }
      }
    }
  }
}

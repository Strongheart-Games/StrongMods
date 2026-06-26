using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using JetBrains.Annotations;
using UniLinq;

namespace CustomChatCommands {
  public static class CommandManager {
    private static FileSystemWatcher s_watcher;
    public static string FilePath { get; private set; }

    public static Dictionary<string, ChatCommand> Commands { get; private set; } = NewCommandsDict();

    private static Dictionary<string, ChatCommand> NewCommandsDict() {
      return new Dictionary<string, ChatCommand>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Init(string filePath) {
      FilePath = filePath;
      LoadCommandsFromXml();

      var directory = Path.GetDirectoryName(filePath);
      var fileName = Path.GetFileName(filePath);
      var watcher = new FileSystemWatcher(directory, fileName) {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        EnableRaisingEvents = true
      };
      watcher.Changed += (_, _) => LoadCommandsFromXml();
      s_watcher = watcher;
    }

    private static void LoadCommandsFromXml() {
      Dictionary<string, ChatCommand> commands = NewCommandsDict();
      try {
        Log.Out($"[CustomChatCommands] Loading {FilePath}...");
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(FilePath);

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

          List<string> aliases = cmdNode.Attributes["aliases"]?.Value?.Split(",").ToList() ?? new List<string>();

          var description = cmdNode.Attributes["description"]?.Value ?? string.Empty;

          var minAdmin = 1000;
          if (cmdNode.Attributes["minAdminLevel"] != null) {
            int.TryParse(cmdNode.Attributes["minAdminLevel"].Value, out minAdmin);
          }

          var newCommand = new ChatCommand {
            Trigger = trigger,
            Aliases = aliases,
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

          commands[trigger] = newCommand;
          foreach (var alias in newCommand.Aliases) {
            commands[alias] = newCommand;
          }
        }
      } catch (Exception ex) {
        Log.Error($"[CustomChatCommands] Error parsing CustomChatCommands XML: {ex.Message}");
      }

      Commands = commands;
      Log.Out($"[CustomChatCommands] Loaded {commands.Count} commands: {string.Join(",", commands.Keys)}");
    }

    private static void ParseActionList(XmlNodeList nodes, List<CommandAction> targetList) {
      if (nodes is null) {
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

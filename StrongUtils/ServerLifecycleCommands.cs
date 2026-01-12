using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace StrongUtils {
  public class ServerLifecycleCommands {
    private const string ConfigFileName = "server_lifecycle_commands.xml";

    public static void Init() {
      ConfigManager.Instance.RegisterConfigFile(ConfigFileName);
    }

    public static void OnGameStartDone() {
      List<string> commands = LoadCommands("on_game_start_done");
      ExecuteCommands(commands);
      RemoveCommands("on_game_start_done");
    }

    public static void AddCommand(string command) {
      try {
        var commandElement = new XElement("on_game_start_done");
        commandElement.SetAttributeValue("command", command);

        ConfigManager.Instance.AppendConfig(ConfigFileName, commandElement);

        Log.Out($"[ServerLifecycleCommands] Added command: {command}");
      } catch (Exception e) {
        Log.Error($"[ServerLifecycleCommands] Error adding command: {e.Message}");
      }
    }

    private static List<string> LoadCommands(string elementName) {
      var commands = new List<string>();

      try {
        XElement root = ConfigManager.Instance.ReadConfigFile(ConfigFileName);

        if (root == null) {
          Log.Warning($"[ServerLifecycleCommands] Config file not found or empty: {ConfigFileName}");
          return commands;
        }

        IEnumerable<XElement> commandElements = root.Elements(elementName);

        foreach (XElement element in commandElements) {
          XAttribute commandAttr = element.Attribute("command");
          if (commandAttr != null && !string.IsNullOrEmpty(commandAttr.Value)) {
            commands.Add(commandAttr.Value);
          }
        }

        Log.Out($"[ServerLifecycleCommands] Loaded {commands.Count} commands for {elementName}");
      } catch (Exception e) {
        Log.Error($"[ServerLifecycleCommands] Error loading config: {e.Message}");
      }

      return commands;
    }

    private static void ExecuteCommands(List<string> commands) {
      foreach (var command in commands) {
        try {
          Log.Out($"[ServerLifecycleCommands] Executing: {command}");
          SdtdConsole.Instance.ExecuteSync(command, null);
        } catch (Exception e) {
          Log.Error($"[ServerLifecycleCommands] Error executing command '{command}': {e.Message}");
        }
      }
    }

    private static void RemoveCommands(string elementName) {
      try {
        XElement root = ConfigManager.Instance.ReadConfigFile(ConfigFileName);

        if (root == null) {
          Log.Warning($"[ServerLifecycleCommands] Config file not found or empty: {ConfigFileName}");
          return;
        }

        IEnumerable<XElement> commands = root.Elements(elementName);

        foreach (XElement element in commands) {
          ConfigManager.Instance.RemoveConfig(ConfigFileName, element);
        }

        Log.Out($"[ServerLifecycleCommands] Removed all commands for {elementName}");
      } catch (Exception e) {
        Log.Error($"[ServerLifecycleCommands] Error loading config: {e.Message}");
      }
    }
  }
}

using System.IO;

namespace CustomChatCommands {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      CommandManager.LoadCommandsFromXml(GetConfigPath());
      ModEvents.ChatMessage.RegisterHandler(OnChatMessage);
      Log.Out("[CustomChatCommands] Initialized and listening for chat triggers.");
    }

    private static string GetConfigPath() {
      var configDir = Path.Combine(GameIO.GetSaveGameRootDir(), "StrongMods");
      var configPath = Path.Combine(configDir, "custom_chat_commands.xml");
      if (File.Exists(configPath)) {
        return configPath;
      }

      Directory.CreateDirectory(configDir);
      File.WriteAllText(configPath, GetDefaultXmlSkeleton());
      Log.Out($"[CustomChatCommands] Generated default configuration file at: {configPath}");
      return configPath;
    }

    private ModEvents.EModEventResult OnChatMessage(ref ModEvents.SChatMessageData data) {
      if (data.ClientInfo is null || string.IsNullOrEmpty(data.Message)) {
        return ModEvents.EModEventResult.Continue;
      }

      var messageParts = data.Message.Split(' ');
      var potentialTrigger = messageParts[0];

      if (!CommandManager.CommandsCache.TryGetValue(potentialTrigger, out ChatCommand command)) {
        return ModEvents.EModEventResult.Continue;
      }

      var isAuthorized = CommandEvaluator.CheckRequirements(data.ClientInfo, command);

      if (!isAuthorized) {
        if (command.UnauthorizedActions.Count <= 0) {
          // If no actions registered for unauthorized access, behave like the command doesn't exist
          return ModEvents.EModEventResult.Continue;
        }

        CommandEvaluator.ExecuteActionList(command.UnauthorizedActions, data.ClientInfo);
        return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      CommandEvaluator.ExecuteActionList(command.Actions, data.ClientInfo);
      return ModEvents.EModEventResult.StopHandlersAndVanilla;
    }

    private static string GetDefaultXmlSkeleton() {
      return @"<?xml version=""1.0"" encoding=""utf-8""?>
<CustomChatCommands>
<!-- EXAMPLES

  <Command trigger=""!hades"" description=""Announce the new season"" minAdminLevel=""0"">
    <Execute>
      <Action type=""broadcast"">Welcome to [ff6a02]Hades Season 2[-] on Stronghold!</Action>
    </Execute>
  </Command>

  <Command trigger=""/builderkit"" description=""Grants builder tools"" minAdminLevel=""0"">
    <Execute>
      <Action type=""console"">giveplus {EntityID} meleeToolPaintBrush 1</Action>
      <Action type=""console"">giveplus {EntityID} resourcePaint 500</Action>
      <Action type=""whisper"">Builder kit granted. Happy building, {Name}!</Action>
    </Execute>
  </Command>

  <Command trigger=""/stronghold"" description=""Teleports you to the faction stronghold."" minAdminLevel=""1000"">
    <Requirements>
      <Requirement type=""cvar"" name=""can_tp_to_stronghold"" value=""1"" />

      <OnUnauthorized>
        <Action type=""whisper"">You must complete the 'Desert Outpost' questline to unlock this teleport!</Action>
      </OnUnauthorized>
    </Requirements>

    <Execute>
      <Action type=""console"">teleport {EntityID} 1500 64 -2300</Action>
      <Action type=""whisper"">Teleporting...</Action>
    </Execute>
  </Command>

-->
</CustomChatCommands>";
    }
  }
}

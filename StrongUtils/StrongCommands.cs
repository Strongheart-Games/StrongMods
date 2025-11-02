using System.Collections.Generic;

namespace StrongUtils {
  public class StrongCommands {
    private static string s_passphrase = "My voice is my passport, verify me.";
    private static string s_teleport_stronghold = "teleportplayer {EntityId} 0 -1 0 n";
    private static string s_teleport_beginner_horde = "teleportplayer {EntityId} 0 -1 0 n";
    private static string s_teleport_advanced_horde = "teleportplayer {EntityId} 0 -1 0 n";

    private static string s_stronghold_citizen_cvar = "stronghold_citizen";

    private static string s_new_citizen_message = "Welcome to Stronghold, citizen!";
    private static string s_repeat_citizen_message =
      "We get it, you know the passphrase...it’s supposed to be a secret, stop repeating it everywhere!";
    private static string s_not_citizen_message =
      "Only those who know the passphrase may use the Stronghold teleportation network. Talk to The Ferryman and pray that he finds you worthy.";
    private static string s_wrong_passphrase_message =
      "I'm not sure where you heard that passphrase. Perhaps The Ferryman judged you unworthy...or perhaps you misunderstood him. Speak with him again and mind his words.";

    public static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      var player = GameManager.Instance.World.GetEntity(_data.SenderEntityId) as EntityPlayer;
      if (player is null) {
        // Don't handle messages from the server
        return ModEvents.EModEventResult.Continue;
      }

      var message = _data.Message.Trim();
      var firstSpace = message.IndexOf(' ');
      var command = firstSpace > 0 ? message.Substring(0, firstSpace) : message;
      switch (command.ToLower()) {
        case "/stronghold":
          HandleStrongholdCommand(player, firstSpace > 0 ? message.Substring(firstSpace + 1) : "");
          return ModEvents.EModEventResult.StopHandlersAndVanilla;
        case "/horde":
          HandleHordeCommand(player);
          return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      return ModEvents.EModEventResult.Continue;
    }

    public static void HandleStrongholdCommand(EntityPlayer player, string args) {
      if (string.IsNullOrWhiteSpace(args)) {
        ExecuteConsoleCommand(s_teleport_stronghold, player);
        return;
      }

      if (args.Trim('"', '“', '”').Equals(s_passphrase)) {
        if (player.GetCVar(s_stronghold_citizen_cvar) == 0) {
          Whisper(player, s_new_citizen_message);
          player.SetCVar(s_stronghold_citizen_cvar, 1);
        } else {
          Whisper(player, s_repeat_citizen_message);
        }
        ExecuteConsoleCommand(s_teleport_stronghold, player);
      } else {
        Whisper(player, s_wrong_passphrase_message);
      }
    }

    public static void HandleHordeCommand(EntityPlayer player) {
      var command = player.Progression.Level < 100 ? s_teleport_beginner_horde : s_teleport_advanced_horde;
      ExecuteConsoleCommand(command, player);
    }

    public static void Whisper(EntityPlayer player, string message) {
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        new List<int> { player.entityId }, EMessageSender.None,
        GeneratedTextManager.BbCodeSupportMode.Supported);
      package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
    }

    public static void ExecuteConsoleCommand(string template, EntityPlayer player) {
      if (player.GetCVar(s_stronghold_citizen_cvar) == 0) {
        Whisper(player, s_not_citizen_message);
        return;
      }

      var command = template.Replace("{EntityId}", player.entityId.ToString());
      SdtdConsole.Instance.ExecuteSync(command, null);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        return;
      }

      properties.ParseString("strong_commands_passphrase", ref s_passphrase);
      properties.ParseString("strong_commands_teleport_stronghold", ref s_teleport_stronghold);
      properties.ParseString("strong_commands_teleport_beginner_horde", ref s_teleport_beginner_horde);
      properties.ParseString("strong_commands_teleport_advanced_horde", ref s_teleport_advanced_horde);
    }
  }
}

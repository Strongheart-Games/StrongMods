using System.Collections.Generic;

namespace StrongUtils {
  public class StrongCommands {
    private static string s_passphrase = "My voice is my passport, verify me.";

    private static readonly string s_new_citizen_message = "Welcome, Strongsworn! You can now teleport to Stronghold by typing /stronghold in chat!";

    private static readonly string s_repeat_citizen_message =
      "We get it, you know the passphrase...it’s supposed to be a secret, stop repeating it everywhere!";

    private static readonly string s_wrong_passphrase_message =
      "I'm not sure where you heard that passphrase. Perhaps The Ferryman judged you unworthy...or perhaps you misunderstood him. Speak with him again and mind his words.";

    public static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      if (GameManager.Instance.World.GetEntity(_data.SenderEntityId) is not EntityPlayer player) {
        // Don't handle messages from the server
        return ModEvents.EModEventResult.Continue;
      }

      var message = _data.Message.Trim();
      var firstSpace = message.IndexOf(' ');
      var command = firstSpace > 0 ? message.Substring(0, firstSpace) : message;
      var args = firstSpace > 0 ? message.Substring(firstSpace + 1) : "";
      switch (command.ToLower()) {
        case "/strongsworn":
          HandleStrongswornCommand(player, args);
          return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      return ModEvents.EModEventResult.Continue;
    }

    private static void HandleStrongswornCommand(EntityPlayer player, string args) {
      if (args.Trim('"', '“', '”').Equals(s_passphrase)) {
        if (player.IsStrongSworn()) {
          Whisper(player, s_repeat_citizen_message);
        } else {
          Whisper(player, s_new_citizen_message);
          player.SetStrongSworn(true);
        }
      } else {
        Whisper(player, s_wrong_passphrase_message);
      }
    }

    private static void Whisper(EntityPlayer player, string message) {
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        new List<int> { player.entityId }, EMessageSender.None,
        GeneratedTextManager.BbCodeSupportMode.Supported);
      package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        return;
      }

      properties.ParseString("strong_commands_passphrase", ref s_passphrase);
    }
  }
}

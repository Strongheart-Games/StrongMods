using System.Collections.Generic;

namespace ChatCommandHelper {
  public class ChatCommandHelper : IModApi {
    public static readonly List<string> AsyncChatCommands = new List<string> {
      "link", "vote", "balance", "claim", "confirmTransfer", "grantCurrency", "revokeCurrency", "shop", "store",
      "topCurrency", "transfer", "daily", "streak", "topstreak"
    };

    public void InitMod(Mod _modInstance) {
      ModEvents.ChatMessage.RegisterHandler(HandleChatMessage);
    }

    private static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      // This should be the very last mod loaded and so the last handler in the chain, therefore, anything not handled
      // but looking like a command should be treated as such and not sent to the "recipient", but logged.
      if (_data.Message.StartsWith("/")) {
        var whisper = IsAsyncChatCommand(_data.Message.Substring(1))
          ? $"Processing command: {_data.Message}"
          : $"Unrecognized command: {_data.Message}";

        // Use the net package's logic to determine whether to send the message to the client or not
        NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, whisper,
          new List<int> { _data.SenderEntityId }, EMessageSender.None,
          GeneratedTextManager.BbCodeSupportMode.Supported);
        package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);

        // Takaro can see messages that get handled by other mods, so it's okay to stop handling its commands
        return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      return ModEvents.EModEventResult.Continue;
    }

    private static bool IsAsyncChatCommand(string message) {
      var iSpace = message.IndexOf(' ');
      var command = iSpace > 0 ? message.Substring(0, iSpace) : message;
      return AsyncChatCommands.ContainsCaseInsensitive(command);
    }
  }
}

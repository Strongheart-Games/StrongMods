namespace ChatCommandHelper {
  public class ChatCommandHelper : IModApi {
    public void InitMod(Mod _modInstance) {
      ModEvents.ChatMessage.RegisterHandler(HandleChatMessage);
    }

    private static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      // This should be the very last mod loaded and so the last handler in the chain, therefore, anything not handled
      // but looking like a command should be treated as such and not sent to the "recipient", but logged.
      if (_data.Message.StartsWith("/")) {
        // TODO: Differentiate between Async command processing and unknown commands, send a helpful whisper for the latter
        // Takaro can see messages that get handled by other mods, so it's okay to stop handling its commands
        return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      return ModEvents.EModEventResult.Continue;
    }
  }
}

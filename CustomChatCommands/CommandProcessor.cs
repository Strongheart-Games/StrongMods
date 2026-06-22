namespace CustomChatCommands {
  public static class CommandProcessor {
    public static string ReplaceVariables(string rawCommand, ClientInfo playerInfo) {
      if (string.IsNullOrEmpty(rawCommand) || playerInfo == null) {
        return rawCommand;
      }

      return rawCommand
        .Replace("{Name}", playerInfo.playerName)
        .Replace("{EntityID}", playerInfo.entityId.ToString())
        .Replace("{PlatformID}", playerInfo.PlatformId != null
          ? playerInfo.PlatformId.PlatformIdentifierString
          : "UnknownPlatformID")
        .Replace("{EOSID}", playerInfo.CrossplatformId != null
          ? playerInfo.CrossplatformId.PlatformIdentifierString
          : "UnknownEOSID");
    }
  }
}

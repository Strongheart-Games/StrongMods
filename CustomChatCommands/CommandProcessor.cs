namespace CustomChatCommands {
  public static class CommandProcessor {
    public static string ReplaceVariables(string rawCommand, ChatCommandSender sender) {
      if (string.IsNullOrEmpty(rawCommand)) {
        return rawCommand;
      }

      string name;
      string entityId;
      string platformId;
      string eosId;
      if (sender.ClientInfo is null) {
        EntityPlayer player = sender.GetEntityPlayer();
        name = player.PlayerDisplayName;
        entityId = sender.EntityId.ToString();
        platformId = player.PersistentPlayerData.PlatformData.NativeId.CombinedString;
        eosId = player.PersistentPlayerData.PlatformData.PrimaryId.CombinedString;
      } else {
        ClientInfo clientInfo = sender.ClientInfo;
        name = clientInfo.playerName;
        entityId = clientInfo.entityId.ToString();
        platformId = clientInfo.PlatformId != null ? clientInfo.PlatformId.CombinedString : "UnknownPlatformID";
        eosId = clientInfo.CrossplatformId != null ? clientInfo.CrossplatformId.CombinedString : "UnknownEOSID";
      }

      return rawCommand
        .Replace("{Name}", name)
        .Replace("{EntityID}", entityId)
        .Replace("{PlatformID}", platformId)
        .Replace("{EOSID}", eosId);
    }
  }
}

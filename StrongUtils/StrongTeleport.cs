namespace StrongUtils {
  public static class StrongTeleport {
    private const string StrongholdCvar = "can_tp_to_stronghold";
    private const string BedCvar = "can_tp_to_bed";

    private const string CommandPrefix = "/";
    private const string StrongholdCommand = "stronghold";
    private const string StrongholdShortCommand = "sh";
    private const string BedCommand = "bed";

    private const string StrongholdPrefabTag = "find_stronghold";

    private static string s_strongholdPrefabName;

    public static void Init() {
      foreach (PrefabInstance prefab in GameManager.Instance.GetDynamicPrefabDecorator().allPrefabs) {
        if (!prefab.prefab.HasAnyQuestTag(FastTags<TagGroup.Global>.GetTag(StrongholdPrefabTag))) {
          continue;
        }

        s_strongholdPrefabName = prefab.prefab.PrefabName;
        return;
      }

      Log.Error("[StrongTeleport] Could not find stronghold prefab");
    }

    public static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      Log.Out("[StrongTeleport] HandleChatMessage");
      if (GameManager.Instance.World.GetEntity(_data.SenderEntityId) is not EntityPlayer player) {
        // Don't handle messages from the server
        return ModEvents.EModEventResult.Continue;
      }

      var message = _data.Message.Trim();
      if (!message.StartsWith(CommandPrefix)) {
        return ModEvents.EModEventResult.Continue;
      }

      var firstSpace = message.IndexOf(' ');
      var command = firstSpace > 0 ? message.Substring(1, firstSpace) : message[1..];
      Log.Out($"[StrongTeleport] Command: {command}");
      switch (command.ToLower()) {
        case StrongholdCommand:
        case StrongholdShortCommand:
          if (HandleStrongholdCommand(player)) {
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
          }

          break;
        case BedCommand:
          if (HandleBedCommand(player)) {
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
          }

          break;
      }

      return ModEvents.EModEventResult.Continue;
    }

    private static bool HandleStrongholdCommand(EntityPlayer player) {
      if (s_strongholdPrefabName is null) {
        Log.Out($"[StrongTeleport] Could not find stronghold prefab");
        return false;
      }

      if (player.GetCVar(StrongholdCvar) < 1) {
        Log.Out($"[StrongTeleport] Player does not have permission to teleport to Stronghold");
        return false;
      }

      SdtdConsole.Instance.ExecuteSync($"teleportplayertoprefab {player.entityId} {s_strongholdPrefabName}", null);
      return true;
    }

    private static bool HandleBedCommand(EntityPlayer player) {
      if (player.GetCVar(BedCvar) < 1) {
        return false;
      }

      PersistentPlayerData data = player.SpawnPoints?.GetData();
      if (data is null || !data.HasBedrollPos) {
        return false;
      }

      SdtdConsole.Instance.ExecuteSync(
        $"teleportplayer {player.entityId} {data.BedrollPos.x} {data.BedrollPos.y} {data.BedrollPos.z}", null);
      return true;
    }
  }
}

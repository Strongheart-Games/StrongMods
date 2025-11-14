using System;
using System.Collections.Generic;

namespace StrongUtils {
  public class StrongAudit {
    public static void Audit_GameManager_ChangeBlocks(PlatformUserIdentifierAbs persistentPlayerId,
      List<BlockChangeInfo> _blocksToChange) {
      if (persistentPlayerId is null || _blocksToChange?.Count <= 1) {
        return;
      }

      HashSetLong chunks = new();
      var minX = int.MaxValue;
      var maxX = int.MinValue;
      var minZ = int.MaxValue;
      var maxZ = int.MinValue;
      foreach (BlockChangeInfo change in _blocksToChange) {
        var x = change.pos.x;
        var z = change.pos.z;
        minX = Math.Min(x, minX);
        maxX = Math.Max(x, maxX);
        minZ = Math.Min(z, minZ);
        maxZ = Math.Max(z, maxZ);
        var chunkKey = WorldChunkCache.MakeChunkKey(World.toChunkXZ(x), World.toChunkXZ(z));
        chunks.Add(chunkKey);
      }

      PersistentPlayerData player = GameManager.Instance.GetPersistentPlayerList()?.GetPlayerData(persistentPlayerId);
      if (player is null) {
        // We don't expect to get here as this should only happen if persistentPlayerId is null.
        return;
      }

      var playerInfo = $"{player.PlayerName?.DisplayName} ({persistentPlayerId})";
      var changeInfo = $"changed {_blocksToChange.Count} blocks in {chunks.Count} chunks";
      var locationInfo = _blocksToChange.Count == 1
        ? $"at {minX}, {minZ}"
        : $"between {minX}, {minZ} and {maxX}, {maxZ}";
      Log.Out($"[StrongUtils] ChangeBlocks: {playerInfo} {changeInfo} {locationInfo}");

      EntityPlayer playerEntity = GameManager.Instance.World.Players.dict[player.EntityId];
      // IsAdmin checks for permission level 0
      if (playerEntity is null || playerEntity.IsAdmin) {
        return;
      }

      SdtdConsole.Instance.ExecuteSync(
        $"ban add {persistentPlayerId.CombinedString} 10 years hacking \"{player.PlayerName?.DisplayName}\"", null);
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
        $"[ff0000]{player.PlayerName?.DisplayName} has been banned for hacking.[-]", null, EMessageSender.None);
    }
  }
}

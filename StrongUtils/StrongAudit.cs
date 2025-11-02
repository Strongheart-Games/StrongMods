using System.Collections.Generic;

namespace StrongUtils {
  public class StrongAudit {
    public static void Audit_GameManager_ChangeBlocks(PlatformUserIdentifierAbs persistentPlayerId,
      List<BlockChangeInfo> _blocksToChange) {
      HashSetLong chunks = new();
      foreach (BlockChangeInfo change in _blocksToChange) {
        var chunkKey = WorldChunkCache.MakeChunkKey(World.toChunkXZ(change.pos.x), World.toChunkXZ(change.pos.z));
        chunks.Add(chunkKey);
      }

      PersistentPlayerName playerName =
        GameManager.Instance.GetPersistentPlayerList()?.GetPlayerData(persistentPlayerId)?.PlayerName;
      Log.Out(
        $"[StrongUtils] ChangeBlocks: {playerName} ({persistentPlayerId}) changed {_blocksToChange.Count} blocks in {chunks.Count} chunks");
    }
  }
}

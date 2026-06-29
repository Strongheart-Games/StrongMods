using System.Collections.Generic;
using HarmonyLib;

namespace StrongLocks {
  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Prefix(List<BlockChangeInfo> _blocksToChange, out List<BlockValueRef> __state) {
      __state = null;
      foreach (BlockChangeInfo change in _blocksToChange) {
        if (!(change.blockValue.Block is BlockCompositeTileEntity block &&
              block.CompositeData.TryGetFeatureData<TEFeatureLockable>(out TileEntityFeatureData _))) {
          continue;
        }

        BlockValue currentBlock = GameManager.Instance.World.GetBlock(change.blockValueRef);
        if (change.blockValue.Equals(currentBlock)) {
          continue;
        }
        __state ??= new List<BlockValueRef>();
        __state.Add(change.blockValueRef);
      }
    }

    private static void Postfix(List<BlockValueRef> __state) {
      if (__state is null) {
        return;
      }
      foreach (BlockValueRef block in __state) {
        TileEntity tile = GameManager.Instance.World.GetTileEntity(block);
        TEFeatureLockable lockable = tile?.GetSelfOrFeature<TEFeatureLockable>();
        if (lockable is null) {
          continue;
        }

        if (lockable.IsLocked()) {
          continue;
        }
        lockable.SetLocked(true);
      }
    }
  }
}

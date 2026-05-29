using System.Collections.Generic;
using HarmonyLib;

namespace StrongFill {
  public static class StrongFill {
    private const string FillBlockName = "strong_fill";

    public static List<BlockChangeInfo> RemoveFillBlockChanges(List<BlockChangeInfo> changes) {
      List<BlockChangeInfo> fillBlockChanges = null;
      for (var x = 0; x < changes.Count; x++) {
        BlockChangeInfo change = changes[x];
        if (change.blockValue.Block.blockName is not FillBlockName) {
          continue;
        }

        fillBlockChanges ??= new List<BlockChangeInfo>();
        fillBlockChanges.Add(change);
        changes.Remove(change);
      }
      return fillBlockChanges;
    }

    public static void Fill(List<BlockChangeInfo> fillBlockChanges) {
      if (fillBlockChanges is null || fillBlockChanges.Count == 0) {
        return;
      }

      List<BlockChangeInfo> additionalChanges = new List<BlockChangeInfo>();
      World world = GameManager.Instance.World;
      foreach (BlockChangeInfo change in fillBlockChanges) {
        var posToFill = new Vector3i(change.pos.x, change.pos.y - 1, change.pos.z);
        BlockValue block = world.GetBlock(posToFill);
        if (block is { isair: false, isWater: false, ischild: false } &&
            block.Block.AutoShapeType == EAutoShapeType.Shape) {
          additionalChanges.Add(new BlockChangeInfo {
            pos = posToFill,
            bChangeDensity = true,
            density = MarchingCubes.DensityTerrainHi,
            bChangeBlockValue = false
          });
        }

        additionalChanges.Add(new BlockChangeInfo(change.pos, BlockValue.Air, MarchingCubes.DensityAirHi));
      }

      if (additionalChanges != null) {
        world.SetBlocksRPC(additionalChanges);
      }
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Prefix(List<BlockChangeInfo> _blocksToChange, out List<BlockChangeInfo> __state) {
      __state = StrongFill.RemoveFillBlockChanges(_blocksToChange);
    }
    private static void Postfix(List<BlockChangeInfo> __state) {
      StrongFill.Fill(__state);
    }
  }
}

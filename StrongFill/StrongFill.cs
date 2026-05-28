using System.Collections.Generic;
using HarmonyLib;

namespace StrongFill {
  public static class StrongFill {
    private const string FillBlock = "strong_fill";

    public static void FillIfPresent(List<BlockChangeInfo> changes) {
      foreach (BlockChangeInfo change in changes) {
        if (change.blockValue.Block.blockName is not FillBlock) {
          continue;
        }

        World world = GameManager.Instance.World;


        var position = new Vector3i(change.pos.x, change.pos.y - 1, change.pos.z);
        BlockValue block = world.GetBlock(position);
        if (block is { isair: false, isWater: false, ischild: false } &&
            block.Block.AutoShapeType == EAutoShapeType.Shape) {
          world.SetBlockRPC(position, block, MarchingCubes.DensityTerrainHi);
        }

        world.SetBlockRPC(change.pos, BlockValue.Air);
      }
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Postfix(List<BlockChangeInfo> _blocksToChange) {
      StrongFill.FillIfPresent(_blocksToChange);
    }
  }
}

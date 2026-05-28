using System.Collections.Generic;
using HarmonyLib;

namespace StrongFill {
  public static class StrongFill {
    private const string FillBlock = "strong_fill";

    public static void FillIfPresent(List<BlockChangeInfo> changes) {
      List<Vector3i> positionsToFill = null;
      for (var x = 0; x < changes.Count; x++) {
        BlockChangeInfo change = changes[x];
        if (change.blockValue.Block.blockName is not FillBlock) {
          continue;
        }

        positionsToFill ??= new List<Vector3i>();
        positionsToFill.Add(new Vector3i(change.pos.x, change.pos.y - 1, change.pos.z));

        changes.RemoveAt(x);
      }

      if (positionsToFill is null) {
        return;
      }

      // Add at the end so we don't bother checking the new changes in the loop above
      foreach (Vector3i position in positionsToFill) {
        BlockValue block = GameManager.Instance.World.GetBlock(position);
        if (block.isair || block.isWater || block.ischild) {
          continue;
        }

        changes.Add(new BlockChangeInfo {
          pos = position,
          bChangeDensity = true,
          density = MarchingCubes.DensityTerrainHi,
          bChangeBlockValue = false
        });
      }
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Prefix(List<BlockChangeInfo> _blocksToChange) {
      StrongFill.FillIfPresent(_blocksToChange);
    }
  }
}

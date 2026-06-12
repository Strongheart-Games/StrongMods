using System.Collections.Generic;

namespace StrongFill {
  public class BlockStrongFill : BlockUpgradeRated {
    public override bool UpdateTick(WorldBase _world, int _clrIdx, Vector3i _blockPos, BlockValue _blockValue,
      bool _bRandomTick, ulong _ticksIfLoaded, GameRandom _rnd) {
      List<BlockChangeInfo> fillChanges = Fill(new Vector3i(_blockPos.x, _blockPos.y - 1, _blockPos.z));
      if (fillChanges is null || fillChanges.Count == 0) {
        // If there's nothing to fill, leave the block there for the player to pick it up
        return true;
      }

      GameManager.Instance.World.SetBlocksRPC(fillChanges);
      return base.UpdateTick(_world, _clrIdx, _blockPos, _blockValue, _bRandomTick, _ticksIfLoaded, _rnd);
    }

    private static List<BlockChangeInfo> Fill(Vector3i pos) {
      World world = GameManager.Instance.World;
      BlockValue b = world.GetBlock(pos);
      if (!b.isTerrain) {
        return null;
      }

      // Cardinal directions (N, S, E, W)
      var n = new Vector3i(pos.x, pos.y, pos.z + 1);
      var fillN = IsShape(n);
      var s = new Vector3i(pos.x, pos.y, pos.z - 1);
      var fillS = IsShape(s);
      var e = new Vector3i(pos.x + 1, pos.y, pos.z);
      var fillE = IsShape(e);
      var w = new Vector3i(pos.x - 1, pos.y, pos.z);
      var fillW = IsShape(w);

      // Diagonals are only filled if both it AND it's neighboring cardinals are shapes
      var ne = new Vector3i(pos.x + 1, pos.y, pos.z + 1);
      var fillNE = fillN && fillE && IsShape(ne);
      var se = new Vector3i(pos.x + 1, pos.y, pos.z - 1);
      var fillSE = fillS && fillE && IsShape(se);
      var sw = new Vector3i(pos.x - 1, pos.y, pos.z - 1);
      var fillSW = fillS && fillW && IsShape(sw);
      var nw = new Vector3i(pos.x - 1, pos.y, pos.z + 1);
      var fillNW = fillN && fillW && IsShape(nw);

      List<BlockChangeInfo> fillChanges = null;
      MaybeAddFillChanges(fillN, n, ref fillChanges);
      MaybeAddFillChanges(fillS, s, ref fillChanges);
      MaybeAddFillChanges(fillE, e, ref fillChanges);
      MaybeAddFillChanges(fillW, w, ref fillChanges);
      MaybeAddFillChanges(fillNE, ne, ref fillChanges);
      MaybeAddFillChanges(fillSE, se, ref fillChanges);
      MaybeAddFillChanges(fillSW, sw, ref fillChanges);
      MaybeAddFillChanges(fillNW, nw, ref fillChanges);
      return fillChanges;
    }

    private static void MaybeAddFillChanges(bool fill, Vector3i pos, ref List<BlockChangeInfo> fillChanges) {
      if (!fill) {
        return;
      }

      fillChanges ??= new List<BlockChangeInfo>();
      fillChanges.Add(new BlockChangeInfo {
        pos = pos,
        bChangeDensity = true,
        density = MarchingCubes.DensityTerrainHi,
        bChangeBlockValue = false
      });
    }

    private static bool IsShape(Vector3i pos) {
      World world = GameManager.Instance.World;
      BlockValue b = world.GetBlock(pos);
      return b is { isair: false, isWater: false, ischild: false } &&
             b.Block.AutoShapeType == EAutoShapeType.Shape;
    }
  }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace StrongBoxes {
  public static class SortBoxes {
    private const int MaxDistance = 15;

    public static void Init() {
      StrongBoxes.RegisterOnCloseListener(IsSortBox, OnClose);
    }

    private static bool IsSortBox(string label) {
      return "sort".EqualsCaseInsensitive(label);
    }

    private static void OnClose(TileEntity sortBox) {
      ITileEntityLootable lootable = sortBox.GetSelfOrFeature<ITileEntityLootable>();
      if (lootable is null || lootable.IsEmpty()) {
        return;
      }

      var targets = new List<SortTarget>();
      foreach (var k in StrongBoxes.CalculateAdjacentChunkKeys(sortBox.ToWorldPos())) {
        targets.AddRange(GetQualifiedSortTargets(k, sortBox));
      }

      // TODO: Sort by distance

      foreach (SortTarget t in targets) {
        Transfer(sortBox, t);
      }
      Log.Out("[StrongBoxes] Sort done");
    }

    private static void Transfer(TileEntity source, SortTarget target) {
      ITileEntityLootable lootableSource = source.GetSelfOrFeature<ITileEntityLootable>();
      if (lootableSource is null || lootableSource.IsEmpty()) {
        return;
      }
      ITileEntityLootable lootableTarget = target.Target.GetSelfOrFeature<ITileEntityLootable>();
      if (lootableTarget is null || lootableTarget.IsEmpty()) {
        return;
      }

    }

    private static IEnumerable<SortTarget> GetQualifiedSortTargets(long chunkKey, TileEntity sortBox) {
      var c = (Chunk)GameManager.Instance.World.GetChunkSync(chunkKey);
      var targets = c?.GetTileEntities().list
        .Select(t => ToSortTarget(sortBox, t))
        .Where(t => IsQualifiedSortTarget(sortBox, t))
        .ToList();
      targets?.Sort((a, b) => a.Distance.CompareTo(b.Distance));
      return targets;
    }

    private static SortTarget ToSortTarget(TileEntity sortBox, TileEntity target) {
      return new SortTarget(target, (sortBox.ToWorldCenterPos() - target.ToWorldCenterPos()).magnitude);
    }

    private static bool IsQualifiedSortTarget(TileEntity sortBox, SortTarget target) {
      // TODO: Check owner, lock, password and whether someone's currently in the box
      return target.Distance <= MaxDistance;
    }

    private struct SortTarget {
      public readonly TileEntity Target;
      public readonly float Distance;

      public SortTarget(TileEntity target, float distance) {
        Target = target;
        Distance = distance;
      }
    }
  }
}

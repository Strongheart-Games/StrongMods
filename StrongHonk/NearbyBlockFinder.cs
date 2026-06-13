using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;

namespace StrongHonk {
  public static class NearbyBlockFinder {
    private static readonly ConcurrentBag<BlockCategory> s_categories = new();

    private static readonly
      ConcurrentDictionary<string, ConcurrentDictionary<long, ConcurrentDictionary<Vector3i, TrackedBlock>>> s_blocks =
        new();

    public static void AddCategory(string name, Func<Block, WorldBase, int, Vector3i, BlockValue, bool> filter) {
      s_categories.Add(new BlockCategory(name, filter));
      s_blocks.TryAdd(name, new ConcurrentDictionary<long, ConcurrentDictionary<Vector3i, TrackedBlock>>());
    }

    public static void ForeachNearbyBlock(string category, Vector3i pos, int maxDistance,
      Action<Block, Vector3i> callback) {
      ConcurrentDictionary<long, ConcurrentDictionary<Vector3i, TrackedBlock>> blocksByChunk =
        s_blocks.GetValueOrDefault(category, null);
      if (blocksByChunk is null) {
        return;
      }

      TrackedBlock? closestBlock = null;
      var closestDistance = float.MaxValue;
      foreach (var chunkKey in CaclulateNearbyChunkKeys(pos)) {
        ConcurrentDictionary<Vector3i, TrackedBlock> blocksByPos = blocksByChunk.GetValueOrDefault(chunkKey, null);
        if (blocksByPos is null) {
          continue;
        }

        foreach (Vector3i blockPos in blocksByPos.Keys) {
          var distance = (blockPos.ToVector3CenterXZ() - pos.ToVector3CenterXZ()).magnitude;
          if (distance > maxDistance || distance >= closestDistance) {
            continue;
          }

          closestBlock = blocksByPos[blockPos];
          closestDistance = distance;
        }
      }

      if (closestBlock is null) {
        return;
      }

      callback.Invoke(((TrackedBlock)closestBlock).Block, ((TrackedBlock)closestBlock).Pos);
    }

    private static HashSet<long> CaclulateNearbyChunkKeys(Vector3i pos) {
      var posX = World.toChunkXZ(pos.x);
      var posZ = World.toChunkXZ(pos.z);
      var nearby = new HashSet<long>();
      for (var x = posX - 1; x <= posX + 1; x++) {
        for (var z = posZ - 1; z <= posZ + 1; z++) {
          nearby.Add(WorldChunkCache.MakeChunkKey(x, z));
        }
      }

      return nearby;
    }

    private static void AddBlock(Block block, WorldBase world, int clrIdx, Vector3i pos, BlockValue blockValue) {
      foreach (BlockCategory c in s_categories) {
        if (!c.Filter(block, world, clrIdx, pos, blockValue)) {
          continue;
        }

        ConcurrentDictionary<long, ConcurrentDictionary<Vector3i, TrackedBlock>> blocksByChunk =
          s_blocks.GetOrAdd(c.Name, new ConcurrentDictionary<long, ConcurrentDictionary<Vector3i, TrackedBlock>>());
        var chunkKey = WorldChunkCache.MakeChunkKey(World.toChunkXZ(pos.x), World.toChunkXZ(pos.z));
        ConcurrentDictionary<Vector3i, TrackedBlock> blocksByPos =
          blocksByChunk.GetOrAdd(chunkKey, new ConcurrentDictionary<Vector3i, TrackedBlock>());
        blocksByPos[pos] = new TrackedBlock(block, pos);
      }
    }

    private struct BlockCategory {
      public readonly string Name;
      public readonly Func<Block, WorldBase, int, Vector3i, BlockValue, bool> Filter;

      public BlockCategory(string name, Func<Block, WorldBase, int, Vector3i, BlockValue, bool> filter) {
        Name = name;
        Filter = filter;
      }
    }

    private struct TrackedBlock {
      public readonly Block Block;
      public readonly Vector3i Pos;

      public TrackedBlock(Block block, Vector3i pos) {
        Block = block;
        Pos = pos;
      }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockLoaded))]
    public class Block_OnBlockLoaded_Patch {
      private static void Postfix(Block __instance, WorldBase _world, int _clrIdx, Vector3i _blockPos,
        BlockValue _blockValue) {
        AddBlock(__instance, _world, _clrIdx, _blockPos, _blockValue);
      }
    }
  }
}

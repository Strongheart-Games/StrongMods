using System;
using System.Collections.Generic;

namespace Tests.SortBoxes;

/// <summary>
///   A block position, in world coordinates. A local stand-in for the game's <c>Vector3i</c> so the scan
///   geometry can be exercised with no game tree, no load context and no world — these strategies are pure
///   integer geometry, and running them for real is what turns a reading of the code into a measurement.
/// </summary>
public readonly struct BlockPos : IEquatable<BlockPos> {
  public readonly int X;
  public readonly int Y;
  public readonly int Z;

  public BlockPos(int x, int y, int z) {
    X = x;
    Y = y;
    Z = z;
  }

  public bool Equals(BlockPos other) => X == other.X && Y == other.Y && Z == other.Z;

  public override bool Equals(object obj) => obj is BlockPos other && Equals(other);

  public override int GetHashCode() => (X * 397 ^ Y) * 397 ^ Z;

  public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>
///   What one container-discovery scan did: which positions it probed, how many times, and how many probes it
///   cost. A "probe" is one <c>world.GetTileEntity</c> call for the coordinate strategies, and one tile-entity
///   dictionary entry for the chunk strategies — the unit that dominates each approach's cost.
/// </summary>
public sealed class SortScanTrace {
  private readonly Dictionary<BlockPos, int> visits = new();
  private readonly Dictionary<BlockPos, int> firstShell = new();

  public int Probes { get; private set; }

  /// <summary>
  ///   Which shell the walk is currently sweeping. Set by the shell strategies as they go, so
  ///   <see cref="FirstShell" /> can record how far out a position was when it was first reached — the
  ///   measurement that shows whether a shell walk really delivers nearest-first.
  /// </summary>
  public int CurrentShell { get; set; }

  public IReadOnlyDictionary<BlockPos, int> Visits => visits;

  /// <summary>The shell each position was first probed in, keyed by position.</summary>
  public IReadOnlyDictionary<BlockPos, int> FirstShell => firstShell;

  public int DistinctPositions => visits.Count;

  public int RepeatedProbes => Probes - visits.Count;

  public void Probe(int x, int y, int z) {
    Probes++;
    var pos = new BlockPos(x, y, z);
    if (visits.TryGetValue(pos, out var n)) {
      visits[pos] = n + 1;
    } else {
      visits[pos] = 1;
      firstShell[pos] = CurrentShell;
    }
  }

  public bool Visited(int x, int y, int z) => visits.ContainsKey(new BlockPos(x, y, z));
}

/// <summary>
///   The container-discovery geometries of the three reference sort mods and of StrongBoxes, ported faithfully
///   enough to measure. Each method enumerates the positions the real code would hand to
///   <c>world.GetTileEntity</c>, in the real order, so coverage gaps, repeated probes, clamp violations and
///   raw cost are all observable without a running game.
///   <para>
///     Fidelity is the whole point, so the ports keep the originals' oddities — including
///     <see cref="RoboticInboxShellAsShipped" />'s two <c>FastMax</c> calls where the shell wants
///     <c>FastMin</c>, and <see cref="RoboticInboxMaxDistanceAsShipped" />'s component list. Where a corrected
///     variant exists it is a separate method, so a test can compare the two rather than assert against prose.
///   </para>
///   Sources: RoboticInbox <c>StorageManager.OrganizeCoroutine</c>; RoboticSorter
///   <c>SorterServerPatch.ProcessSortingFromSorter</c>; ServerTools <c>Sorter.SortContainers</c>; StrongBoxes
///   <c>SortBoxes.OnClose</c> with <c>StrongBoxes.CalculateAdjacentChunkKeys</c>.
/// </summary>
public static class SortScanStrategies {
  /// <summary>Chunks are 16 blocks wide in X and Z — <c>World.toChunkXZ</c> is <c>_v &gt;&gt; 4</c>.</summary>
  public const int ChunkSizeXZ = 16;

  /// <summary>And 256 tall — <c>World.toChunkY</c> is <c>_v &gt;&gt; 8</c>, so one chunk spans every Y.</summary>
  public const int ChunkSizeY = 256;

  private static int Max(int a, int b) => a > b ? a : b;

  private static int Min(int a, int b) => a < b ? a : b;

  /// <summary>
  ///   RoboticInbox's shell walk, exactly as shipped. Six face sweeps per shell at Chebyshev distance
  ///   <c>d</c>: the two Y planes take the full (2d+1)² square, the two Z planes take full X but Y narrowed to
  ///   the interior, and the two X planes narrow both — the standard decomposition that covers a cube shell
  ///   without double-counting its edges.
  ///   <para>
  ///     Two bounds do not match that intent: in both Y-plane sweeps the inner Z loop closes on
  ///     <c>FastMax(source.z + d, max.z)</c> where every sibling bound uses <c>FastMin</c>. The consequence is
  ///     measured by the tests, not asserted here.
  ///   </para>
  /// </summary>
  public static SortScanTrace RoboticInboxShellAsShipped(BlockPos source, BlockPos min, BlockPos max) =>
    RoboticInboxShell(source, min, max, clampZWithMin: false);

  /// <summary>
  ///   The same walk with the two Y-plane Z bounds using <c>FastMin</c>, which is what a shell sweep clamped
  ///   to a box requires. The difference between this and <see cref="RoboticInboxShellAsShipped" /> is the
  ///   whole effect of that one swapped call.
  /// </summary>
  public static SortScanTrace RoboticInboxShellCorrected(BlockPos source, BlockPos min, BlockPos max) =>
    RoboticInboxShell(source, min, max, clampZWithMin: true);

  private static SortScanTrace RoboticInboxShell(BlockPos source, BlockPos min, BlockPos max, bool clampZWithMin) {
    var trace = new SortScanTrace();
    var maxDist = RoboticInboxMaxDistanceAsShipped(
      new BlockPos(source.X - min.X, source.Y - min.Y, source.Z - min.Z),
      new BlockPos(max.X - source.X, max.Y - source.Y, max.Z - source.Z));

    for (var d = 1; d <= maxDist; d++) {
      trace.CurrentShell = d;
      // The two Y planes: full square in X and Z.
      foreach (var y in new[] { source.Y - d, source.Y + d }) {
        if (y < min.Y || y > max.Y) {
          continue;
        }

        var zEnd = clampZWithMin ? Min(source.Z + d, max.Z) : Max(source.Z + d, max.Z);
        for (var x = Max(source.X - d, min.X); x <= Min(source.X + d, max.X); x++) {
          for (var z = Max(source.Z - d, min.Z); z <= zEnd; z++) {
            trace.Probe(x, y, z);
          }
        }
      }

      // The two Z planes: full X, interior Y.
      foreach (var z in new[] { source.Z - d, source.Z + d }) {
        if (z < min.Z || z > max.Z) {
          continue;
        }

        for (var y = Max(source.Y - d + 1, min.Y); y <= Min(source.Y + d - 1, max.Y); y++) {
          for (var x = Max(source.X - d, min.X); x <= Min(source.X + d, max.X); x++) {
            trace.Probe(x, y, z);
          }
        }
      }

      // The two X planes: interior Y and Z.
      foreach (var x in new[] { source.X - d, source.X + d }) {
        if (x < min.X || x > max.X) {
          continue;
        }

        for (var y = Max(source.Y - d + 1, min.Y); y <= Min(source.Y + d - 1, max.Y); y++) {
          for (var z = Max(source.Z - d + 1, min.Z); z <= Min(source.Z + d - 1, max.Z); z++) {
            trace.Probe(x, y, z);
          }
        }
      }
    }

    return trace;
  }

  /// <summary>
  ///   RoboticInbox's <c>FindMaxDistance</c>, component list intact. It is handed the two corner offsets
  ///   (source-to-min, max-to-source) and returns how many shells the walk will run.
  ///   <para>
  ///     The list it folds over is <c>{ v1.z, v1.y, v1.z, v2.x, v2.y, v2.z }</c> — <c>v1.z</c> twice and
  ///     <c>v1.x</c> not at all. Whether that can actually truncate a scan is what the tests settle.
  ///   </para>
  /// </summary>
  public static int RoboticInboxMaxDistanceAsShipped(BlockPos toMin, BlockPos fromMax) {
    var result = 0;
    foreach (var component in new[] { toMin.Z, toMin.Y, toMin.Z, fromMax.X, fromMax.Y, fromMax.Z }) {
      if (result < component) {
        result = component;
      }
    }

    return result;
  }

  /// <summary>The same fold over all six components, which is what a bounding walk needs.</summary>
  public static int RoboticInboxMaxDistanceCorrected(BlockPos toMin, BlockPos fromMax) {
    var result = 0;
    foreach (var component in new[] { toMin.X, toMin.Y, toMin.Z, fromMax.X, fromMax.Y, fromMax.Z }) {
      if (result < component) {
        result = component;
      }
    }

    return result;
  }

  /// <summary>
  ///   RoboticSorter's dense cube: every coordinate in the axis-aligned box of the given radius, in x-then-y
  ///   -then-z order, skipping only the sorter's own position. No shell structure, so no distance ordering
  ///   and no early exit.
  /// </summary>
  public static SortScanTrace RoboticSorterCube(BlockPos source, int radius) {
    var trace = new SortScanTrace();
    for (var x = source.X - radius; x <= source.X + radius; x++) {
      for (var y = source.Y - radius; y <= source.Y + radius; y++) {
        for (var z = source.Z - radius; z <= source.Z + radius; z++) {
          if (x == source.X && y == source.Y && z == source.Z) {
            continue;
          }

          trace.Probe(x, y, z);
        }
      }
    }

    return trace;
  }

  /// <summary>The closed form of <see cref="RoboticSorterCube" />'s probe count: (2r+1)³ − 1.</summary>
  public static long RoboticSorterCubeProbeCount(int radius) {
    long side = 2L * radius + 1;
    return side * side * side - 1;
  }

  /// <summary>
  ///   The chunk keys StrongBoxes asks for: its own chunk and the eight around it, from
  ///   <c>CalculateAdjacentChunkKeys</c>. Y is not part of a chunk key — one chunk spans the whole column.
  /// </summary>
  public static HashSet<(int X, int Z)> StrongBoxesChunkWindow(BlockPos source) {
    var chunkX = source.X >> 4;
    var chunkZ = source.Z >> 4;
    var keys = new HashSet<(int, int)>();
    for (var x = chunkX - 1; x <= chunkX + 1; x++) {
      for (var z = chunkZ - 1; z <= chunkZ + 1; z++) {
        keys.Add((x, z));
      }
    }

    return keys;
  }

  /// <summary>
  ///   How far, in blocks, a chunk window of the given half-width reaches from <paramref name="source" /> in
  ///   its worst horizontal direction. A window is whole chunks, so the guaranteed reach depends on where in
  ///   its chunk the source sits: a source flush against a chunk edge gets a full chunk on that side and only
  ///   the rest of its own chunk on the other. This is the number any radius must stay under for the window to
  ///   be sufficient.
  /// </summary>
  public static int ChunkWindowGuaranteedReach(int chunkHalfWidth) {
    var worst = int.MaxValue;
    for (var local = 0; local < ChunkSizeXZ; local++) {
      // Distance to the far edge of the last chunk on each side, from a source at this offset in its own.
      var toNegative = local + chunkHalfWidth * ChunkSizeXZ;
      var toPositive = ChunkSizeXZ - 1 - local + chunkHalfWidth * ChunkSizeXZ;
      worst = Min(worst, Min(toNegative, toPositive));
    }

    return worst;
  }

  /// <summary>
  ///   What a chunk-keyed scan costs: one visit per tile entity in the window, independent of the block
  ///   volume. ServerTools and StrongBoxes both pay this rather than a per-coordinate probe.
  /// </summary>
  public static int ChunkTileEntityProbeCount(int chunkCount, int tileEntitiesPerChunk) =>
    chunkCount * tileEntitiesPerChunk;

  /// <summary>
  ///   The block volume a chunk window covers, for comparison against the tile-entity count actually walked.
  ///   The ratio between the two is the whole argument for scanning chunks instead of coordinates.
  /// </summary>
  public static long ChunkWindowBlockVolume(int chunkCount) =>
    (long)chunkCount * ChunkSizeXZ * ChunkSizeXZ * ChunkSizeY;
}

using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Tests.SortBoxes;

/// <summary>
///   Measurements of the four container-discovery geometries in <see cref="SortScanStrategies" />, taken for
///   the research behind #31. These are not assertions about StrongBoxes' shipping behaviour — StrongBoxes
///   does not sort anything yet. They exist so the comparison of the three reference mods rests on numbers
///   that can be re-run rather than on a reading of decompiled loops.
///   <para>
///     Settings used are the real deployed ones: RoboticInbox's <c>robotic-inbox.json</c> carries
///     <c>InboxHorizontalRange</c> and <c>InboxVerticalRange</c> of 20 with <c>BaseSiphoningProtection</c>
///     on; RoboticSorter's <c>blocks.xml</c> sets <c>SorterSearchRadius</c> to 30.
///   </para>
/// </summary>
public sealed class SortScanStrategyTests {
  private const int InboxRange = 20;
  private const int SorterRadius = 30;

  private readonly ITestOutputHelper output;

  public SortScanStrategyTests(ITestOutputHelper output) {
    this.output = output;
  }

  // -----------------------------------------------------------------------------------------------
  // The shell walk's two swapped bounds
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   With the clamp box symmetric about the source, the swapped bound is invisible: the walk's last shell
  ///   lands exactly on the +Z edge, so <c>FastMax(source.z + d, max.z)</c> and <c>FastMin</c> of the same two
  ///   numbers agree everywhere. This is why the defect survives casual testing — a box placed in the middle
  ///   of its claim behaves correctly.
  /// </summary>
  [Fact]
  public void A_clamp_box_centred_on_the_source_hides_the_swapped_bound_entirely() {
    BlockPos source = Centre();
    (BlockPos min, BlockPos max) = ClaimBox(source);

    SortScanTrace shipped = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);

    Assert.DoesNotContain(shipped.Visits.Keys, p => p.Z > max.Z);
  }

  /// <summary>
  ///   Move the source near the +Z edge of its land claim and the same bound reaches straight through it.
  ///   The clamp box is then asymmetric — a long run in X, a short one in +Z — so the shell count is set by X
  ///   while the Y-plane sweeps keep extending in Z to <c>source.z + d</c>, well past the claim edge that
  ///   <c>BaseSiphoningProtection</c> computed.
  ///   <para>
  ///     That makes it a containment failure and not merely a cost one: the scan offers items to containers
  ///     outside the land claim the protection exists to confine it to.
  ///   </para>
  /// </summary>
  [Fact]
  public void A_source_near_the_claim_edge_makes_the_shell_walk_reach_outside_the_land_claim() {
    BlockPos source = Centre();
    // The inbox sits 2 blocks in from the +Z edge of its claim; X and Y still have the full range.
    var min = new BlockPos(source.X - InboxRange, source.Y - InboxRange, source.Z - InboxRange);
    var max = new BlockPos(source.X + InboxRange, source.Y + InboxRange, source.Z + 2);

    SortScanTrace shipped = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);
    SortScanTrace corrected = SortScanStrategies.RoboticInboxShellCorrected(source, min, max);

    List<BlockPos> beyond = shipped.Visits.Keys.Where(p => p.Z > max.Z).ToList();
    var overrun = beyond.Count == 0 ? 0 : beyond.Max(p => p.Z) - max.Z;
    output.WriteLine($"claim edge at Z={max.Z}; shipped walk probed {beyond.Count} positions beyond it, " +
                     $"reaching {overrun} blocks outside the claim");
    output.WriteLine($"shipped {shipped.Probes:N0} probes vs corrected {corrected.Probes:N0} " +
                     $"({(double)shipped.Probes / corrected.Probes:F1}x)");

    Assert.NotEmpty(beyond);
    Assert.Equal(InboxRange - 2, overrun);
    Assert.DoesNotContain(corrected.Visits.Keys, p => p.Z > max.Z);
  }

  /// <summary>
  ///   Inside the box the overrun is not a containment problem but an ordering and cost one: the two Y-plane
  ///   sweeps stop being a shell and become a strip running out to the far Z edge at every distance, so the
  ///   same position is probed once per shell instead of once.
  /// </summary>
  [Fact]
  public void The_shipped_shell_walk_probes_the_same_positions_many_times_over() {
    BlockPos source = Centre();
    (BlockPos min, BlockPos max) = ClaimBox(source);

    SortScanTrace shipped = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);
    SortScanTrace corrected = SortScanStrategies.RoboticInboxShellCorrected(source, min, max);

    output.WriteLine($"shipped:   {shipped.Probes,9} probes over {shipped.DistinctPositions,7} positions " +
                     $"({shipped.RepeatedProbes} repeats, worst position probed " +
                     $"{shipped.Visits.Values.Max()}x)");
    output.WriteLine($"corrected: {corrected.Probes,9} probes over {corrected.DistinctPositions,7} positions " +
                     $"({corrected.RepeatedProbes} repeats)");
    output.WriteLine($"shipped costs {(double)shipped.Probes / corrected.Probes:F2}x the corrected walk");

    Assert.Equal(0, corrected.RepeatedProbes);
    Assert.True(shipped.RepeatedProbes > 0);
    Assert.True(shipped.Probes > corrected.Probes);
  }

  /// <summary>
  ///   A shell walk exists to deliver nearest containers first, which is the only reason the six-face
  ///   decomposition is worth its complexity. The shipped bound breaks that: a far position on a Y plane is
  ///   reached at a shell far below its true Chebyshev distance, so it is offered items before nearer ones.
  /// </summary>
  [Fact]
  public void The_shipped_shell_walk_loses_the_nearest_first_ordering_it_exists_to_provide() {
    BlockPos source = Centre();
    (BlockPos min, BlockPos max) = ClaimBox(source);

    SortScanTrace shipped = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);
    SortScanTrace corrected = SortScanStrategies.RoboticInboxShellCorrected(source, min, max);

    List<KeyValuePair<BlockPos, int>> early = shipped.FirstShell
      .Where(e => Chebyshev(source, e.Key) > e.Value)
      .OrderByDescending(e => Chebyshev(source, e.Key) - e.Value).ToList();

    output.WriteLine($"{early.Count} positions are reached before their true distance shell");
    foreach (KeyValuePair<BlockPos, int> e in early.Take(3)) {
      output.WriteLine($"  {e.Key} is {Chebyshev(source, e.Key)} away but first probed at shell {e.Value}");
    }

    Assert.NotEmpty(early);
    // The corrected walk reaches every position in exactly its own distance shell — that is the property
    // the decomposition is for, and it is the one the shipped bound gives up.
    Assert.All(corrected.FirstShell, e => Assert.Equal(Chebyshev(source, e.Key), e.Value));
  }

  // -----------------------------------------------------------------------------------------------
  // The shell count
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   <c>FindMaxDistance</c> folds over <c>v1.z</c> twice and never over <c>v1.x</c>, so the distance from
  ///   the source back to the box's −X edge cannot raise the shell count. Where that edge is the furthest
  ///   one, the walk stops before reaching it.
  /// </summary>
  [Fact]
  public void The_shipped_shell_count_ignores_the_distance_to_the_negative_X_edge() {
    // A source hard against the +X, +Y and +Z corner of its claim: every offset is small except the run
    // back to −X, which is exactly the component the fold drops.
    var toMin = new BlockPos(40, 3, 3);
    var fromMax = new BlockPos(1, 1, 1);

    var shipped = SortScanStrategies.RoboticInboxMaxDistanceAsShipped(toMin, fromMax);
    var corrected = SortScanStrategies.RoboticInboxMaxDistanceCorrected(toMin, fromMax);

    output.WriteLine($"offsets toMin={toMin} fromMax={fromMax}: shipped stops at shell {shipped}, " +
                     $"correct is {corrected} — {corrected - shipped} shells never run");

    Assert.Equal(40, corrected);
    Assert.Equal(3, shipped);
  }

  /// <summary>And the containers in those shells are simply never offered anything.</summary>
  [Fact]
  public void A_container_past_the_truncated_shells_is_never_probed() {
    var source = new BlockPos(1000, 60, 1000);
    var min = new BlockPos(source.X - 40, source.Y - 3, source.Z - 3);
    var max = new BlockPos(source.X + 1, source.Y + 1, source.Z + 1);

    SortScanTrace trace = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);

    output.WriteLine($"walk covered X from {trace.Visits.Keys.Min(p => p.X)} to " +
                     $"{trace.Visits.Keys.Max(p => p.X)}; box starts at {min.X}");

    Assert.False(trace.Visited(min.X, source.Y, source.Z));
    Assert.True(trace.Visited(source.X - 3, source.Y, source.Z));
  }

  // -----------------------------------------------------------------------------------------------
  // What each strategy costs
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   The headline cost comparison. A coordinate walk pays for volume; a chunk walk pays for the tile
  ///   entities that actually exist, and containers are sparse — which is why the two differ by orders of
  ///   magnitude rather than by a constant.
  /// </summary>
  [Fact]
  public void Coordinate_scans_cost_orders_of_magnitude_more_than_chunk_scans() {
    BlockPos source = Centre();
    (BlockPos min, BlockPos max) = ClaimBox(source);

    SortScanTrace inbox = SortScanStrategies.RoboticInboxShellAsShipped(source, min, max);
    var sorterPull = SortScanStrategies.RoboticSorterCubeProbeCount(SorterRadius);
    var sorterPush = SortScanStrategies.RoboticSorterCubeProbeCount(30);
    // Nine chunks, and a well-built base chunk holding 40 tile entities is already generous.
    var chunkScan = SortScanStrategies.ChunkTileEntityProbeCount(9, 40);
    var chunkVolume = SortScanStrategies.ChunkWindowBlockVolume(9);

    output.WriteLine($"RoboticInbox shell (range {InboxRange}, as shipped) : {inbox.Probes,10:N0} GetTileEntity calls");
    output.WriteLine($"RoboticSorter cube (radius {SorterRadius}, pull)     : {sorterPull,10:N0}");
    output.WriteLine($"RoboticSorter cube (radius 30, push path)      : {sorterPush,10:N0}");
    output.WriteLine($"Chunk tile-entity walk (9 chunks x 40 entities): {chunkScan,10:N0}");
    output.WriteLine($"  ...over a block volume of                    {chunkVolume,10:N0}");
    output.WriteLine($"  chunk walk is {(double)sorterPush / chunkScan:N0}x cheaper than the radius-30 cube");

    Assert.True(chunkScan * 100 < sorterPull);
    Assert.True(chunkScan * 100 < inbox.Probes);
  }

  /// <summary>
  ///   RoboticSorter's cube is triggered from <c>TileEntity.SetModified</c>, so its cost is paid per
  ///   container mutation rather than once per sort. The product is the number that matters.
  /// </summary>
  [Fact]
  public void The_cube_scan_cost_multiplies_by_how_often_its_hook_fires() {
    var perFire = SortScanStrategies.RoboticSorterCubeProbeCount(SorterRadius);

    foreach (var fires in new[] { 1, 10, 100 }) {
      output.WriteLine($"{fires,4} SetModified calls => {perFire * fires,14:N0} GetTileEntity calls");
    }

    Assert.Equal(226_980, perFire);
  }

  // -----------------------------------------------------------------------------------------------
  // StrongBoxes' chunk window against its own radius
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   StrongBoxes asks for a 3×3 chunk window and then filters candidates to 15 m. Those two numbers are
  ///   declared independently and happen to be compatible: the window's worst-case reach is 16 blocks, one
  ///   more than the radius. The margin is one block, and nothing in the code ties them together.
  /// </summary>
  [Fact]
  public void The_three_by_three_chunk_window_covers_the_fifteen_metre_radius_with_one_block_to_spare() {
    var reach = SortScanStrategies.ChunkWindowGuaranteedReach(1);

    output.WriteLine($"a 3x3 chunk window reaches at least {reach} blocks horizontally from any source; " +
                     "StrongBoxes filters at 15");

    Assert.Equal(16, reach);
    Assert.True(reach >= 15);
  }

  /// <summary>
  ///   Raise the radius past that reach and the window silently stops covering it — the scan returns fewer
  ///   containers with no error. A test that ties the two constants together is the cheap guard.
  /// </summary>
  [Theory]
  [InlineData(15, true)]
  [InlineData(16, true)]
  [InlineData(17, false)]
  [InlineData(24, false)]
  public void A_radius_past_the_window_reach_is_silently_unsatisfiable(int radius, bool covered) {
    Assert.Equal(covered, radius <= SortScanStrategies.ChunkWindowGuaranteedReach(1));
  }

  /// <summary>
  ///   The window is whole chunks, so where the source sits inside its chunk decides how lopsided the
  ///   coverage is. At a chunk edge the scan reaches 31 blocks one way and 16 the other — harmless while the
  ///   radius filter is the real bound, but it means the window is not a disc and cannot be reasoned about
  ///   as one.
  /// </summary>
  [Fact]
  public void The_chunk_window_reaches_twice_as_far_one_way_as_the_other() {
    var atEdge = new BlockPos(1024, 60, 1024);
    HashSet<(int X, int Z)> keys = SortScanStrategies.StrongBoxesChunkWindow(atEdge);

    var minX = keys.Min(k => k.X) * SortScanStrategies.ChunkSizeXZ;
    var maxX = keys.Max(k => k.X) * SortScanStrategies.ChunkSizeXZ + SortScanStrategies.ChunkSizeXZ - 1;

    output.WriteLine($"source at x={atEdge.X} (chunk-local {atEdge.X & 15}) gets X coverage {minX}..{maxX} " +
                     $"— {atEdge.X - minX} blocks one way, {maxX - atEdge.X} the other");

    Assert.Equal(9, keys.Count);
    Assert.Equal(16, atEdge.X - minX);
    Assert.Equal(31, maxX - atEdge.X);
  }

  // -----------------------------------------------------------------------------------------------
  // Helpers
  // -----------------------------------------------------------------------------------------------

  private static BlockPos Centre() => new(1000, 60, 1000);

  /// <summary>
  ///   The box RoboticInbox clamps to with siphoning protection on: the inbox range intersected with the
  ///   land claim. A default 41-block claim gives a radius of 20, which is also the configured range, so the
  ///   two coincide and the claim edge is the operative bound.
  /// </summary>
  private static (BlockPos Min, BlockPos Max) ClaimBox(BlockPos source) => (
    new BlockPos(source.X - InboxRange, source.Y - InboxRange, source.Z - InboxRange),
    new BlockPos(source.X + InboxRange, source.Y + InboxRange, source.Z + InboxRange));

  private static int Chebyshev(BlockPos a, BlockPos b) {
    var dx = a.X > b.X ? a.X - b.X : b.X - a.X;
    var dy = a.Y > b.Y ? a.Y - b.Y : b.Y - a.Y;
    var dz = a.Z > b.Z ? a.Z - b.Z : b.Z - a.Z;
    return dx > dy ? dx > dz ? dx : dz : dy > dz ? dy : dz;
  }

}

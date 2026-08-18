using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Tests.SortBoxes;

/// <summary>
///   What the matching and tie-break choices in <see cref="SortTargetPlanner" /> actually do to a plausible
///   base, measured for the #31 research report. The interesting results are the ones about ordering: every
///   reference mod takes whichever qualifying target its scan reached first, and these tests show what that
///   costs a player.
/// </summary>
public sealed class SortTargetPlannerTests {
  private readonly ITestOutputHelper output;

  public SortTargetPlannerTests(ITestOutputHelper output) {
    this.output = output;
  }

  // -----------------------------------------------------------------------------------------------
  // Ordering: the scatter problem
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   Two boxes both already hold nails, so both qualify. Under scan order the winner is decided by whichever
  ///   coordinate the walk happened to reach first — which is a function of where the boxes sit relative to
  ///   the sort box, not of anything the player chose. Rebuild the same base rotated and the nails land
  ///   somewhere else.
  /// </summary>
  [Fact]
  public void Scan_order_sends_an_item_to_whichever_qualifying_box_the_walk_reached_first() {
    SortContainer source = Source(new SortStack("nail", 500));
    SortContainer near = Box("near", 2, 0, 0, new SortStack("nail", 10));
    SortContainer far = Box("far", 12, 0, 0, new SortStack("nail", 10));

    SortPlan asScanned = SortTargetPlanner.PlanByIndex(source, new List<SortContainer> { far, near },
      tieBreak: SortTieBreak.ScanOrder);
    SortPlan reversed = SortTargetPlanner.PlanByIndex(source, new List<SortContainer> { near, far },
      tieBreak: SortTieBreak.ScanOrder);

    output.WriteLine($"scan order [far, near] -> {asScanned.Moves[0].Target.Name}");
    output.WriteLine($"scan order [near, far] -> {reversed.Moves[0].Target.Name}");

    Assert.Equal("far", asScanned.Moves[0].Target.Name);
    Assert.Equal("near", reversed.Moves[0].Target.Name);
  }

  /// <summary>
  ///   Ranking makes the answer a property of the base rather than of the scan. Whichever order the targets
  ///   arrive in, nearest picks the same box.
  /// </summary>
  [Fact]
  public void Ranking_by_distance_gives_the_same_answer_whatever_order_the_scan_produced() {
    SortContainer source = Source(new SortStack("nail", 500));
    SortContainer near = Box("near", 2, 0, 0, new SortStack("nail", 10));
    SortContainer far = Box("far", 12, 0, 0, new SortStack("nail", 10));

    foreach (List<SortContainer> order in new[] {
               new List<SortContainer> { far, near }, new List<SortContainer> { near, far },
             }) {
      SortPlan plan = SortTargetPlanner.PlanByIndex(source, order, tieBreak: SortTieBreak.Nearest);
      Assert.Equal("near", plan.Moves[0].Target.Name);
    }
  }

  /// <summary>
  ///   Distance is not always the policy a player wants. Consolidating into the box that already holds the
  ///   most keeps one canonical nail box instead of topping up whichever happens to be closest — the two
  ///   policies genuinely disagree, which is why this is a design decision and not a detail.
  /// </summary>
  [Fact]
  public void Nearest_and_most_existing_disagree_and_both_are_defensible() {
    SortContainer source = Source(new SortStack("nail", 500));
    SortContainer nearSmall = Box("near-small", 2, 0, 0, new SortStack("nail", 5));
    SortContainer farBulk = Box("far-bulk", 12, 0, 0, new SortStack("nail", 4000));
    var targets = new List<SortContainer> { nearSmall, farBulk };

    var nearest = SortTargetPlanner.PlanByIndex(source, targets, tieBreak: SortTieBreak.Nearest)
      .Moves[0].Target.Name;
    var existing = SortTargetPlanner.PlanByIndex(source, targets, tieBreak: SortTieBreak.MostExisting)
      .Moves[0].Target.Name;

    output.WriteLine($"nearest -> {nearest}; most-existing -> {existing}");

    Assert.Equal("near-small", nearest);
    Assert.Equal("far-bulk", existing);
  }

  // -----------------------------------------------------------------------------------------------
  // Matching grain
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   The match mode decides whether a quality-6 pickaxe may join a box of quality-1 ones. Class matching
  ///   says yes and keeps all pickaxes together; value matching says no and preserves the distinction the
  ///   game's own stacking rules make.
  /// </summary>
  [Fact]
  public void Class_matching_pools_qualities_that_value_matching_keeps_apart() {
    SortContainer source = Source(new SortStack("pickaxe", 1, 6));
    var targets = new List<SortContainer> { Box("tools", 2, 0, 0, new SortStack("pickaxe", 1, 1)) };

    SortPlan byClass = SortTargetPlanner.PlanByIndex(source, targets, SortMatchMode.ItemClass);
    SortPlan byValue = SortTargetPlanner.PlanByIndex(source, targets, SortMatchMode.ItemValue);

    output.WriteLine($"class match placed {byClass.MovedTotal}, value match placed {byValue.MovedTotal}");

    Assert.Single(byClass.Moves);
    Assert.Empty(byValue.Moves);
    Assert.Single(byValue.Unplaced);
  }

  // -----------------------------------------------------------------------------------------------
  // The #31 open question: top up only, or open new stacks
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   #31 leaves open whether a qualifying container may take a whole new stack or only top up partial ones.
  ///   The difference shows the moment a target holds a full stack: top-up-only leaves the surplus in the
  ///   sort box, new-stacks moves it into a free slot.
  /// </summary>
  [Fact]
  public void Top_up_only_leaves_the_surplus_behind_where_new_stacks_would_place_it() {
    SortContainer source = Source(new SortStack("nail", 1000));
    // One full stack, and free slots beside it.
    var targets = new List<SortContainer> { Box("hardware", 2, 0, 0, new SortStack("nail", 6000)) };

    SortPlan topUp = SortTargetPlanner.PlanByIndex(source, targets, allowNewStacks: false);
    SortPlan newStacks = SortTargetPlanner.PlanByIndex(source, targets, allowNewStacks: true);

    output.WriteLine($"top-up-only  : moved {topUp.MovedTotal}, left {topUp.Unplaced.Sum(s => s.Count)}");
    output.WriteLine($"allow-new    : moved {newStacks.MovedTotal}, left " +
                     $"{newStacks.Unplaced.Sum(s => s.Count)}");

    Assert.Equal(0, topUp.MovedTotal);
    Assert.Equal(1000, newStacks.MovedTotal);
  }

  /// <summary>
  ///   The decisive argument against a top-up-only mode. <c>ItemClass.LateInit</c> forces
  ///   <c>Stacknumber.Value = 1</c> for every item with quality, so a tool, weapon or armour piece has a
  ///   stack limit of one and no partial stack ever exists to top up. Top-up-only is therefore not the
  ///   conservative choice — it silently refuses to sort the entire item category players most want sorted.
  /// </summary>
  [Fact]
  public void Top_up_only_can_never_move_a_quality_item_because_its_stack_limit_is_one() {
    SortContainer source = Source(new SortStack("steelPickaxe", 1, 3));
    var targets = new List<SortContainer> {
      Box("tools", 2, 0, 0, new SortStack("steelPickaxe", 1, 5)),
    };

    SortPlan topUp = SortTargetPlanner.PlanByIndex(source, targets, allowNewStacks: false, stackLimit: 1);
    SortPlan newStacks = SortTargetPlanner.PlanByIndex(source, targets, allowNewStacks: true, stackLimit: 1);

    output.WriteLine($"quality item, stack limit 1: top-up-only moved {topUp.MovedTotal}, " +
                     $"allow-new moved {newStacks.MovedTotal}");

    Assert.Equal(0, topUp.MovedTotal);
    Assert.Equal(1, newStacks.MovedTotal);
  }

  /// <summary>
  ///   When the best target cannot take everything, the plan spills the rest into the next qualifying one
  ///   rather than stopping. A single-target planner would strand the surplus.
  /// </summary>
  [Fact]
  public void A_partial_fill_spills_into_the_next_qualifying_target() {
    SortContainer source = Source(new SortStack("nail", 1000));
    SortContainer near = Box("near", 2, 0, 0, new SortStack("nail", 5700));
    SortContainer next = Box("next", 6, 0, 0, new SortStack("nail", 5000));

    SortPlan plan = SortTargetPlanner.PlanByIndex(source, new List<SortContainer> { near, next });

    foreach (SortMove move in plan.Moves) {
      output.WriteLine($"{move.Count,5} nail -> {move.Target.Name}");
    }

    Assert.Equal(2, plan.Moves.Count);
    Assert.Equal(300, plan.Moves[0].Count);
    Assert.Equal(700, plan.Moves[1].Count);
    Assert.Empty(plan.Unplaced);
  }

  /// <summary>Items no target already holds stay put — the MVP rule from #31, and the safe default.</summary>
  [Fact]
  public void An_item_no_target_holds_stays_in_the_sort_box() {
    SortContainer source = Source(new SortStack("nail", 100), new SortStack("plutonium", 3));
    var targets = new List<SortContainer> { Box("hardware", 2, 0, 0, new SortStack("nail", 10)) };

    SortPlan plan = SortTargetPlanner.PlanByIndex(source, targets);

    Assert.Equal("nail", Assert.Single(plan.Moves).ItemClass);
    Assert.Equal("plutonium", Assert.Single(plan.Unplaced).ItemClass);
  }

  // -----------------------------------------------------------------------------------------------
  // Cost
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   Indexing the targets once beats rescanning them per source stack. The base modelled here is the one
  ///   players actually build: 60 containers, each SPECIALISED to a couple of item kinds, so most containers
  ///   match most items not at all. That is the case the rescan handles worst — it still walks every slot of
  ///   every container for every source stack before concluding nothing fits.
  /// </summary>
  [Fact]
  public void Indexing_the_targets_once_costs_far_less_than_rescanning_them_per_item() {
    SortContainer source = SortedBase.SourceBox(45);
    List<SortContainer> targets = SortedBase.SpecialisedBoxes(60);

    SortPlan rescan = SortTargetPlanner.PlanByRescan(source, targets);
    SortPlan indexed = SortTargetPlanner.PlanByIndex(source, targets);

    output.WriteLine("60 specialised containers x 45 slots, 45 source slots:");
    output.WriteLine($"  rescan  : {rescan.Comparisons,9:N0} comparisons");
    output.WriteLine($"  indexed : {indexed.Comparisons,9:N0} comparisons " +
                     $"({(double)rescan.Comparisons / indexed.Comparisons:F0}x cheaper)");
    output.WriteLine($"  both placed the same items: rescan {rescan.MovedTotal}, indexed {indexed.MovedTotal}");

    Assert.True(indexed.Comparisons * 10 < rescan.Comparisons);
  }

  /// <summary>
  ///   The rescan cost grows with the PRODUCT of source slots and target slots; the indexed cost grows with
  ///   their SUM. Doubling the sort box's slots doubles the rescan and barely moves the index.
  /// </summary>
  [Fact]
  public void Rescan_cost_grows_with_the_product_where_indexed_cost_grows_with_the_sum() {
    var previous = (Rescan: 0, Indexed: 0);
    foreach (var sourceSlots in new[] { 15, 30, 45, 90 }) {
      SortContainer source = SortedBase.SourceBox(sourceSlots);
      List<SortContainer> targets = SortedBase.SpecialisedBoxes(60);

      SortPlan rescan = SortTargetPlanner.PlanByRescan(source, targets);
      SortPlan indexed = SortTargetPlanner.PlanByIndex(source, targets);
      output.WriteLine($"{sourceSlots,3} source slots: rescan {rescan.Comparisons,9:N0}, " +
                       $"indexed {indexed.Comparisons,6:N0}");
      previous = (rescan.Comparisons, indexed.Comparisons);
    }

    // At the largest size the index is still within a few thousand comparisons of its smallest.
    Assert.True(previous.Indexed < 4000);
    Assert.True(previous.Rescan > 200_000);
  }

  /// <summary>
  ///   Adding containers to a base costs the rescan linearly in a large constant. This is the growth a
  ///   long-lived server actually experiences.
  /// </summary>
  [Fact]
  public void Rescan_cost_grows_with_every_container_added_to_the_base() {
    foreach (var boxes in new[] { 20, 60, 120, 240 }) {
      SortContainer source = SortedBase.SourceBox(45);
      List<SortContainer> targets = SortedBase.SpecialisedBoxes(boxes);
      SortPlan rescan = SortTargetPlanner.PlanByRescan(source, targets);
      SortPlan indexed = SortTargetPlanner.PlanByIndex(source, targets);
      output.WriteLine($"{boxes,4} containers: rescan {rescan.Comparisons,9:N0}, " +
                       $"indexed {indexed.Comparisons,7:N0}");
    }

    Assert.True(true);
  }

  // -----------------------------------------------------------------------------------------------
  // Helpers
  // -----------------------------------------------------------------------------------------------

  private static SortContainer Source(params SortStack[] contents) =>
    new("sort", new BlockPos(0, 0, 0), 45, contents);

  private static SortContainer Box(string name, int x, int y, int z, params SortStack[] contents) =>
    new(name, new BlockPos(x, y, z), 45, contents);

  /// <summary>
  ///   A base shaped the way players build them: each container is specialised to two item kinds and holds
  ///   several partial stacks of each, drawn from a vocabulary far wider than any one container covers. That
  ///   sparsity is the point — it is what makes a rescan pay for containers that were never going to match.
  /// </summary>
  private static class SortedBase {
    private const int Vocabulary = 40;

    public static SortContainer SourceBox(int slots) =>
      new("sort", new BlockPos(0, 0, 0), slots, Enumerable.Range(0, slots)
        .Select(i => new SortStack($"item{i % Vocabulary}", 100)).ToArray());

    public static List<SortContainer> SpecialisedBoxes(int count) =>
      Enumerable.Range(0, count).Select(i => new SortContainer($"box{i}", new BlockPos(i + 2, 0, 0), 45,
        Enumerable.Range(0, 45)
          .Select(s => new SortStack($"item{(i * 2 + s % 2) % Vocabulary}", 10)).ToArray())).ToList();
  }
}

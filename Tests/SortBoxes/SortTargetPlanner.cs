using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.SortBoxes;

/// <summary>
///   How two stacks are judged to be "the same item". The three reference mods each pick a different one of
///   these, and the choice is visible to players: it decides whether a worn steel pickaxe tops up the stack of
///   fresh ones.
///   <para>
///     <see cref="ItemClass" /> is RoboticInbox's comparison
///     (<c>targetContainer.items[j].itemValue.ItemClass == sourceContainer.items[i].itemValue.ItemClass</c>),
///     <see cref="ItemType" /> is RoboticSorter's (<c>itemValue.type</c>), and <see cref="ItemValue" /> is the
///     strictest — everything the game's own stacking would demand.
///   </para>
/// </summary>
public enum SortMatchMode {
  /// <summary>Same item class. A level-1 and a level-6 pickaxe match.</summary>
  ItemClass,

  /// <summary>Same item type id. In practice the same grain as <see cref="ItemClass" /> for plain items.</summary>
  ItemType,

  /// <summary>Same class AND same quality/variant — only genuinely interchangeable stacks match.</summary>
  ItemValue,
}

/// <summary>Which target wins when several would accept the same item.</summary>
public enum SortTieBreak {
  /// <summary>Whatever order the scan produced. What all three reference mods actually do.</summary>
  ScanOrder,

  /// <summary>Nearest container first, ties broken by position so the result is stable.</summary>
  Nearest,

  /// <summary>The container already holding the most of that item — consolidates rather than scatters.</summary>
  MostExisting,

  /// <summary>The container with the most free room — spreads load, delays overflow.</summary>
  MostFreeSpace,
}

/// <summary>One stack: an item class, a quality band, and a count.</summary>
public sealed class SortStack {
  public SortStack(string itemClass, int count, int quality = 0) {
    ItemClass = itemClass;
    Count = count;
    Quality = quality;
  }

  public string ItemClass { get; }

  public int Quality { get; }

  public int Count { get; set; }

  public bool IsEmpty => Count <= 0;

  public override string ToString() => $"{ItemClass}{(Quality == 0 ? "" : $"@q{Quality}")} x{Count}";
}

/// <summary>
///   A container the planner may move items into: a fixed number of slots, a per-item stack limit, a position
///   for distance ranking, and the sign text that any name-driven rule reads.
/// </summary>
public sealed class SortContainer {
  public SortContainer(string name, BlockPos position, int slots, IEnumerable<SortStack> contents = null) {
    Name = name;
    Position = position;
    Slots = new SortStack[slots];
    var i = 0;
    foreach (SortStack stack in contents ?? Enumerable.Empty<SortStack>()) {
      Slots[i++] = stack;
    }
  }

  public string Name { get; }

  public BlockPos Position { get; }

  public SortStack[] Slots { get; }

  public string SignText { get; set; } = "";

  public int CountOf(string itemClass) =>
    Slots.Where(s => s != null && s.ItemClass == itemClass).Sum(s => s.Count);

  public bool Holds(string itemClass) => Slots.Any(s => s != null && s.ItemClass == itemClass);

  public int FreeSlots => Slots.Count(s => s == null || s.IsEmpty);
}

/// <summary>One planned movement of items from the sort box into a target.</summary>
public sealed record SortMove(string ItemClass, int Count, SortContainer Target, string Reason);

/// <summary>What a planning run produced, including the work it took to produce it.</summary>
public sealed class SortPlan {
  public List<SortMove> Moves { get; } = new();

  /// <summary>Item-to-item comparisons performed. The cost measure the complexity tests read.</summary>
  public int Comparisons { get; set; }

  /// <summary>Items that matched nothing and stay in the sort box.</summary>
  public List<SortStack> Unplaced { get; } = new();

  public int MovedTotal => Moves.Sum(m => m.Count);

  public IEnumerable<SortContainer> TargetsUsed => Moves.Select(m => m.Target).Distinct();
}

/// <summary>
///   A prototype sort planner for #31, written to make the design choices in the research report measurable
///   rather than arguable. It is deliberately a PLANNER: it decides what should move and returns a list of
///   moves, touching no container state. That split is the recommendation the report argues for — the
///   expensive matching work happens against a snapshot, and only the short application step needs the
///   containers to still be there and unlocked.
///   <para>
///     Not shipping code, and not in StrongBoxes. It models item stacks with plain strings and ints so it can
///     run with no game tree at all.
///   </para>
/// </summary>
public static class SortTargetPlanner {
  /// <summary>
  ///   The shape the reference mods use, modelled on RoboticInbox's <c>Distribute</c>: the outer walk visits
  ///   every target in turn, and for each one re-walks every source slot against every target slot. Nothing
  ///   is remembered between targets, so a source stack is compared against the whole of every container.
  ///   <para>
  ///     Two properties of the original are kept because they dominate the cost. There is no early exit when
  ///     the source empties — RoboticInbox keeps visiting containers regardless (ServerTools alone checks, and
  ///     only once per chunk) — and the inner slot loop runs to the end of the target rather than stopping at
  ///     the first match, because it is also looking for further partial stacks to top up.
  ///   </para>
  ///   Cost is therefore |targets| x |source slots| x |target slots|, paid in full on every sort.
  /// </summary>
  public static SortPlan PlanByRescan(SortContainer source, IReadOnlyList<SortContainer> targets,
    SortMatchMode mode = SortMatchMode.ItemClass) {
    var plan = new SortPlan();
    // A sort box routinely holds several stacks of one item, so the outstanding count is summed per key
    // rather than keyed per slot.
    var remaining = new Dictionary<string, int>();
    foreach (SortStack stack in source.Slots.Where(s => s is { IsEmpty: false })) {
      var key = Key(stack, mode);
      remaining[key] = remaining.TryGetValue(key, out var held) ? held + stack.Count : stack.Count;
    }

    foreach (SortContainer target in targets) {
      foreach (SortStack stack in source.Slots.Where(s => s is { IsEmpty: false })) {
        foreach (SortStack slot in target.Slots) {
          plan.Comparisons++;
          if (slot == null || slot.IsEmpty || !Matches(slot, stack, mode)) {
            continue;
          }

          var key = Key(stack, mode);
          if (remaining.TryGetValue(key, out var left) && left > 0) {
            plan.Moves.Add(new SortMove(stack.ItemClass, left, target, "rescan found a matching slot"));
            remaining[key] = 0;
          }
        }
      }
    }

    foreach (SortStack stack in source.Slots.Where(s => s is { IsEmpty: false })) {
      if (remaining.TryGetValue(Key(stack, mode), out var left) && left > 0) {
        plan.Unplaced.Add(new SortStack(stack.ItemClass, left, stack.Quality));
        remaining[Key(stack, mode)] = 0;
      }
    }

    return plan;
  }

  /// <summary>
  ///   Index every target's contents once, then answer each source stack from the index. Cost becomes
  ///   |targets| x |target slots| to build, plus |source slots| lookups — the target side is walked once
  ///   instead of once per source stack.
  ///   <para>
  ///     The index is also what makes a real tie-break affordable: every candidate for an item is in hand at
  ///     once, so the planner can rank them instead of taking whichever the scan reached first.
  ///   </para>
  /// </summary>
  public static SortPlan PlanByIndex(SortContainer source, IReadOnlyList<SortContainer> targets,
    SortMatchMode mode = SortMatchMode.ItemClass, SortTieBreak tieBreak = SortTieBreak.Nearest,
    bool allowNewStacks = false, int stackLimit = 6000) {
    var plan = new SortPlan();
    Dictionary<string, List<SortContainer>> index = BuildIndex(targets, mode, plan);

    foreach (SortStack stack in source.Slots.Where(s => s is { IsEmpty: false })) {
      var remaining = stack.Count;
      if (!index.TryGetValue(Key(stack, mode), out List<SortContainer> candidates)) {
        plan.Unplaced.Add(new SortStack(stack.ItemClass, remaining, stack.Quality));
        continue;
      }

      foreach (SortContainer target in Rank(candidates, source, stack, tieBreak)) {
        plan.Comparisons++;
        var room = Room(target, stack, allowNewStacks, stackLimit);
        if (room <= 0) {
          continue;
        }

        var moving = room < remaining ? room : remaining;
        plan.Moves.Add(new SortMove(stack.ItemClass, moving, target, $"tie-break {tieBreak}"));
        remaining -= moving;
        if (remaining == 0) {
          break;
        }
      }

      if (remaining > 0) {
        plan.Unplaced.Add(new SortStack(stack.ItemClass, remaining, stack.Quality));
      }
    }

    return plan;
  }

  private static Dictionary<string, List<SortContainer>> BuildIndex(IReadOnlyList<SortContainer> targets,
    SortMatchMode mode, SortPlan plan) {
    var index = new Dictionary<string, List<SortContainer>>();
    foreach (SortContainer target in targets) {
      var seen = new HashSet<string>();
      foreach (SortStack slot in target.Slots) {
        plan.Comparisons++;
        if (slot == null || slot.IsEmpty || !seen.Add(Key(slot, mode))) {
          continue;
        }

        var key = Key(slot, mode);
        if (!index.TryGetValue(key, out List<SortContainer> list)) {
          index[key] = list = new List<SortContainer>();
        }

        list.Add(target);
      }
    }

    return index;
  }

  private static IEnumerable<SortContainer> Rank(List<SortContainer> candidates, SortContainer source,
    SortStack stack, SortTieBreak tieBreak) =>
    tieBreak switch {
      SortTieBreak.ScanOrder => candidates,
      SortTieBreak.Nearest => candidates
        .OrderBy(c => DistanceSquared(source.Position, c.Position))
        .ThenBy(c => c.Position.X).ThenBy(c => c.Position.Y).ThenBy(c => c.Position.Z),
      SortTieBreak.MostExisting => candidates
        .OrderByDescending(c => c.CountOf(stack.ItemClass))
        .ThenBy(c => DistanceSquared(source.Position, c.Position)),
      SortTieBreak.MostFreeSpace => candidates
        .OrderByDescending(c => c.FreeSlots)
        .ThenBy(c => DistanceSquared(source.Position, c.Position)),
      _ => candidates,
    };

  /// <summary>
  ///   How much of <paramref name="stack" /> the target can take. With
  ///   <paramref name="allowNewStacks" /> false this is the #31 MVP reading — top up existing partial stacks
  ///   only, never open a fresh slot — which is strictly more conservative and never grows a container's
  ///   slot footprint.
  /// </summary>
  private static int Room(SortContainer target, SortStack stack, bool allowNewStacks, int stackLimit) {
    var room = target.Slots
      .Where(s => s is { IsEmpty: false } && s.ItemClass == stack.ItemClass)
      .Sum(s => stackLimit - s.Count);
    if (allowNewStacks) {
      room += target.FreeSlots * stackLimit;
    }

    return room;
  }

  private static bool Matches(SortStack a, SortStack b, SortMatchMode mode) => Key(a, mode) == Key(b, mode);

  private static string Key(SortStack stack, SortMatchMode mode) =>
    mode == SortMatchMode.ItemValue ? $"{stack.ItemClass}@{stack.Quality}" : stack.ItemClass;

  private static long DistanceSquared(BlockPos a, BlockPos b) {
    long dx = a.X - b.X;
    long dy = a.Y - b.Y;
    long dz = a.Z - b.Z;
    return dx * dx + dy * dy + dz * dz;
  }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.Patcher;

/// <summary>
///   Cross-reference integrity of the MERGED config — vanilla with every mod's patches applied (#50 gap S3).
///   The existing replay (<see cref="PatchApplicationTests" />) asserts that a patch <em>applies</em>; this
///   asserts that what it produced still hangs together: that every <c>Extends</c>, <c>Next</c>,
///   <c>DowngradeBlock</c>, recipe output, ingredient, loot group, loot drop and triggered buff resolves to
///   something the merged document actually defines.
///   <para>
///     <b>Differential, not absolute.</b> Vanilla itself ships references that resolve to nothing — ten of
///     them in V3.1.0 b14, all in buff effects. Those are the game's, and asserting against zero would mean
///     carrying a permanent exemption list that says nothing about this repo. So the subject is the
///     <em>difference</em>: a mod must not introduce a dangling reference vanilla did not already have. That
///     also catches the other direction, where a mod REMOVES something vanilla still points at.
///   </para>
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class ReferenceIntegrityTests {
  /// <summary>
  ///   Names a mod references that nothing in the merged config defines, and why they are tolerated. Keyed
  ///   by the referenced NAME. Same contract as <see cref="PatchApplicationTests.ExpectedToLog" />: every
  ///   entry is a decision on record, and the list is checked in both directions, so an entry that stops
  ///   dangling fails too rather than rotting here.
  /// </summary>
  private static readonly Dictionary<string, string> KnownDangling = new() {
    // StrongholdTweaks/Config/recipes.xml appends twelve "Strong crop" recipes whose seed ingredient is
    // planted<Crop>1Sel2. No vanilla block or item carries a "Sel<n>" suffix in EITHER declared version —
    // the convention does not exist in the game's own naming — and no mod in this repo defines one. The
    // recipes sit inside <conditional><if cond="mod_loaded('PootPavillion') and (mod_loaded('ProjectZ') or
    // mod_loaded('Z_Bosses'))">, so the only remaining way for them to resolve is that the FOREIGN mod
    // ProjectZ / Z_Bosses defines these blocks; that is unverifiable here (= #61, the foreign-mod base
    // fixture). Declared rather than fixed, because deciding needs the foreign mod in hand.
    ["plantedMushroom1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedAloe1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedYucca1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedPotato1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedCorn1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedGoldenrod1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedCotton1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedChrysanthemum1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedCoffee1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedBlueberry1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedHop1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61",
    ["plantedPumpkin1Sel2"] = "StrongholdTweaks Strong-crop recipe; see #61"
  };

  private static readonly ConcurrentDictionary<string, Lazy<Comparison>> Comparisons = new();

  public static IEnumerable<object[]> Labels() =>
    SmokeTestCtx.Labels.Value.Select(label => new object[] { label });

  [Theory]
  [MemberData(nameof(Labels))]
  public void No_mod_introduces_a_dangling_reference(string label) {
    List<string> introduced = Introduced(For(label))
      .Where(r => !KnownDangling.ContainsKey(r.Target))
      .Select(r => r.ToString()).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

    Assert.True(introduced.Count == 0,
      $"Against {label}, the merged config has {introduced.Count} reference(s) that resolve to nothing and " +
      "that vanilla did not already have. Either the name is a typo, or the thing it names is never " +
      "defined. Fix it, or declare it in KnownDangling with the reason:\n  " +
      string.Join("\n  ", introduced.Take(40)));
  }

  /// <summary>
  ///   The other direction: a declared exception that no longer dangles is a stale declaration. Without
  ///   this, KnownDangling would quietly accumulate names that were fixed years ago.
  /// </summary>
  [Theory]
  [MemberData(nameof(Labels))]
  public void Declared_dangling_names_are_still_dangling(string label) {
    var stillDangling = new HashSet<string>(Introduced(For(label)).Select(r => r.Target), StringComparer.Ordinal);
    List<string> resolved = KnownDangling.Keys.Where(k => !stillDangling.Contains(k))
      .OrderBy(k => k, StringComparer.Ordinal).ToList();

    Assert.True(resolved.Count == 0,
      $"Against {label}, these names in KnownDangling now resolve (or are no longer referenced). Remove " +
      "them, so the list keeps meaning something:\n  " + string.Join("\n  ", resolved));
  }

  /// <summary>
  ///   The rules are only worth anything if they see references at all. A game update that renames an
  ///   element would otherwise silently reduce this suite to asserting nothing, and it would still pass.
  /// </summary>
  [Theory]
  [MemberData(nameof(Labels))]
  public void The_rules_still_find_references_to_check(string label) {
    Comparison comparison = For(label);

    Assert.True(comparison.MergedTotal > 5000,
      $"Against {label} only {comparison.MergedTotal} references were collected. The rules in " +
      "ReferenceGraph key on element and property names; if a game update renamed one, this suite would " +
      "pass by checking nothing.");
  }

  /// <summary>The dangling references the mods added — merged minus vanilla's own.</summary>
  private static IEnumerable<XmlReference> Introduced(Comparison comparison) =>
    comparison.Merged.Where(r => !comparison.VanillaBaseline.Contains(r.ToString()));

  private static Comparison For(string label) => Comparisons.GetOrAdd(label, l => new Lazy<Comparison>(() => {
    PatcherHost host = SmokeTestCtx.TreeIsDeclared ? PatcherHost.ForLabel(l) : PatcherHost.Instance.Value;
    PatchPipeline pipeline = SmokeTestCtx.TreeIsDeclared
      ? PatchPipeline.Run(host, l)
      : PatchPipeline.Run(host);
    ReferenceGraph merged = ReferenceGraph.OfMerged(host, pipeline);
    ReferenceGraph vanilla = ReferenceGraph.OfVanilla(host, pipeline.EntryPoints);
    return new Comparison(
      merged.Dangling(),
      new HashSet<string>(vanilla.Dangling().Select(r => r.ToString()), StringComparer.Ordinal),
      merged.Total);
  })).Value;

  private sealed record Comparison(IReadOnlyList<XmlReference> Merged, HashSet<string> VanillaBaseline,
                                   int MergedTotal);
}

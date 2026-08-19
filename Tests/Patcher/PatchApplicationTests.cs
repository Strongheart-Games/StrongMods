using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.Patcher;

/// <summary>
///   Every mod's real <c>Config\</c> patches, applied to the unit's real vanilla XML (#43 wave D). The default
///   expectation is that a patch applies with no error or warning; anything else must be <b>declared below
///   with a reason</b>, so "this mod warns" is a decision on record rather than noise someone learns to
///   ignore.
///   The declarations are checked in both directions: an undeclared patch that warns fails, and a declared one
///   that has gone quiet fails too. The second half is what keeps the list from rotting, and it has already paid
///   for itself once — the two StrongholdTweaks entries here were retired by #60, and this suite is what would
///   have failed had the conversion been forgotten.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class PatchApplicationTests {
  /// <summary>
  ///   Patches that legitimately log, and why. Keyed <c>Mod/entry-point</c>.
  /// </summary>
  private static readonly Dictionary<string, string> ExpectedToLog = new() {
    // Patch another mod's content, so on vanilla their xpaths match nothing. Expected; see #61 for testing
    // them against the mods they actually target.
    ["AECInternationalMarketFixes/gameevents"] = "targets AEC International Market's content, absent from vanilla",
    ["AECVehiclesFixes/vehicles"] = "targets AEC Vehicles' content, absent from vanilla",
    ["ProjectZFixes/gameevents"] = "targets Project Z's content, absent from vanilla",
    ["ProjectZFixes/items"] = "targets Project Z's content, absent from vanilla",
    ["ProjectZFixes/item_modifiers"] = "targets Project Z's content, absent from vanilla",
    ["ProjectZFixes/recipes"] = "targets Project Z's content, absent from vanilla",
  };

  /// <summary>
  ///   Files under a mod's <c>Config\</c> that the game never opens. Should be empty; every entry is a bug
  ///   with an issue, not an exemption.
  /// </summary>
  private static readonly Dictionary<string, string> ExpectedDead = new() {
  };

  // One replay per declared version label (#23 phase 6b): each mod's Config against every vanilla it
  // declares support for — vanilla XML differs per version, which is exactly why per-version assertion is
  // worth having. The memoized runs moved to Fixtures\PipelineRuns.cs when PatchValueAssertionTests became
  // the second class asserting over the same replay.
  public static IEnumerable<object[]> Labels() =>
    SmokeTestCtx.Labels.Value.Select(label => new object[] { label });

  private static PatchPipeline Pipeline(string label) => PipelineRuns.For(label);

  [Theory]
  [MemberData(nameof(Labels))]
  public void Every_patch_applies_without_error_or_warning_unless_declared(string label) {
    List<string> undeclared = Pipeline(label).Applications
      .Where(a => !a.Clean && !ExpectedToLog.ContainsKey(a.Key))
      .Select(a => $"{a.Key}: {a.Summary}").ToList();

    Assert.True(undeclared.Count == 0,
      $"These patches logged an error or warning against {label} vanilla and are not declared in " +
      "ExpectedToLog.\nEither fix the patch, or add it there with the reason it is expected:\n  " +
      string.Join("\n  ", undeclared));
  }

  [Theory]
  [MemberData(nameof(Labels))]
  public void Declared_exceptions_are_still_needed(string label) {
    List<PatchApplication> attempted = Pipeline(label).Applications
      .Where(a => ExpectedToLog.ContainsKey(a.Key)).ToList();

    List<string> nowClean = attempted.Where(a => a.Clean).Select(a => $"{a.Key} — {ExpectedToLog[a.Key]}").ToList();
    Assert.True(nowClean.Count == 0,
      $"These are declared in ExpectedToLog but apply cleanly against {label}. If they are clean against " +
      "EVERY declared version, remove the declaration:\n  " + string.Join("\n  ", nowClean));

    // Stale = the mod DID replay at this label, yet no application carries the declared key. A mod pinned
    // away from this label is judged by the labels it does declare, never here.
    List<string> vanished = ExpectedToLog.Keys
      .Where(k => Pipeline(label).Applications.All(a => a.Key != k) && ModReplayed(label, Mod(k))).ToList();
    Assert.True(vanished.Count == 0,
      $"These are declared in ExpectedToLog but no such patch was applied against {label} — the mod or entry " +
      "point was renamed or removed, so the declaration is stale:\n  " + string.Join("\n  ", vanished));
  }

  [Theory]
  [MemberData(nameof(Labels))]
  public void No_patch_file_is_silently_dead_unless_declared(string label) {
    // A file at a path matching no entry point is never opened and never warns — invisible until someone
    // notices the change did not happen (#62).
    List<string> undeclared = Pipeline(label).DeadFiles
      .Where(d => !ExpectedDead.ContainsKey(d.Key)).Select(d => d.Key).ToList();

    Assert.True(undeclared.Count == 0,
      $"These files sit under a mod's Config\\ but match no {label} entry point and no <include> references " +
      "them, so the game will never apply them:\n  " + string.Join("\n  ", undeclared));

    List<string> fixedUp = ExpectedDead.Keys
      .Where(k => Pipeline(label).DeadFiles.All(d => d.Key != k)).ToList();
    Assert.True(fixedUp.Count == 0,
      "These are declared dead but are now reachable. Remove the declaration:\n  " +
      string.Join("\n  ", fixedUp));
  }

  [Theory]
  [MemberData(nameof(Labels))]
  public void The_pipeline_actually_ran(string label) {
    // Guards the whole class against going vacuously green: entry points come from the unit's IL, and the
    // repo demonstrably ships patches for them.
    Assert.True(Pipeline(label).EntryPoints.Count > 40,
      $"only {Pipeline(label).EntryPoints.Count} entry points read from {label}'s IL");
    Assert.True(Pipeline(label).Applications.Count > 30,
      $"only {Pipeline(label).Applications.Count} patches applied against {label}");
    Assert.True(Pipeline(label).Applications.Count(a => a.Clean) > 30,
      $"only {Pipeline(label).Applications.Count(a => a.Clean)} patches applied cleanly against {label}");
  }

  private static readonly string RepoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));

  private static readonly Lazy<GameVersionDeclarations> Declarations =
    new(() => GameVersionDeclarations.Load(RepoRoot));

  /// <summary>The mod half of an ExpectedToLog key ("Mod/entry-point").</summary>
  private static string Mod(string key) => key.Split('/')[0];

  /// <summary>Whether this label's replay included the mod: the escape-hatch run filters nothing, otherwise
  ///   the project's declaration decides (a dir with no csproj is always replayed, mirroring the pipeline).</summary>
  private static bool ModReplayed(string label, string mod) {
    if (!SmokeTestCtx.TreeIsDeclared) {
      return true;
    }

    var csproj = Path.Combine(RepoRoot, mod, mod + ".csproj");
    return !File.Exists(csproj) || Declarations.Value.For(csproj).Test.Contains(label);
  }
}

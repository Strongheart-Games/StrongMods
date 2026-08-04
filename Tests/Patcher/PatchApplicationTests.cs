using System;
using System.Collections.Generic;
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
///   that has gone quiet fails too. The second half is what keeps the list from rotting — when #60 lands and
///   the ensure-idiom warnings disappear, these tests will say so.
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

    // The paired setattribute+append idiom: exactly one command matches by design and the other reports
    // "did not apply". Goes away when <ensure> lands (#60).
    ["StrongholdTweaks/blocks"] = "paired setattribute+append for AllowedRotations; one always misses (#60)",
    ["StrongholdTweaks/items"] = "paired setattribute+append for AltItemTypeIconColor; one always misses (#60)",

    // A documented foreach skip, working exactly as foreach.md says it should.
    ["AutoCollectLoot/items"] = "vanilla twitch_crate_template has no Mesh property, so that iteration skips",
  };

  /// <summary>
  ///   Files under a mod's <c>Config\</c> that the game never opens. Should be empty; every entry is a bug
  ///   with an issue, not an exemption.
  /// </summary>
  private static readonly Dictionary<string, string> ExpectedDead = new() {
  };

  private static readonly Lazy<PatchPipeline> Pipeline = new(() => PatchPipeline.Run(PatcherHost.Instance.Value));

  [Fact]
  public void Every_patch_applies_without_error_or_warning_unless_declared() {
    List<string> undeclared = Pipeline.Value.Applications
      .Where(a => !a.Clean && !ExpectedToLog.ContainsKey(a.Key))
      .Select(a => $"{a.Key}: {a.Summary}").ToList();

    Assert.True(undeclared.Count == 0,
      "These patches logged an error or warning against vanilla and are not declared in ExpectedToLog.\n" +
      "Either fix the patch, or add it there with the reason it is expected:\n  " +
      string.Join("\n  ", undeclared));
  }

  [Fact]
  public void Declared_exceptions_are_still_needed() {
    List<PatchApplication> attempted = Pipeline.Value.Applications
      .Where(a => ExpectedToLog.ContainsKey(a.Key)).ToList();

    List<string> nowClean = attempted.Where(a => a.Clean).Select(a => $"{a.Key} — {ExpectedToLog[a.Key]}").ToList();
    Assert.True(nowClean.Count == 0,
      "These are declared in ExpectedToLog but now apply cleanly. Remove the declaration:\n  " +
      string.Join("\n  ", nowClean));

    List<string> vanished = ExpectedToLog.Keys
      .Where(k => Pipeline.Value.Applications.All(a => a.Key != k)).ToList();
    Assert.True(vanished.Count == 0,
      "These are declared in ExpectedToLog but no such patch was applied — the mod or entry point was renamed " +
      "or removed, so the declaration is stale:\n  " + string.Join("\n  ", vanished));
  }

  [Fact]
  public void No_patch_file_is_silently_dead_unless_declared() {
    // A file at a path matching no entry point is never opened and never warns — invisible until someone
    // notices the change did not happen (#62).
    List<string> undeclared = Pipeline.Value.DeadFiles
      .Where(d => !ExpectedDead.ContainsKey(d.Key)).Select(d => d.Key).ToList();

    Assert.True(undeclared.Count == 0,
      "These files sit under a mod's Config\\ but match no entry point and no <include> references them, so " +
      "the game will never apply them:\n  " + string.Join("\n  ", undeclared));

    List<string> fixedUp = ExpectedDead.Keys
      .Where(k => Pipeline.Value.DeadFiles.All(d => d.Key != k)).ToList();
    Assert.True(fixedUp.Count == 0,
      "These are declared dead but are now reachable. Remove the declaration:\n  " +
      string.Join("\n  ", fixedUp));
  }

  [Fact]
  public void The_pipeline_actually_ran() {
    // Guards the whole class against going vacuously green: entry points come from the unit's IL, and the
    // repo demonstrably ships patches for them.
    Assert.True(Pipeline.Value.EntryPoints.Count > 40,
      $"only {Pipeline.Value.EntryPoints.Count} entry points read from the unit's IL");
    Assert.True(Pipeline.Value.Applications.Count > 30,
      $"only {Pipeline.Value.Applications.Count} patches applied");
    Assert.True(Pipeline.Value.Applications.Count(a => a.Clean) > 30,
      $"only {Pipeline.Value.Applications.Count(a => a.Clean)} patches applied cleanly");
  }
}

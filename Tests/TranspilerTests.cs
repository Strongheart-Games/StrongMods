using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Xunit;

namespace Tests;

/// <summary>
///   Every transpiler in the repo, run against the real IL of the method it patches, on every game version
///   its mod declares (#50 gap S1). See <see cref="TranspilerVerification" /> for why running them is the
///   check.
/// </summary>
public class TranspilerTests {
  /// <summary>
  ///   The smallest transpiler count the repo is known to have. A rename or a refactor that made the
  ///   discovery find nothing would otherwise leave this suite green while checking nothing — the same
  ///   failure mode <see cref="SmokeTests.Coverage_sanity" /> guards for patch classes.
  /// </summary>
  private const int KnownTranspilerCount = 13;

  public static IEnumerable<object[]> Labels() =>
    SmokeTestCtx.Labels.Value.Select(label => new object[] { label });

  [Theory]
  [MemberData(nameof(Labels))]
  public void Every_transpiler_still_finds_its_match_point(string label) {
    List<TranspilerRun> failures = Runs(label).Where(r => !r.Changed).ToList();

    Assert.True(failures.Count == 0,
      $"Against {label}, {failures.Count} transpiler(s) did not transform their target. The method still " +
      "exists — SmokeTests would have failed otherwise — so what moved is the instruction sequence inside " +
      "it:\n  " + string.Join("\n  ", failures.Select(f => f.Describe())));
  }

  [Theory]
  [MemberData(nameof(Labels))]
  public void The_transpilers_are_all_found(string label) {
    var found = Runs(label).Count;

    Assert.True(found >= KnownTranspilerCount,
      $"Only {found} transpiler(s) were discovered against {label}, below the known {KnownTranspilerCount}. " +
      "Either a mod dropped one — then lower the constant — or the discovery in TranspilerVerification " +
      "broke and this suite is asserting nothing.");
  }

  /// <summary>
  ///   The suite's own proof that it can fail. A transpiler that looks for a call the target does not make
  ///   must be reported, not passed over — without this, a harness that quietly stopped running anything
  ///   would look identical to a healthy one.
  /// </summary>
  [Fact]
  public void Negative_control_a_transpiler_that_cannot_match_is_reported() {
    TranspilerRun run = Assert.Single(TranspilerVerification.Run("negative-control", typeof(BogusTranspiler)));

    Assert.False(run.Changed);
    Assert.Contains("changed nothing", run.Failure);
  }

  /// <summary>
  ///   A transpiler that hands its input straight back. That is what a real transpiler looks like when its
  ///   match point has moved and it is not strict enough to throw, so it is the case the harness most needs
  ///   to catch.
  /// </summary>
  [HarmonyPatch(typeof(ConnectionManagerShim), nameof(ConnectionManagerShim.Untouched))]
  private static class BogusTranspiler {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      return instructions;
    }
  }

  /// <summary>A local target, so the control does not depend on any particular game method surviving.</summary>
  private static class ConnectionManagerShim {
    public static int Untouched() => 41 + 1;
  }

  private static List<TranspilerRun> Runs(string label) {
    SmokeTestCtx.LoadedLabel loaded = SmokeTestCtx.For(label);
    return loaded.PatchClasses
      .SelectMany(pc => TranspilerVerification.Run(pc.Mod, pc.Type))
      .ToList();
  }
}

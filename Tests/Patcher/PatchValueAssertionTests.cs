using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Tests.Fixtures;
using Xunit;

namespace Tests.Patcher;

/// <summary>
///   Post-patch VALUE assertions (#50 gap S5). The replay in <c>PatchApplicationTests</c> proves every patch
///   applies without error; nothing there proves a patch did what it was written to do — a <c>set</c> whose
///   xpath matches but whose value was mistyped applies "cleanly" and ships the wrong balance. Each row here
///   states: after the whole pipeline runs, this xpath in the merged entry-point document selects exactly the
///   expected value(s). The row table is the reusable pattern — any mod joins by adding rows.
///   Rows run per declared version label; a row whose mod is pinned away from a label is skipped there, the
///   same way the pipeline itself skips the mod.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class PatchValueAssertionTests {
  /// <summary>
  ///   One expected post-patch value: in <paramref name="EntryPoint" />'s merged document,
  ///   <paramref name="XPath" /> (attribute- or element-selecting) must select at least one node and every
  ///   selected node's value must equal <paramref name="Expected" />. <paramref name="Mod" /> is the project
  ///   whose patch establishes the value — it scopes the row to the labels that mod declares.
  /// </summary>
  public sealed record ExpectedValue(string Mod, string EntryPoint, string XPath, string Expected);

  /// <summary>
  ///   The rows. Citations point at the patch line that writes each value, so a changed balance decision
  ///   knows exactly which row to update.
  /// </summary>
  private static readonly ExpectedValue[] Rows = {
    // StrongholdTweaks\Config\entityclasses.xml lines 3–4: player health and stamina doubled.
    new("StrongholdTweaks", "entityclasses",
      "/entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='HealthMax']/@value",
      "200"),
    new("StrongholdTweaks", "entityclasses",
      "/entity_classes/entity_class[@name='playerMale']/effect_group/passive_effect[@name='StaminaMax']/@value",
      "200"),
    // StrongholdTweaks\Config\progression.xml lines 4–5: lockpicking minigame effectively bypassed.
    new("StrongholdTweaks", "progression",
      "/progression/perks/perk[@name='perkLockPicking']/effect_group/passive_effect[@name='LockPickTime']/@value",
      ".25,.75"),
    new("StrongholdTweaks", "progression",
      "/progression/perks/perk[@name='perkLockPicking']/effect_group/passive_effect[@name='LockPickBreakChance']/@value",
      ".267,.8"),
    // StrongholdTweaks\Config\events.xml lines 8–9: Thanksgiving runs all of November.
    new("StrongholdTweaks", "events",
      "/events/event[@name='thanksgiving']/@start_date", "11/01"),
    new("StrongholdTweaks", "events",
      "/events/event[@name='thanksgiving']/@duration", "30"),
  };

  public static IEnumerable<object[]> Labels() =>
    SmokeTestCtx.Labels.Value.Select(label => new object[] { label });

  [Theory]
  [MemberData(nameof(Labels))]
  public void Every_expected_value_is_present_in_the_merged_documents(string label) {
    PatchPipeline pipeline = PipelineRuns.For(label);
    PatcherHost host = PipelineRuns.HostFor(label);
    var failures = new List<string>();

    foreach (ExpectedValue row in Rows.Where(r => ModReplayed(label, r.Mod))) {
      if (!pipeline.Documents.TryGetValue(row.EntryPoint, out object document)) {
        failures.Add($"{row.Mod}/{row.EntryPoint}: no merged document — the entry point vanished from {label}");
        continue;
      }

      List<string> values = Select(XDocument.Parse(host.XmlOf(document)), row.XPath);
      if (values.Count == 0) {
        failures.Add($"{row.Mod}/{row.EntryPoint}: '{row.XPath}' selected nothing against {label}");
      } else if (values.Any(v => v != row.Expected)) {
        failures.Add($"{row.Mod}/{row.EntryPoint}: '{row.XPath}' → [{string.Join(", ", values)}], " +
                     $"expected every match to be '{row.Expected}'");
      }
    }

    Assert.True(failures.Count == 0,
      $"Post-patch values missing or wrong against {label} vanilla:\n  " + string.Join("\n  ", failures));
  }

  [Theory]
  [MemberData(nameof(Labels))]
  public void The_value_rows_actually_ran(string label) {
    // Guards against the class going vacuously green: if every row's mod were pinned away from every label
    // (or a rename orphaned them all), the theory above would pass having asserted nothing.
    Assert.True(Rows.Any(r => ModReplayed(label, r.Mod)) || SmokeTestCtx.Labels.Value.Any(
        other => other != label && Rows.Any(r => ModReplayed(other, r.Mod))),
      $"No value row's mod replayed against {label} or any other declared label — the table is orphaned.");
  }

  /// <summary>Every value the xpath selects, as strings — attributes and elements both.</summary>
  private static List<string> Select(XDocument document, string xpath) =>
    ((IEnumerable)document.XPathEvaluate(xpath)).Cast<object>()
    .Select(node => node switch {
      XAttribute attribute => attribute.Value,
      XElement element => element.Value,
      _ => node?.ToString() ?? "",
    }).ToList();

  private static readonly string RepoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));

  /// <summary>Mirrors PatchPipeline.Run's project filter: under the escape hatch nothing is filtered;
  ///   otherwise the project's declaration decides (no csproj = always replayed).</summary>
  private static bool ModReplayed(string label, string mod) {
    if (!SmokeTestCtx.TreeIsDeclared) {
      return true;
    }

    var csproj = Path.Combine(RepoRoot, mod, mod + ".csproj");
    return !File.Exists(csproj) || GameVersionDeclarations.Load(RepoRoot).For(csproj).Test.Contains(label);
  }
}

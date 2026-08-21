using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>Public, game-free AuthZ invariant-engine behavior.</summary>
[Collection(ModLogicCollection.Name)]
public sealed class AuthZInvariantEngineTests {
  private readonly Type engineType;
  private readonly Type settingsType;

  public AuthZInvariantEngineTests() {
    // AuthZ invariant types reference UnityEngine.Vector3. Tested paths do not log, so real Unity is safe.
    ModLogicHost host = ModLogicHost.For("AuthZ", stubUnity: false);
    engineType = host.ModType("AuthZ.InvariantEngine");
    settingsType = host.ModType("AuthZ.Settings");
  }

  [Fact]
  public void Registration_has_the_19_documented_unique_ids() {
    Reset();
    Assert.Equal(new[] {
      "exp.self-only", "skill.self-only", "score.self-only", "itemaction.self-only", "smell.self-only",
      "lasersight.self-only", "chat.identity", "pve.damage", "pve.buff", "pve.velocity", "pve.attack-target",
      "skill.range", "buff.known-name", "exp.delta", "lifecycle.enter-game-once", "lifecycle.spawn-once",
      "lifecycle.spawn-when-unbound", "editor.volume-add", "editor.volume-update"
    }, Invariants().Select(Id).ToArray());
  }

  [Fact]
  public void Ensure_registered_does_not_replace_the_existing_set() {
    Reset();
    object first = Invariants().First();
    ModLogicHost.CallStatic(engineType, "EnsureRegistered");
    Assert.Same(first, Invariants().First());
  }

  [Fact]
  public void Settings_default_and_per_invariant_modes_are_applied() {
    Reset();
    Load("<AuthZ><defaults mode=\"off\" /><invariants><invariant id=\"pve.damage\" mode=\"log\" />" +
         "<invariant id=\"exp.delta\" mode=\"block\" /></invariants></AuthZ>");
    ModLogicHost.CallStatic(engineType, "ApplySettings", (Action<string>)(_ => { }));
    Assert.Equal("Off", Mode("chat.identity"));
    Assert.Equal("Log", Mode("pve.damage"));
    Assert.Equal("Block", Mode("exp.delta"));
  }

  [Fact]
  public void Invalid_mode_falls_back_and_unknown_id_is_reported() {
    Reset();
    var warnings = new List<string>();
    Load("<AuthZ><invariants><invariant id=\"pve.damage\" mode=\"invalid\" />" +
         "<invariant id=\"retired.rule\" mode=\"off\" /></invariants></AuthZ>", warnings);
    ModLogicHost.CallStatic(engineType, "ApplySettings", (Action<string>)warnings.Add);
    Assert.Equal("Log", Mode("pve.damage"));
    Assert.Contains(warnings, warning => warning.Contains("invalid"));
    Assert.Contains(warnings, warning => warning.Contains("retired.rule"));
  }

  [Fact]
  public void Shipped_template_names_each_registered_invariant_once() {
    Reset();
    XDocument template = XDocument.Load(Path.Combine(AssemblyMetadata.Get("RepoRoot"), "AuthZ", "Docs",
      "AuthZ.default.xml"));
    string[] configured = template.Descendants("invariant").Select(e => (string)e.Attribute("id")).ToArray();
    Assert.Equal(Invariants().Select(Id).OrderBy(id => id), configured.OrderBy(id => id));
    Assert.Equal(configured.Length, configured.Distinct().Count());
  }

  [Fact]
  public void Patch_targets_follow_enabled_invariants() {
    Reset();
    Load("<AuthZ><defaults mode=\"off\" /></AuthZ>");
    ModLogicHost.CallStatic(engineType, "ApplySettings", (Action<string>)(_ => { }));
    Assert.Empty(Targets());
    Load("<AuthZ><defaults mode=\"log\" /></AuthZ>");
    ModLogicHost.CallStatic(engineType, "ApplySettings", (Action<string>)(_ => { }));
    Assert.NotEmpty(Targets());
  }

  private void Reset() { ModLogicHost.CallStatic(engineType, "RegisterAll"); Load("<AuthZ />"); }

  private void Load(string xml, List<string> warnings = null) => ModLogicHost.CallStatic(settingsType, "Load",
    XDocument.Parse(xml), (Action<string>)(warnings == null ? _ => { } : warnings.Add));

  private IEnumerable<object> Invariants() =>
    ((IEnumerable)ModLogicHost.CallStatic(engineType, "get_All")).Cast<object>();

  private IEnumerable<object> Targets() =>
    ((IEnumerable)ModLogicHost.CallStatic(engineType, "PatchTargets")).Cast<object>();

  private string Mode(string id) => Invariants().Single(invariant => Id(invariant) == id).GetType()
    .GetProperty("Mode")!.GetValue(Invariants().Single(invariant => Id(invariant) == id))!.ToString();

  private static string Id(object invariant) => (string)invariant.GetType().GetProperty("Id")!.GetValue(invariant)!;
}

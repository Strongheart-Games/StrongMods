using System;
using System.Collections.Generic;
using System.Linq;
using AutoCollectLoot;
using Xunit;

namespace Tests.AutoCollectLoot;

public class LootCoverageAuditTests {
  [Fact]
  public void Report_classifies_omissions_in_deterministic_order_and_groups_every_affected_enemy() {
    LootCoverageReport report = LootCoverageAudit.CreateReport(new LootCoverageInput(
      new[] {
        Declaration("Configured", null, "EntityLootContainer", "Mesh", "LootList"),
        Declaration("DropBag", null, "EntityLootContainer", "Mesh", "LootList"),
        Declaration("Missing", null, "EntityLootContainer", "LootList"),
        Declaration("InheritedBase", null, "EntityLootContainer", "Mesh", "LootList"),
        Declaration("Inherited", "InheritedBase", null),
        Declaration("Deep", "Inherited", null, "Mesh", "LootList"),
        Declaration("Other", null, "EntityZombie", "Mesh", "LootList"),
        Declaration("Unmapped", null, "EntityLootContainer", "Mesh", "LootList"),
        Declaration("Invalid", null, "EntityLootContainer", "Mesh", "LootList")
      },
      new[] {
        Candidate("Configured", "configured", "zombieA"), Candidate("DropBag", "cntDropBag", "zombieB"),
        Candidate("Missing", "missing", "zombieC"), Candidate("Inherited", "inherited", "zombieD"),
        Candidate("Deep", "deep", "zombieD", "zombieE"), Candidate("Other", "other", "zombieF"),
        Candidate("Unmapped", "unmapped", "zombieG"), Candidate("Invalid", "invalid", "zombieH")
      },
      Substitutes(("Configured", new LootSubstitute("AutoLoot_Configured", true, "configured")),
        ("Invalid", new LootSubstitute("AutoLoot_Invalid", false, "invalid")),
        ("Invalid", new LootSubstitute("AutoLoot_Duplicate", true, "invalid")))));

    Assert.Equal(8, report.CandidateCount);
    Assert.Equal(1, report.ConfiguredCount);
    Assert.Equal(7, report.Omissions.Count);
    Assert.Equal(7, report.AffectedEnemyCount);
    Assert.Equal(new[] { "Deep", "DropBag", "Inherited", "Invalid", "Missing", "Other", "Unmapped" },
      report.Omissions.Select(omission => omission.ContainerName));
    Assert.Equal("unsupported-inheritance-depth", report.Omissions[0].Reasons.Single().Token);
    Assert.Equal("excluded-drop-bag-policy", report.Omissions[1].Reasons.Single().Token);
    Assert.Equal("inherited-required-data", report.Omissions[2].Reasons.Single().Token);
    Assert.Equal("invalid-substitute-item", report.Omissions[3].Reasons.Single().Token);
    Assert.Equal("missing-required-data", report.Omissions[4].Reasons.Single().Token);
    Assert.Equal("unsupported-drop-entity-class", report.Omissions[5].Reasons.Single().Token);
    Assert.Equal("missing-substitute-item", report.Omissions[6].Reasons.Single().Token);
    Assert.Equal(new[] { "zombieD", "zombieE" }, report.Omissions[0].EnemyNames);
  }

  [Fact]
  public void Coordinator_waits_for_a_stable_snapshot_and_resets_for_a_later_game_start() {
    LootCoverageInput input = Input("one");
    var reports = new List<LootCoverageReport>();
    var inconclusive = new List<string>();
    var coordinator = new LootCoverageAuditCoordinator(() => input, reports.Add, inconclusive.Add);

    coordinator.Arm(0);
    coordinator.Update(0);
    coordinator.Update(4.9f);
    Assert.Empty(reports);

    input = Input("two");
    coordinator.Update(5.2f);
    coordinator.Update(10.1f);
    Assert.Empty(reports);
    coordinator.Update(10.4f);
    Assert.Single(reports);

    coordinator.Arm(20);
    coordinator.Update(20);
    coordinator.Update(25.1f);
    Assert.Equal(2, reports.Count);
    Assert.Empty(inconclusive);
  }

  [Fact]
  public void Coordinator_logs_only_an_inconclusive_result_when_prerequisites_never_arrive() {
    var inconclusive = new List<string>();
    var coordinator = new LootCoverageAuditCoordinator(() => null, _ => throw new InvalidOperationException(), inconclusive.Add);

    coordinator.Arm(0);
    coordinator.Update(30);

    Assert.Equal(new[] { "prerequisites unavailable" }, inconclusive);
  }

  private static LootCoverageInput Input(string lootList) => new(new[] {
    Declaration("Container", null, "EntityLootContainer", "Mesh", "LootList")
  }, new[] { Candidate("Container", lootList, "zombie") },
    Substitutes(("Container", new LootSubstitute("AutoLoot_Container", true, lootList))));

  private static LootContainerDeclaration Declaration(string name, string extends, string @class, params string[] properties) =>
    new(name, extends, @class, properties);

  private static LootCoverageCandidate Candidate(string name, string lootList, params string[] enemies) =>
    new(name, lootList, enemies);

  private static IReadOnlyDictionary<string, IReadOnlyList<LootSubstitute>> Substitutes(
    params (string Target, LootSubstitute Item)[] entries) => entries.GroupBy(entry => entry.Target, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => (IReadOnlyList<LootSubstitute>)group.Select(entry => entry.Item).ToArray(),
        StringComparer.Ordinal);
}

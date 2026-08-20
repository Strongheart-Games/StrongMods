using System.Collections.Generic;
using System.IO;
using Tests.Fixtures;
using Xunit;

namespace Tests.AutoCollectLoot;

/// <summary>
///   Verifies the Project Z hierarchy adapter runs before AutoCollectLoot's direct-class item generator.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class ProjectZFixesCompatibilityTests {
  [Fact]
  public void ProjectZFixes_makes_BossMasterLoot_children_visible_to_AutoCollectLoot_baseline() {
    PatcherHost host = PatcherHost.Instance.Value;
    object entityClasses = host.CreateXmlFile(ProjectZEntityClasses, "entityclasses.xml");
    object items = host.CreateXmlFile("<items />", "items.xml");
    host.Cache.Clear();
    try {
      AssertNoProblems(host.ApplyPatchFile(entityClasses, File.ReadAllText(ProjectZFixesEntityClassesPath),
        "entityclasses.xml"));
      host.Cache.Seed("entityclasses", entityClasses);

      AssertNoProblems(host.ApplyPatchFile(items, File.ReadAllText(AutoCollectLootItemsPath), "items.xml"));

      var result = host.XmlOf(items);
      Assert.Contains("AutoLoot_BossLootContainerMummy", result);
      Assert.Contains("AutoLoot_EntityLootContainerRegular", result);
    } finally {
      host.Cache.Clear();
    }
  }

  [Fact]
  public void ProjectZFixes_is_silent_when_BossMasterLoot_is_absent() {
    PatcherHost host = PatcherHost.Instance.Value;
    object entityClasses = host.CreateXmlFile(OlderProjectZEntityClasses, "entityclasses.xml");
    host.Cache.Clear();
    try {
      AssertNoProblems(host.ApplyPatchFile(entityClasses, File.ReadAllText(ProjectZFixesEntityClassesPath),
        "entityclasses.xml"));
      Assert.Contains("EntityLootContainer", host.XmlOf(entityClasses));
    } finally {
      host.Cache.Clear();
    }
  }

  private static readonly string AutoCollectLootItemsPath = Path.Combine(AssemblyMetadata.Get("RepoRoot"),
    "AutoCollectLoot", "Config", "items.xml");

  private static readonly string ProjectZFixesEntityClassesPath = Path.Combine(AssemblyMetadata.Get("RepoRoot"),
    "ProjectZFixes", "Config", "entityclasses.xml");

  private static void AssertNoProblems(IReadOnlyList<LogEntry> logs) {
    Assert.DoesNotContain(logs, log => log.Level is LogLevel.Error or LogLevel.Warning or LogLevel.Exception);
  }

  private const string ProjectZEntityClasses = """
    <entity_classes>
      <entity_class name="EntityLootContainerRegular">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/zpackPrefab.prefab" />
        <property name="LootList" value="regularRewards" />
      </entity_class>
      <entity_class name="BossMasterLoot">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/zpackRedPrefab.prefab" />
        <property name="LootList" value="masterRewards" />
      </entity_class>
      <entity_class name="BossLootContainerMummy" extends="BossMasterLoot">
        <property name="Mesh" value="@:Entities/LootContainers/tier3LootChestPrefab.prefab" />
        <property name="LootList" value="mummyRewards" />
      </entity_class>
    </entity_classes>
    """;

  private const string OlderProjectZEntityClasses = """
    <entity_classes>
      <entity_class name="LegacyBossLootContainer">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/zpackRedPrefab.prefab" />
        <property name="LootList" value="legacyBossRewards" />
      </entity_class>
    </entity_classes>
    """;
}

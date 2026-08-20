using System.IO;
using Tests.Fixtures;
using Xunit;

namespace Tests.AutoCollectLoot;

/// <summary>
///   Regression coverage for AutoCollectLoot's direct-container item generator. The synthetic content deliberately has
///   no third-party identity.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class DirectLootContainerGenerationTests {
  [Fact]
  public void Shipped_items_patch_generates_substitutes_only_for_loot_containers_with_direct_required_data() {
    PatcherHost host = PatcherHost.Instance.Value;
    object items = host.CreateXmlFile("<items />", "items.xml");
    host.Cache.Clear();
    host.Cache.Seed("entityclasses", host.CreateXmlFile(EntityClasses, "entityclasses.xml"));
    try {
      var logs = host.ApplyPatchFile(items, File.ReadAllText(ItemsPatchPath), "items.xml");
      var result = host.XmlOf(items);

      Assert.Empty(logs);
      Assert.Contains("""<item name="AutoLoot_DirectContainer">""", result);
      Assert.Contains("""<property name="AutoLootSubstituteFor" value="DirectContainer" />""", result);
      Assert.Contains("""<item name="AutoLoot_DirectExtendedContainer">""", result);
      Assert.DoesNotContain("AutoLoot_SyntheticBossContainer", result);
      Assert.DoesNotContain("AutoLoot_TransitiveContainer", result);
      Assert.DoesNotContain("AutoLoot_IncompleteChild", result);
      Assert.DoesNotContain("AutoLoot_DirectMissingMesh", result);
      Assert.DoesNotContain("AutoLoot_DirectMissingLootList", result);
    } finally {
      host.Cache.Clear();
    }
  }

  private static readonly string ItemsPatchPath = Path.Combine(AssemblyMetadata.Get("RepoRoot"), "AutoCollectLoot",
    "Config", "items.xml");

  private const string EntityClasses = """
    <entity_classes>
      <entity_class name="DirectContainer">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/direct.prefab" />
        <property name="LootList" value="directRewards" />
      </entity_class>
      <entity_class name="SharedContainerBase" />
      <entity_class name="DirectExtendedContainer" extends="SharedContainerBase">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/direct-extended.prefab" />
        <property name="LootList" value="directExtendedRewards" />
      </entity_class>
      <entity_class name="DirectMissingMesh">
        <property name="Class" value="EntityLootContainer" />
        <property name="LootList" value="missingMeshRewards" />
      </entity_class>
      <entity_class name="DirectMissingLootList">
        <property name="Class" value="EntityLootContainer" />
        <property name="Mesh" value="@:Entities/LootContainers/missing-loot-list.prefab" />
      </entity_class>
      <entity_class name="SyntheticBossContainer" extends="DirectContainer">
        <property name="Mesh" value="@:Entities/LootContainers/boss.prefab" />
        <property name="LootList" value="syntheticBossRewards" />
      </entity_class>
      <entity_class name="TransitiveContainer" extends="SyntheticBossContainer">
        <property name="Mesh" value="@:Entities/LootContainers/transitive.prefab" />
        <property name="LootList" value="transitiveRewards" />
      </entity_class>
      <entity_class name="IncompleteChild" extends="DirectContainer">
        <property name="Mesh" value="@:Entities/LootContainers/incomplete.prefab" />
      </entity_class>
    </entity_classes>
    """;
}

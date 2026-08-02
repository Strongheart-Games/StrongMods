using System.Collections.Generic;
using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md on reading from another config file: <c>source=</c> on
///   <c>&lt;foreach&gt;</c> and on <c>&lt;bind&gt;</c>. Deferred here from waves A and B because it resolves
///   through the breadth-first patcher's cache, which these tests seed.
/// </summary>
[Collection(GameRoomCollection.Name)]
public class CrossFileTests {
  private const string Items = """<items><item name="alpha" /></items>""";

  private static readonly Dictionary<string, string> EntityClasses = new() {
    ["entityclasses"] = """
      <entity_classes>
        <entity_class name="zombieOne"><property name="Tier" value="1" /></entity_class>
        <entity_class name="zombieTwo"><property name="Tier" value="2" /></entity_class>
      </entity_classes>
      """,
  };

  [Fact]
  public void Foreach_source_loops_over_another_document_while_commands_target_this_one() {
    // Spec: source is "Which config file to select from, named without the .xml"; body commands still target
    // the patch's own file — the whole point of Example 2, "generate one file from another".
    PatchOutcome result = GameRoom.Instance.Value.Apply(Items, """
      <foreach source="entityclasses" xpath="/entity_classes/entity_class" as="entity">
        <append xpath="/items">
          <item name="{$entity/@name}" tier="{$entity/property[@name='Tier']/@value}" />
        </append>
      </foreach>
      """, EntityClasses);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<item name="zombieOne" tier="1" />""", result.Xml);
    Assert.Contains("""<item name="zombieTwo" tier="2" />""", result.Xml);
  }

  [Fact]
  public void Bind_source_reads_its_table_from_another_document() {
    // Spec: "<bind name="loot" source="loot_tables" xpath="/loot_tables/autoloot/row" />".
    PatchOutcome result = GameRoom.Instance.Value.Apply(Items, """
      <foreach xpath="/items/item" as="item">
        <bind name="tiers" source="entityclasses" xpath="/entity_classes/entity_class" />
        <append xpath="/items">
          <counted rows="{count($tiers)}" first="{$tiers[@name='zombieOne']/property[@name='Tier']/@value}" />
        </append>
      </foreach>
      """, EntityClasses);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<counted rows="2" first="1" />""", result.Xml);
  }

  [Fact]
  public void A_source_written_with_the_xml_extension_is_told_to_drop_it() {
    // The likeliest way to get this wrong, so the error names the fix rather than just reporting a miss.
    PatchOutcome result = GameRoom.Instance.Value.Apply(Items, """
      <foreach source="entityclasses.xml" xpath="/entity_classes/entity_class" as="entity">
        <append xpath="/items"><nope /></append>
      </foreach>
      """, EntityClasses);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("name the file without") && e.Contains("entityclasses"));
  }
}

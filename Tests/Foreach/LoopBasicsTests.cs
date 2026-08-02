using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md, "Writing a loop". One test per documented behavior; the
///   spec clause is named in each test so a failure reads as "the spec says X, the engine did Y".
/// </summary>
[Collection(GameRoomCollection.Name)]
public class LoopBasicsTests {
  private const string ThreeItems = """
    <items>
      <item name="alpha" tier="1" />
      <item name="beta" tier="2" />
      <item name="gamma" tier="3" />
    </items>
    """;

  [Fact]
  public void Body_runs_once_per_matched_node_in_document_order() {
    // Spec: "Selects the nodes to loop over. Runs once, in document order."
    PatchOutcome result = GameRoom.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <seen name="{$item/@name}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<seen name="alpha" /><seen name="beta" /><seen name="gamma" />""", result.Xml);
  }

  [Fact]
  public void Body_xpath_targets_the_file_not_the_bound_node() {
    // Spec: "Body XPaths are absolute against your target document... To aim a command at the node you're
    // looping over, interpolate your way back to it."
    PatchOutcome result = GameRoom.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item[@tier='2']" as="item">
        <setattribute xpath="/items/item[@name='{$item/@name}']" name="marked">yes</setattribute>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<item name="beta" tier="2" marked="yes" />""", result.Xml);
    Assert.DoesNotContain("""name="alpha" tier="1" marked""", result.Xml);
  }

  [Fact]
  public void Inner_loops_can_read_outer_bindings() {
    // Spec: "Nesting works, and inner loops can read outer bindings."
    PatchOutcome result = GameRoom.Instance.Value.Apply("""
      <items>
        <item name="alpha"><part id="1" /><part id="2" /></item>
      </items>
      """, """
      <foreach xpath="/items/item" as="item">
        <foreach xpath="/items/item/part" as="part">
          <append xpath="/items">
            <pair item="{$item/@name}" part="{$part/@id}" />
          </append>
        </foreach>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<pair item="alpha" part="1" /><pair item="alpha" part="2" />""", result.Xml);
  }

  [Fact]
  public void Reusing_a_name_already_in_scope_is_an_error() {
    // Spec: "Reusing a name that's already in scope is an error, not a shadow."
    PatchOutcome result = GameRoom.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item" as="item">
        <foreach xpath="/items/item" as="item">
          <append xpath="/items"><nope /></append>
        </foreach>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("""as="item" shadows an enclosing binding"""));
  }
}

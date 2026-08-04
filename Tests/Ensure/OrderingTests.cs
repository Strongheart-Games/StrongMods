using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "Order matters, and so does position". Sibling order decides
///   which duplicate wins in this game's config, so every one of these is a behavioral claim rather than a
///   cosmetic one: where a new child lands, and that an existing one is never shuffled.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class OrderingTests {
  private const string ItemWithOne = """
    <items>
      <item name="alpha">
        <property name="Existing" value="0" />
      </item>
    </items>
    """;

  [Fact]
  public void New_children_are_appended_by_default() {
    // Doc: "New children are appended by default, which puts what your mod declares after anything already
    // present — your value takes effect."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithOne, """
      <ensure xpath="/items/item[@name='alpha']">
        <property name="Added" value="1" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="Existing" value="0" /><property name="Added" value="1" />""", result.Xml);
  }

  [Fact]
  public void Prepend_puts_new_children_first() {
    // Doc: "position=prepend states the opposite intent: contribute a value only if nothing later overrides it."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithOne, """
      <ensure xpath="/items/item[@name='alpha']" position="prepend">
        <property name="Added" value="1" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="Added" value="1" /><property name="Existing" value="0" />""", result.Xml);
  }

  [Fact]
  public void Several_new_children_keep_their_written_order_when_appended() {
    // Doc: "Several new children in one block keep the order you wrote them, prepended or appended."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithOne, """
      <ensure xpath="/items/item[@name='alpha']">
        <property name="A" value="1" />
        <property name="B" value="2" />
        <property name="C" value="3" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="Existing" value="0" /><property name="A" value="1" />""" +
      """<property name="B" value="2" /><property name="C" value="3" />""", result.Xml);
  }

  [Fact]
  public void Several_new_children_keep_their_written_order_when_prepended() {
    // Same clause, and the one that is easy to get wrong: prepending each in turn would come out reversed.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithOne, """
      <ensure xpath="/items/item[@name='alpha']" position="prepend">
        <property name="A" value="1" />
        <property name="B" value="2" />
        <property name="C" value="3" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="A" value="1" /><property name="B" value="2" />""" +
      """<property name="C" value="3" /><property name="Existing" value="0" />""", result.Xml);
  }

  [Fact]
  public void Merging_never_moves_an_existing_element() {
    // Doc: "Merging never moves an element. position governs insertion only." Even under prepend, the matched
    // element is updated where it sits — relocating a node the author never asked to move would be surprise for
    // no benefit, since one match's position cannot change which value wins.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property name="First" value="1" />
          <property name="Second" value="2" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']" position="prepend">
        <property name="Second" value="99" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="First" value="1" /><property name="Second" value="99" />""", result.Xml);
  }

  [Fact]
  public void Position_applies_at_every_level_of_a_nested_block() {
    // Doc: "position applies at every level of a nested block, not just the outermost." One rule everywhere
    // beats a rule that silently stops applying once you nest.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0">
            <property name="Existing" value="0" />
          </property>
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']" position="prepend">
        <property class="Action0">
          <property name="Added" value="1" />
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property name="Added" value="1" /><property name="Existing" value="0" />""", result.Xml);
  }
}

using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "Text content". Two guards carry the section: only a childless
///   template can declare text, and whitespace is never content. Both exist because the game's config is full of
///   indentation between children, and mistaking that for a value would quietly blank out elements.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class TextContentTests {
  private const string OneBlock = """
    <blocks>
      <block name="terrDirt" />
    </blocks>
    """;

  [Fact]
  public void Text_is_set_on_a_created_element() {
    // Doc: "A template with text and no child elements sets the element's text."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneBlock, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group">Building</property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group">Building</property>""", result.Xml);
  }

  [Fact]
  public void Text_is_set_on_an_existing_element() {
    // Same clause on the merge path — the element is already there, and its text is brought up to date.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <blocks>
        <block name="terrDirt">
          <property name="Group">Old</property>
        </block>
      </blocks>
      """, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group">Building</property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group">Building</property>""", result.Xml);
    Assert.DoesNotContain("Old", result.Xml);
  }

  [Fact]
  public void Surrounding_whitespace_is_trimmed() {
    // Doc: "Surrounding whitespace is trimmed." Without this, creating and merging would disagree and the block
    // would stop being idempotent — the same reason the clone normalizes its own text.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneBlock, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group">
          Building
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group">Building</property>""", result.Xml);
  }

  [Fact]
  public void Whitespace_only_text_declares_nothing_and_leaves_existing_text_alone() {
    // Doc: "the whitespace between nested children is formatting, not content, and is never mistaken for it."
    // A template that carries only whitespace declares no text, so it must not blank the element it merges into.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <blocks>
        <block name="terrDirt">
          <property name="Group">Building</property>
        </block>
      </blocks>
      """, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group" tier="2">
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group" tier="2">Building</property>""", result.Xml);
  }

  [Fact]
  public void A_template_with_child_elements_never_sets_text() {
    // Doc: "only a template with no child elements can carry text". The indentation around the inner property is
    // whitespace text on the outer one; treating it as content would replace the children it belongs between.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0">
            <property name="Existing" value="1" />
          </property>
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property class="Action0">
          <property name="Added" value="2" />
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Existing" value="1" />""", result.Xml);
    Assert.Contains("""<property name="Added" value="2" />""", result.Xml);
  }

  [Fact]
  public void Setting_text_does_not_remove_existing_child_elements() {
    // Doc, "Nested templates": "<ensure> never removes anything." Replacing an element's whole value would have
    // taken its children with it, so only the text nodes are replaced.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <blocks>
        <block name="terrDirt">
          <property name="Group">Old<nested keep="yes" /></property>
        </block>
      </blocks>
      """, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group">Building</property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<nested keep="yes" />""", result.Xml);
    Assert.Contains("Building", result.Xml);
    Assert.DoesNotContain("Old", result.Xml);
  }

  [Fact]
  public void A_template_with_no_text_leaves_existing_text_alone() {
    // The ordinary attribute-only template must not be read as "set the text to empty".
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <blocks>
        <block name="terrDirt">
          <property name="Group">Building</property>
        </block>
      </blocks>
      """, """
      <ensure xpath="/blocks/block[@name='terrDirt']">
        <property name="Group" tier="2" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group" tier="2">Building</property>""", result.Xml);
  }
}

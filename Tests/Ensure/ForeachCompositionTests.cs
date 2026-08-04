using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   &lt;ensure&gt; inside a &lt;foreach&gt; body. Nothing in either engine special-cases the other: foreach
///   materializes an ensure block like any other body command — substituting <c>{…}</c> through its attributes,
///   its template children, and their text — and then dispatches it through the game's own singlePatch. These
///   pin that composition, because "it should just work" is exactly the claim that quietly stops being true.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class ForeachCompositionTests {
  private const string ThreeItems = """
    <items>
      <item name="alpha" tier="1" />
      <item name="beta" tier="2" />
      <item name="gamma" tier="3" />
    </items>
    """;

  [Fact]
  public void An_ensure_body_is_materialized_once_per_iteration() {
    // The loop aims each ensure at its own item, and the template reads that item's data — the pairing that
    // makes ensure set-based over a computed selector rather than a literal one.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}']">
          <property name="Tier" value="{$item/@tier}" />
        </ensure>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<item name="alpha" tier="1"><property name="Tier" value="1" /></item>""", result.Xml);
    Assert.Contains("""<item name="beta" tier="2"><property name="Tier" value="2" /></item>""", result.Xml);
    Assert.Contains("""<item name="gamma" tier="3"><property name="Tier" value="3" /></item>""", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Iterations_create_and_merge_independently() {
    // Half the items already carry the property. Each iteration takes whichever branch its own item needs, which
    // is the whole point of driving ensure from a loop.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha"><property name="Tier" value="old" /></item>
        <item name="beta" />
      </items>
      """, """
      <foreach xpath="/items/item" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}']">
          <property name="Tier" value="{$item/@name}" />
        </ensure>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<item name="alpha"><property name="Tier" value="alpha" /></item>""", result.Xml);
    Assert.Contains("""<item name="beta"><property name="Tier" value="beta" /></item>""", result.Xml);
    Assert.DoesNotContain("old", result.Xml);
  }

  [Fact]
  public void Substitution_reaches_nested_template_children() {
    // foreach clones a body command depth-first, so an expression works at any level of an ensure block, not
    // only on its outermost template.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item[@tier='2']" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}']">
          <property class="Action0">
            <property name="Label" value="{$item/@name}_action" />
          </property>
        </ensure>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Label" value="beta_action" />""", result.Xml);
  }

  [Fact]
  public void Substitution_reaches_ensure_key() {
    // ensure-key is an ordinary attribute as far as foreach is concerned, so it substitutes like any other —
    // worth pinning, since a reserved attribute is exactly the kind of thing a future change might special-case.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="wrench" keyed="name,operation">
          <passive_effect name="EntityDamage" operation="base_set" value="1" />
          <passive_effect name="EntityDamage" operation="perc_add" value="0.1" />
        </item>
      </items>
      """, """
      <foreach xpath="/items/item" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}']">
          <passive_effect ensure-key="{$item/@keyed}" name="EntityDamage" operation="perc_add" value="0.5" />
        </ensure>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""operation="perc_add" value="0.5" """.TrimEnd() + " />", result.Xml);
    Assert.Contains("""operation="base_set" value="1" """.TrimEnd() + " />", result.Xml);
    Assert.DoesNotContain("ensure-key", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Substitution_reaches_template_text() {
    // Text is substituted alongside attributes, so the childless-template-sets-text rule composes too.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item[@tier='3']" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}']">
          <property name="Group">{$item/@name}</property>
        </ensure>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Group">gamma</property>""", result.Xml);
  }

  [Fact]
  public void An_ensure_failure_inside_a_loop_stays_a_per_iteration_skip() {
    // foreach reports a body command that did not apply per iteration and carries on; ensure's own zero-match
    // return feeds straight into that. The item that does match is still ensured.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ThreeItems, """
      <foreach xpath="/items/item" as="item">
        <ensure xpath="/items/item[@name='{$item/@name}'][@tier='1']">
          <property name="Only" value="alpha" />
        </ensure>
      </foreach>
      """);

    Assert.Contains("""<item name="alpha" tier="1"><property name="Only" value="alpha" /></item>""", result.Xml);
    Assert.Contains("""<item name="beta" tier="2" />""", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("<ensure> did not apply"));
  }
}

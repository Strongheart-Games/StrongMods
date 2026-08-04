using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md, "Filling in values": where <c>{...}</c> interpolates, the
///   exactly-one-node rule, and <c>?:</c> fallbacks.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class InterpolationTests {
  private const string TwoItems = """
    <items>
      <item name="alpha"><property name="Tier" value="hi" /></item>
      <item name="beta" />
    </items>
    """;

  [Fact]
  public void Interpolates_into_attribute_values_and_element_text() {
    // Spec: "Interpolation works in four places: attribute values, element text, body command XPaths, and
    // element names." (Element names — the foreach-name attribute — are covered in wave A2.)
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <append xpath="/items">
          <copy of="{$item/@name}">{$item/property[@name='Tier']/@value}</copy>
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<copy of="alpha">hi</copy>""", result.Xml);
  }

  [Fact]
  public void Scalar_expressions_resolve_to_one_value_and_never_skip() {
    // Spec: "Scalar XPath results (count(), string(), string-length(), boolean tests) always produce exactly
    // one value and never skip." — beta has no properties, so count() is 0 rather than a skip.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <counted name="{$item/@name}" properties="{count($item/property)}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<counted name="alpha" properties="1" />""", result.Xml);
    Assert.Contains("""<counted name="beta" properties="0" />""", result.Xml);
  }

  [Fact]
  public void Zero_matches_skips_the_whole_iteration_with_a_warning() {
    // Spec: "Zero matches and the patcher skips that iteration entirely — no half-written item — and logs a
    // warning." beta has no Tier property, so its iteration must produce nothing at all.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <tier for="{$item/@name}" value="{$item/property[@name='Tier']/@value}" />
        </append>
      </foreach>
      """);

    Assert.Contains("""<tier for="alpha" value="hi" />""", result.Xml);
    Assert.DoesNotContain("""for="beta""", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped") && w.Contains("matched 0 nodes"));
  }

  [Fact]
  public void Fallback_runs_only_when_the_left_side_selects_nothing() {
    // Spec: "The right side only runs when the left selects no nodes."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <tier for="{$item/@name}" value="{$item/property[@name='Tier']/@value ?: $item/@name}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<tier for="alpha" value="hi" />""", result.Xml);
    Assert.Contains("""<tier for="beta" value="beta" />""", result.Xml);
  }

  private const string TwoTiers = """
    <items>
      <item name="alpha">
        <property name="Tier" value="one" />
        <property name="Tier" value="two" />
      </item>
    </items>
    """;

  [Fact]
  public void Foreach_name_renames_the_element() {
    // Spec: "use the reserved foreach-name attribute, and the tag renames itself" — XML forbids '{' in a tag.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <append xpath="/items">
          <placeholder foreach-name="{$item/@name}_extra" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("<alpha_extra />", result.Xml);
    Assert.DoesNotContain("placeholder", result.Xml);
  }

  [Fact]
  public void Doubled_braces_are_literal_braces() {
    // Spec: "For a literal brace, double it: {{ and }}."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <append xpath="/items">
          <literal value="{{not an expression}}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<literal value="{not an expression}" />""", result.Xml);
  }

  [Fact]
  public void Two_or_more_matches_skips_the_iteration() {
    // Spec: "Two or more matches skips too: an ambiguous lookup should never guess."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoTiers, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <tier value="{$item/property[@name='Tier']/@value}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<tier", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void Two_or_more_matches_skip_rather_than_falling_through() {
    // Spec: "two or more matches skip the iteration rather than falling through".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoTiers, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <tier value="{$item/property[@name='Tier']/@value ?: $item/@name}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<tier", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void Both_sides_empty_skips_the_iteration() {
    // Spec: "if both sides come up empty, the iteration skips".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <append xpath="/items">
          <tier value="{$item/@missing ?: $item/@alsoMissing}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<tier", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void Only_one_fallback_operator_is_allowed_per_expression() {
    // Spec: "One ?: per expression."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <append xpath="/items">
          <tier value="{$item/@missing ?: $item/@alsoMissing ?: $item/@name}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<tier", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("chains '?:' more than once"));
  }

  [Fact]
  public void An_empty_attribute_is_a_match_and_does_not_fall_through() {
    // Spec: "an attribute that exists but is empty (tier="") is one node with the value "" and does not fall
    // through".
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items><item name="alpha" tier="" /></items>
      """, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <tier value="{$item/@tier ?: $item/@name}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<tier value="" />""", result.Xml);
  }
}

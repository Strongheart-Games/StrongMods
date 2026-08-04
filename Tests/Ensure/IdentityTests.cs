using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "Identity: how a child is recognized". The doc's claim is that
///   the default key covers the overwhelming majority of the game's config and that ambiguity is never resolved
///   by guessing, so these cover both halves: what the default key matches, and what happens when it cannot tell
///   two siblings apart.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class IdentityTests {
  [Fact]
  public void The_default_key_is_name() {
    // Doc: "By default the key is name, or class, or both — whichever the template carries."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property name="Stacknumber" value="1" />
          <property name="EconomicValue" value="10" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Stacknumber" value="65000" />""", result.Xml);
    Assert.Contains("""<property name="EconomicValue" value="10" />""", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void A_template_with_no_name_is_keyed_on_class() {
    // Doc: the <property class="Action0" /> case. The existing Action0 is updated rather than duplicated.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0" delay="1" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property class="Action0" delay="0" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property class="Action0" delay="0" />""", result.Xml);
    Assert.DoesNotContain("""delay="1" """, result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void A_template_carrying_both_is_keyed_on_both() {
    // Doc: "or both — whichever the template carries". The sibling shares the name but not the class, so it is a
    // different element and must be left alone rather than updated.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property name="Action" class="Attack" value="old" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property name="Action" class="Reload" value="new" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Action" class="Attack" value="old" />""", result.Xml);
    Assert.Contains("""<property name="Action" class="Reload" value="new" />""", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Ensure_key_names_the_attributes_that_identify_the_element() {
    // Doc: the passive_effect case — "items routinely carry several <passive_effect name="..."> that differ by
    // operation and tags". Only the perc_add row is the target; the base_set row must survive untouched.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="wrench">
          <passive_effect name="EntityDamage" operation="base_set" tags="salvaging" value="1" />
          <passive_effect name="EntityDamage" operation="perc_add" tags="salvaging" value="0.1" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='wrench']">
        <passive_effect ensure-key="name,operation,tags"
                        name="EntityDamage" operation="perc_add" tags="salvaging" value="0.5" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""operation="base_set" tags="salvaging" value="1" """.TrimEnd() + " />", result.Xml);
    Assert.Contains("""operation="perc_add" tags="salvaging" value="0.5" """.TrimEnd() + " />", result.Xml);
    Assert.DoesNotContain("0.1", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Ensure_key_is_stripped_from_the_element_it_creates() {
    // Doc: "it is stripped from the element before it is written — it never reaches the game's config."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="wrench" />
      </items>
      """, """
      <ensure xpath="/items/item[@name='wrench']">
        <passive_effect ensure-key="name,operation" name="EntityDamage" operation="perc_add" value="0.5" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<passive_effect name="EntityDamage" operation="perc_add" value="0.5" />""", result.Xml);
    Assert.DoesNotContain("ensure-key", result.Xml);
  }

  [Fact]
  public void Ensure_key_is_stripped_from_nested_templates_too() {
    // The whole cloned subtree is the author's, so every level of it must be cleaned, not just the outermost.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha" />
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property class="Action0">
          <triggered_effect ensure-key="trigger,action" trigger="onSelfPrimaryActionEnd" action="GiveExp" exp="100" />
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""action="GiveExp" exp="100" """.TrimEnd() + " />", result.Xml);
    Assert.DoesNotContain("ensure-key", result.Xml);
  }

  [Fact]
  public void An_ambiguous_key_leaves_that_parent_alone_and_warns() {
    // Doc: "If two of a parent's children match the key, that parent is left untouched and warned about."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="wrench">
          <passive_effect name="EntityDamage" operation="base_set" value="1" />
          <passive_effect name="EntityDamage" operation="perc_add" value="0.1" />
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='wrench']">
        <passive_effect name="EntityDamage" value="0.5" />
      </ensure>
      """);

    Assert.DoesNotContain("0.5", result.Xml);
    Assert.Contains("""value="1" """.TrimEnd() + " />", result.Xml);
    Assert.Contains("""value="0.1" """.TrimEnd() + " />", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("ambiguous") && w.Contains("ensure-key"));
  }

  [Fact]
  public void An_ambiguous_key_on_one_parent_does_not_stop_the_others() {
    // Doc: ambiguity is per-parent. "beta" is unambiguous and must still be ensured.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property name="Stacknumber" value="1" />
          <property name="Stacknumber" value="2" />
        </item>
        <item name="beta" />
      </items>
      """, """
      <ensure xpath="/items/item">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<item name="beta"><property name="Stacknumber" value="65000" /></item>""", result.Xml);
    Assert.Contains("""value="1" """.TrimEnd() + " />", result.Xml);
    Assert.Contains("""value="2" """.TrimEnd() + " />", result.Xml);
    Assert.Single(result.Warnings);
  }

  [Fact]
  public void A_template_that_cannot_be_identified_is_an_error() {
    // Doc: "A template with no name, no class, and no ensure-key cannot be identified at all — <triggered_effect>
    // and <effect_group> are the common ones — and that is an error, not a guess."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha" />
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <triggered_effect trigger="onSelfPrimaryActionEnd" action="GiveExp" exp="100" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("triggered_effect", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("neither a name nor a class") && e.Contains("ensure-key"));
  }

  [Fact]
  public void An_unidentifiable_nested_template_is_an_error_too() {
    // Nested templates resolve up front with the rest, so the block fails before anything is written — not
    // half-applied with the outer property created and the inner one rejected.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha" />
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property class="Action0">
          <triggered_effect trigger="onSelfPrimaryActionEnd" action="GiveExp" />
        </property>
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("Action0", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("neither a name nor a class"));
  }
}

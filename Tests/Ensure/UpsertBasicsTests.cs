using System;
using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "Writing one" — the add-or-merge table, set-based application,
///   and idempotency. One test per documented behavior; the doc clause is named in each so a failure reads as
///   "the doc says X, the engine did Y".
///   Runs on the default host, like Foreach\*: these assert StrongMods' engine semantics against synthetic XML,
///   not a mod's compatibility with a particular vanilla (.ai\testing-declared-versions.md §3c). Per-version
///   assertion for &lt;ensure&gt; happens where it means something — on the mods that use it, in
///   Patcher\PatchApplicationTests.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class UpsertBasicsTests {
  private const string OneItem = """
    <items>
      <item name="schematicMaster" />
    </items>
    """;

  private const string ItemWithProperty = """
    <items>
      <item name="schematicMaster">
        <property name="AltItemTypeIconColor" value="255,0,0,200" />
      </item>
    </items>
    """;

  private const string TintBlock = """
    <ensure xpath="/items/item[@name='schematicMaster']">
      <property name="AltItemTypeIconColor" value="0,255,0,200" />
    </ensure>
    """;

  [Fact]
  public void A_missing_child_is_added() {
    // Doc: "no child matching the template | the template is cloned in and inserted per position".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, TintBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="AltItemTypeIconColor" value="0,255,0,200" />""", result.Xml);
    Assert.Empty(result.Warnings);
    Assert.Empty(result.Errors);
  }

  [Fact]
  public void An_existing_child_is_updated_in_place() {
    // Doc: "exactly one matching child | the template's attributes are set on it". One element, not two.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithProperty, TintBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="AltItemTypeIconColor" value="0,255,0,200" />""", result.Xml);
    Assert.DoesNotContain("255,0,0,200", result.Xml);
    Assert.Empty(result.Warnings);
    Assert.Empty(result.Errors);
  }

  [Fact]
  public void Attributes_the_template_does_not_name_are_kept() {
    // Doc: "attributes you didn't name are kept".
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="schematicMaster">
          <property name="AltItemTypeIconColor" value="255,0,0,200" tint="keepme" />
        </item>
      </items>
      """, TintBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """"<property name="AltItemTypeIconColor" value="0,255,0,200" tint="keepme" />"""", result.Xml);
  }

  [Fact]
  public void Every_matched_parent_is_processed() {
    // Doc: "xpath selects parents, plural. This is the whole difference from the two-command idiom." One item
    // already has the property and one does not, so this covers both branches in a single pass.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha"><property name="Stacknumber" value="1" /></item>
        <item name="beta" />
        <item name="gamma" />
      </items>
      """, """
      <ensure xpath="/items/item">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Equal(3, CountOccurrences(result.Xml, """<property name="Stacknumber" value="65000" />"""));
    Assert.DoesNotContain("value=\"1\"", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Several_templates_in_one_block_are_independent() {
    // Doc: "Several templates in one block are independent, so related properties travel together."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(ItemWithProperty, """
      <ensure xpath="/items/item[@name='schematicMaster']">
        <property name="AltItemTypeIconColor" value="0,255,0,200" />
        <property name="AltItemTypeIcon" value="checkmark" />
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="AltItemTypeIconColor" value="0,255,0,200" />""", result.Xml);
    Assert.Contains("""<property name="AltItemTypeIcon" value="checkmark" />""", result.Xml);
  }

  [Fact]
  public void Applying_the_same_block_twice_changes_nothing_the_second_time() {
    // Doc: "Applying the same block twice leaves the document exactly as applying it once did." Asserted from
    // the absent case, so the second pass is the create-then-merge path where a formatting difference between
    // the cloned template and a merged element would show up.
    PatcherHost host = PatcherHost.Instance.Value;
    PatchOutcome once = host.Apply(OneItem, TintBlock);
    PatchOutcome twice = host.Apply(once.Xml, TintBlock);

    Assert.True(twice.Applied, string.Join("\n", twice.Logs));
    Assert.Equal(once.Xml, twice.Xml);
    Assert.Empty(twice.Warnings);
  }

  [Fact]
  public void Two_mods_ensuring_the_same_property_converge() {
    // Doc: "two mods ensuring the same property converge on the later one's value instead of producing a
    // duplicate" — the property that makes <ensure> safe to use in a mod others patch around.
    PatcherHost host = PatcherHost.Instance.Value;
    PatchOutcome first = host.Apply(OneItem, TintBlock);
    PatchOutcome second = host.Apply(first.Xml, """
      <ensure xpath="/items/item[@name='schematicMaster']">
        <property name="AltItemTypeIconColor" value="0,0,255,200" />
      </ensure>
      """);

    Assert.True(second.Applied, string.Join("\n", second.Logs));
    Assert.Equal(1, CountOccurrences(second.Xml, "AltItemTypeIconColor"));
    Assert.Contains(""""<property name="AltItemTypeIconColor" value="0,0,255,200" />"""", second.Xml);
  }

  [Fact]
  public void A_selector_matching_nothing_does_not_apply() {
    // Doc failure table: "xpath matched nothing | The ordinary vanilla did not apply warning — a real one".
    // Apply() reports the singlePatch verdict; the warning itself is PatchXml's and is asserted in
    // FailureModeTests, which drives a whole patch file.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='noSuchItem']">
        <property name="AltItemTypeIconColor" value="0,255,0,200" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("AltItemTypeIconColor", result.Xml);
    Assert.Empty(result.Errors);
  }

  private static int CountOccurrences(string haystack, string needle) {
    var count = 0;
    for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
         at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) {
      count++;
    }

    return count;
  }
}

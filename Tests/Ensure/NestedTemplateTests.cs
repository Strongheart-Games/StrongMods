using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "Nested templates". The doc's claim is that the same rule
///   applies at every level and that ensure never removes, so these assert both the recursion and what it leaves
///   behind — the create path and the merge path reaching the same shape is the point.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class NestedTemplateTests {
  private const string ActionBlock = """
    <ensure xpath="/items/item[@name='alpha']">
      <property class="Action0">
        <property name="Sound_start" value="read_mod" />
        <property name="Sound_in_head" value="true" />
      </property>
    </ensure>
    """;

  [Fact]
  public void A_created_block_brings_its_children_with_it() {
    // Doc: "Whether Action0 was already there or had to be created, both of those properties end up inside it."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha" />
      </items>
      """, ActionBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains(
      """<property class="Action0"><property name="Sound_start" value="read_mod" />""" +
      """<property name="Sound_in_head" value="true" /></property>""", result.Xml);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void An_existing_block_is_recursed_into_rather_than_duplicated() {
    // Same doc clause, other branch: Action0 already exists, so the inner properties are ensured inside the one
    // that is there. One Action0, and its own attributes survive.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0" delay="1">
            <property name="Sound_start" value="old" />
          </property>
        </item>
      </items>
      """, ActionBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property class="Action0" delay="1">""", result.Xml);
    Assert.Contains("""<property name="Sound_start" value="read_mod" />""", result.Xml);
    Assert.Contains("""<property name="Sound_in_head" value="true" />""", result.Xml);
    Assert.DoesNotContain("""value="old" """, result.Xml);
    Assert.Equal(1, Count(result.Xml, "Action0"));
  }

  [Fact]
  public void A_child_the_template_does_not_mention_survives() {
    // Doc: "A child already sitting inside Action0 that your template doesn't mention stays exactly where it is."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0">
            <property name="Sound_end" value="keepme" />
          </property>
        </item>
      </items>
      """, ActionBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Sound_end" value="keepme" />""", result.Xml);
    Assert.Contains("""<property name="Sound_start" value="read_mod" />""", result.Xml);
  }

  [Fact]
  public void Recursion_goes_as_deep_as_the_template_does() {
    // Doc reference table: "Ensures child inside tag, by the same rules, at any depth."
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0">
            <property name="Inner" tier="1">
              <property name="Deep" value="old" />
            </property>
          </property>
        </item>
      </items>
      """, """
      <ensure xpath="/items/item[@name='alpha']">
        <property class="Action0">
          <property name="Inner">
            <property name="Deep" value="new" />
          </property>
        </property>
      </ensure>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<property name="Inner" tier="1">""", result.Xml);
    Assert.Contains("""<property name="Deep" value="new" />""", result.Xml);
    Assert.DoesNotContain("""value="old" """, result.Xml);
  }

  [Fact]
  public void Some_inner_children_present_and_some_absent_all_end_up_right() {
    // The mixed case the two branches above cover separately — one merge and one create inside the same block.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="alpha">
          <property class="Action0">
            <property name="Sound_start" value="old" />
          </property>
        </item>
      </items>
      """, ActionBlock);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Equal(1, Count(result.Xml, "Sound_start"));
    Assert.Equal(1, Count(result.Xml, "Sound_in_head"));
  }

  [Fact]
  public void A_nested_block_applied_twice_changes_nothing_the_second_time() {
    // Idempotency has to hold at every level, not just the top one: the created clone and a merged element must
    // agree in shape, or the second pass would differ from the first.
    PatcherHost host = PatcherHost.Instance.Value;
    PatchOutcome once = host.Apply("""
      <items>
        <item name="alpha" />
      </items>
      """, ActionBlock);
    PatchOutcome twice = host.Apply(once.Xml, ActionBlock);

    Assert.True(twice.Applied, string.Join("\n", twice.Logs));
    Assert.Equal(once.Xml, twice.Xml);
    Assert.Empty(twice.Warnings);
  }

  private static int Count(string haystack, string needle) {
    var count = 0;
    for (var at = haystack.IndexOf(needle, System.StringComparison.Ordinal); at >= 0;
         at = haystack.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal)) {
      count++;
    }

    return count;
  }
}

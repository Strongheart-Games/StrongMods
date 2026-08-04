using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md, "Tables: &lt;bind&gt;" and "The default row". The
///   cross-file form (<c>source=</c>) needs the breadth-first patcher's cache and is covered with the rest of
///   cross-file resolution in wave C.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class BindTests {
  private const string TwoMeshes = """
    <items>
      <item name="alpha"><property name="Mesh" value="duffle" /></item>
      <item name="beta"><property name="Mesh" value="sports" /></item>
    </items>
    """;

  [Fact]
  public void Inline_rows_are_a_node_set_looked_up_by_predicate() {
    // Spec: "A bind holds a set of nodes, and that's the point: $loot IS the rows. Look one up by filtering
    // the variable with a predicate, then take the column you want."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="loot">
          <row mesh="duffle" icon="cntDuffle01" />
          <row mesh="sports" icon="cntSports02" />
        </bind>
        <append xpath="/items">
          <icon for="{$item/@name}" value="{$loot[@mesh = $item/property[@name='Mesh']/@value]/@icon}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<icon for="alpha" value="cntDuffle01" />""", result.Xml);
    Assert.Contains("""<icon for="beta" value="cntSports02" />""", result.Xml);
  }

  [Fact]
  public void Xpath_without_source_selects_from_the_target_document() {
    // Spec reference: bind's source "defaults to the patch's own target file"; xpath is valid "alone".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <bind name="everything" xpath="/items/item" />
        <append xpath="/items">
          <counted rows="{count($everything)}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<counted rows="2" />""", result.Xml);
  }

  [Fact]
  public void Resolved_once_before_the_loop_and_constant_across_iterations() {
    // Spec: "resolved once before the loop starts, and constant across iterations" — the body adds <item>
    // elements the bind's own xpath would match, so a per-iteration re-resolve would show a growing count.
    // The loop's own node set is likewise fixed ("Runs once, in document order"): exactly two iterations.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="everything" xpath="/items/item" />
        <append xpath="/items">
          <item name="added" />
        </append>
        <append xpath="/items">
          <snapshot rows="{count($everything)}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Equal(2, Occurrences(result.Xml, """<snapshot rows="2" />"""));
    Assert.DoesNotContain("""<snapshot rows="3" />""", result.Xml);
    // Both appends really landed, so a re-resolving bind would have counted 3 then 4 — the test cannot pass
    // by the body having done nothing.
    Assert.Equal(2, Occurrences(result.Xml, """<item name="added" />"""));
  }

  [Fact]
  public void Inline_children_together_with_xpath_are_an_error() {
    // Spec: "One form or the other — inline children plus source/xpath on the same bind is an error."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="loot" xpath="/items/item">
          <row mesh="duffle" icon="cntDuffle01" />
        </bind>
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("has both inline content and source/xpath"));
  }

  [Fact]
  public void A_bind_name_colliding_with_the_loop_binding_is_an_error() {
    // Spec: "The name joins the same scope as as names (collisions are errors)."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="item">
          <row mesh="duffle" />
        </bind>
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("collides with a binding, bind, or function"));
  }

  [Fact]
  public void No_matching_row_skips_the_iteration() {
    // Spec: "The exactly-one rule applies at the lookup: no matching row skips the iteration."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="loot">
          <row mesh="duffle" icon="cntDuffle01" />
        </bind>
        <append xpath="/items">
          <icon for="{$item/@name}" value="{$loot[@mesh = $item/property[@name='Mesh']/@value]/@icon}" />
        </append>
      </foreach>
      """);

    Assert.Contains("""<icon for="alpha" value="cntDuffle01" />""", result.Xml);
    Assert.DoesNotContain("""for="beta""", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void Two_rows_with_the_same_key_skip_the_iteration() {
    // Spec: "two rows with the same key skip it too — check the log if a table misbehaves."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <bind name="loot">
          <row mesh="duffle" icon="first" />
          <row mesh="duffle" icon="second" />
        </bind>
        <append xpath="/items">
          <icon value="{$loot[@mesh = $item/property[@name='Mesh']/@value]/@icon}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<icon", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void The_default_row_is_reachable_only_through_the_fallback() {
    // Spec: "The default row has no mesh attribute, so the key predicate can never match it — it's only
    // reachable through the ?: fallback. This is the idiom for 'look it up, or use the default.'"
    PatchOutcome result = PatcherHost.Instance.Value.Apply(TwoMeshes, """
      <foreach xpath="/items/item" as="item">
        <bind name="loot">
          <row mesh="duffle" icon="cntDuffle01" />
          <row default="true" icon="cntFallback" />
        </bind>
        <append xpath="/items">
          <icon for="{$item/@name}"
                value="{$loot[@mesh = $item/property[@name='Mesh']/@value]/@icon ?: $loot[@default='true']/@icon}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<icon for="alpha" value="cntDuffle01" />""", result.Xml);
    Assert.Contains("""<icon for="beta" value="cntFallback" />""", result.Xml);
  }

  private static int Occurrences(string haystack, string needle) {
    var count = 0;
    for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
         i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal)) {
      count++;
    }

    return count;
  }
}

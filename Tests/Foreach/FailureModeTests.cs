using System;
using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md, "When it doesn't work" and "Gotchas". The section's whole
///   point is the split between the two failure kinds, so each test asserts which one happened: a <b>skip</b>
///   abandons one iteration and lets the rest run (a data condition), an <b>error</b> kills the whole loop (a
///   mod bug). Rows already covered by the earlier waves — zero/multiple matches, function null and throw,
///   chained <c>?:</c>, bind with both forms, untagged method, unresolvable method, name collisions, missing
///   <c>as</c> — are not repeated here.
/// </summary>
[Collection(GameRoomCollection.Name)]
public class FailureModeTests {
  private const string TwoItems = """
    <items>
      <item name="alpha" tier="1" />
      <item name="9bad" tier="2" />
    </items>
    """;

  [Fact]
  public void An_invalid_substituted_element_name_skips_only_that_iteration() {
    // Table row: "Substituted element name isn't valid XML | Skip" — a skip, not an error, so the well-named
    // node still produces its element.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <placeholder foreach-name="{$item/@name}" />
        </append>
      </foreach>
      """);

    Assert.Contains("<alpha />", result.Xml);
    Assert.DoesNotContain("9bad />", result.Xml);
    Assert.DoesNotContain("placeholder", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("produced invalid element name"));
  }

  [Fact]
  public void An_unknown_source_on_foreach_is_an_error() {
    // Table row: "Unknown source file, on <foreach> or <bind> | Error".
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach source="no_such_file" xpath="/items/item" as="item">
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("unknown source document"));
  }

  [Fact]
  public void An_unknown_source_on_bind_is_an_error() {
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <bind name="rows" source="no_such_file" xpath="/items/item" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("unknown source document"));
  }

  [Fact]
  public void A_bind_with_neither_inline_content_nor_xpath_is_an_error() {
    // Table row: "<bind> with both inline content and source/xpath, OR NEITHER | Error".
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <bind name="rows" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("has neither inline content nor an xpath attribute"));
  }

  [Fact]
  public void A_bad_xpath_is_an_error() {
    // Table row: "Bad xpath ... | Error" — and the mechanism differs by where it sits. On the <foreach>
    // itself, vanilla dispatch evaluates the xpath before the engine is ever called (the layer that also
    // enforces xpath's presence), so it throws rather than logging.
    Exception onForeach = Assert.ThrowsAny<Exception>(() => GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[" as="item">
        <append xpath="/items"><nope /></append>
      </foreach>
      """));
    Assert.Contains("XPath", onForeach.Message);

    // On a body command the engine owns the failure.
    PatchOutcome onBody = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items/item["><nope /></append>
      </foreach>
      """);
    Assert.False(onBody.Applied);
    Assert.DoesNotContain("<nope", onBody.Xml);
    Assert.Contains(onBody.Errors, e => e.Contains("<append> failed") && e.Contains("aborting foreach"));
  }

  [Fact]
  public void An_unbound_name_is_an_error() {
    // Table row: "$name that isn't bound; unknown function | Error" — an error even though only some nodes
    // might have been affected, because it would fail identically for every one.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <nope value="{$notBound/@name}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("is not a bound name in the current foreach scope"));
  }

  [Fact]
  public void An_unknown_function_is_an_error() {
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <nope value="{noSuchFunction($item/@name)}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("unknown XPath function"));
  }

  [Fact]
  public void A_malformed_expression_is_an_error() {
    // Table row: "Malformed expression: unbalanced brackets, unterminated quote, chained ?: | Error".
    PatchOutcome unbalanced = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <nope value="{$item/property[@name='Tier'/@value}" />
        </append>
      </foreach>
      """);
    PatchOutcome unterminated = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <nope value="{$item/property[@name='Tier]/@value}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", unbalanced.Xml);
    Assert.Contains(unbalanced.Errors, e => e.Contains("has unbalanced brackets"));
    Assert.DoesNotContain("<nope", unterminated.Xml);
    Assert.Contains(unterminated.Errors, e => e.Contains("unterminated"));
  }

  [Fact]
  public void A_wrong_argument_count_is_an_error() {
    // Table row: "Function ... wrong argument count | Error". Join takes two.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="join" method="Tests.FunctionMod.PatchFunctions.Join, Tests.FunctionMod" />
        <append xpath="/items">
          <nope value="{join($item/@name)}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("which takes 2"));
  }

  [Fact]
  public void A_wrong_signature_is_an_error() {
    // Table row: "Function ... wrong signature | Error". NotAString is tagged but returns int.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="len" method="Tests.FunctionMod.PatchFunctions.NotAString, Tests.FunctionMod" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("patch functions must return string"));
  }

  [Fact]
  public void Function_arguments_may_not_use_a_fallback_or_nest_calls() {
    // Spec, "Custom functions": arguments are "anything you could write inside {...}, minus ?: and calls to
    // other functions."
    PatchOutcome withFallback = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Tests.FunctionMod.PatchFunctions.Upper, Tests.FunctionMod" />
        <append xpath="/items">
          <nope value="{up($item/@missing ?: $item/@name)}" />
        </append>
      </foreach>
      """);
    PatchOutcome nested = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Tests.FunctionMod.PatchFunctions.Upper, Tests.FunctionMod" />
        <append xpath="/items">
          <nope value="{up(up($item/@name))}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", withFallback.Xml);
    Assert.Contains(withFallback.Errors, e => e.Contains("failed to evaluate"));
    Assert.DoesNotContain("<nope", nested.Xml);
    Assert.Contains(nested.Errors, e => e.Contains("nested calls are not supported"));
  }

  [Fact]
  public void Extends_is_not_resolved_by_predicates() {
    // Gotcha: "property[@name='Class'] matches a property written on that element. If an entity class
    // inherits Class from a parent via Extends, your predicate won't match it."
    PatchOutcome result = GameRoom.Instance.Value.Apply("""
      <entity_classes>
        <entity_class name="parent"><property name="Class" value="Loot" /></entity_class>
        <entity_class name="child" extends="parent" />
      </entity_classes>
      """, """
      <foreach xpath="/entity_classes/entity_class[property[@name='Class'][@value='Loot']]" as="entity">
        <append xpath="/entity_classes">
          <matched name="{$entity/@name}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<matched name="parent" />""", result.Xml);
    Assert.DoesNotContain("""<matched name="child" />""", result.Xml);
  }
}

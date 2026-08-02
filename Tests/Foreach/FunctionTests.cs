using Tests.Fixtures;
using Xunit;

namespace Tests.Foreach;

/// <summary>
///   Conformance with StrongMods\Docs\foreach.md, "Custom functions", "The method reference" and "Writing
///   one". The functions called here live in Tests\FunctionMod, a real mod assembly registered with the game
///   as loaded — the same path a modder's own mod takes.
/// </summary>
[Collection(GameRoomCollection.Name)]
public class FunctionTests {
  private const string TwoItems = """
    <items>
      <item name="alpha" tier="1" />
      <item name="beta" tier="2" />
    </items>
    """;

  [Fact]
  public void A_declared_function_is_callable_from_an_expression() {
    // Spec: "<function name="tint" method="..." />" then "{tint($lootContainer/@name)}".
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Tests.FunctionMod.PatchFunctions.Upper, Tests.FunctionMod" />
        <append xpath="/items">
          <shout value="{up($item/@name)}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<shout value="ALPHA" />""", result.Xml);
    Assert.Contains("""<shout value="BETA" />""", result.Xml);
  }

  [Fact]
  public void Arguments_are_ordinary_expressions() {
    // Spec: "Arguments are ordinary XPath expressions — anything you could write inside {...}".
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <function name="join" method="Tests.FunctionMod.PatchFunctions.Join, Tests.FunctionMod" />
        <append xpath="/items">
          <joined value="{join($item/@name, $item/@tier)}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<joined value="alpha-1" />""", result.Xml);
  }

  [Fact]
  public void A_function_returning_null_skips_the_iteration() {
    // XmlPatchFunctionAttribute's contract: "Returning null tells the patcher to skip the current iteration,
    // the same verdict an XPath expression matching no nodes produces."
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="nothing" method="Tests.FunctionMod.PatchFunctions.Nothing, Tests.FunctionMod" />
        <append xpath="/items">
          <nope value="{nothing($item/@name)}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void A_null_return_falls_through_the_fallback() {
    // Spec: "{left ?: right} evaluates right only when left selects no nodes (or a called function returns
    // null)."
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item[@name='alpha']" as="item">
        <function name="nothing" method="Tests.FunctionMod.PatchFunctions.Nothing, Tests.FunctionMod" />
        <append xpath="/items">
          <fell value="{nothing($item/@name) ?: $item/@name}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("""<fell value="alpha" />""", result.Xml);
  }

  [Fact]
  public void A_throwing_function_skips_the_iteration() {
    // Engine contract: "a function throwing" skips the current iteration rather than escaping.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="boom" method="Tests.FunctionMod.PatchFunctions.Boom, Tests.FunctionMod" />
        <append xpath="/items">
          <nope value="{boom($item/@name)}" />
        </append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("skipped"));
  }

  [Fact]
  public void An_untagged_method_is_rejected_even_with_a_perfect_signature() {
    // Spec: "An untagged method is rejected even if the signature is perfect — that's deliberate, so nothing
    // in your assembly becomes XML-callable by accident."
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="plain" method="Tests.FunctionMod.PatchFunctions.Untagged, Tests.FunctionMod" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("XmlPatchFunction"));
  }

  [Fact]
  public void Omitting_the_mod_looks_in_Assembly_CSharp() {
    // Spec: "The mod is optional; leave it off and the game looks in Assembly-CSharp." The fixture mod's type
    // is not there, so the failure names where it looked.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Tests.FunctionMod.PatchFunctions.Upper" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("Assembly-CSharp"));
  }

  [Fact]
  public void A_reference_to_an_unloaded_mod_is_an_error() {
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Tests.FunctionMod.PatchFunctions.Upper, NoSuchMod" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("not loaded"));
  }

  [Fact]
  public void A_malformed_method_reference_is_an_error() {
    // Spec shape: "[namespace.]Class.Method, [mod]" — "Upper" alone has no class part.
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="up" method="Upper, Tests.FunctionMod" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("is not of the form"));
  }

  [Fact]
  public void A_function_name_colliding_with_the_loop_binding_is_an_error() {
    // Spec: a function's "name lives for that loop only, in the same scope as as names and binds."
    PatchOutcome result = GameRoom.Instance.Value.Apply(TwoItems, """
      <foreach xpath="/items/item" as="item">
        <function name="item" method="Tests.FunctionMod.PatchFunctions.Upper, Tests.FunctionMod" />
        <append xpath="/items"><nope /></append>
      </foreach>
      """);

    Assert.DoesNotContain("<nope", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("collides with a binding, bind, or function"));
  }
}

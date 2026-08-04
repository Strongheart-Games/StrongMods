using System;
using System.Collections.Generic;
using System.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.Ensure;

/// <summary>
///   Conformance with StrongMods\Docs\ensure.md, "When it doesn't work". The section's whole point is the split
///   between the two failure kinds, so each test asserts which one happened: a <b>warning</b> leaves one parent
///   alone and lets the rest run (a data condition), an <b>error</b> kills the whole block before anything is
///   written (a mod bug). Rows covered by IdentityTests — ambiguous key, unidentifiable template — are not
///   repeated here.
///   Runs on the default host, like Foreach\* (.ai\testing-declared-versions.md §3c).
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class FailureModeTests {
  private const string OneItem = """
    <items>
      <item name="alpha" />
    </items>
    """;

  [Fact]
  public void A_selector_matching_nothing_produces_the_ordinary_vanilla_warning() {
    // Doc table: "xpath matched nothing | The ordinary vanilla did not apply warning — a real one". That warning
    // belongs to XmlPatcher.PatchXml rather than to ensure, so this drives a whole patch file to see it.
    PatcherHost host = PatcherHost.Instance.Value;
    object target = host.CreateXmlFile(OneItem, "items.xml");
    IReadOnlyList<LogEntry> logs = host.ApplyPatchFile(target, """
      <config>
        <ensure xpath="/items/item[@name='noSuchItem']">
          <property name="Stacknumber" value="65000" />
        </ensure>
      </config>
      """, "items.xml");

    Assert.Contains(logs, l => l.Level == LogLevel.Warning && l.Message.Contains("did not apply"));
    Assert.DoesNotContain("Stacknumber", host.XmlOf(target));
  }

  [Fact]
  public void A_block_with_no_template_children_is_an_error() {
    // Doc table: "<ensure> has no template children | Error".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='alpha']" />
      """);

    Assert.False(result.Applied);
    Assert.Contains(result.Errors, e => e.Contains("no template children"));
  }

  [Fact]
  public void An_invalid_position_is_an_error() {
    // Doc table: "position is neither append nor prepend | Error" — and an error rather than a silent default,
    // because sibling order decides which value wins.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='alpha']" position="middle">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("Stacknumber", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("not a valid position"));
  }

  [Fact]
  public void An_ensure_key_naming_an_absent_attribute_is_an_error() {
    // Doc: "Every attribute it names must be present on the template, since those values are the key."
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='alpha']">
        <passive_effect ensure-key="name,operation" name="EntityDamage" value="0.5" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("passive_effect", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("carries no operation attribute"));
  }

  [Fact]
  public void An_empty_ensure_key_is_an_error() {
    // Doc table: "ensure-key is empty, or names an attribute the template lacks | Error".
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='alpha']">
        <property ensure-key="  " name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.DoesNotContain("Stacknumber", result.Xml);
    Assert.Contains(result.Errors, e => e.Contains("empty ensure-key"));
  }

  [Fact]
  public void A_malformed_xpath_throws_like_any_other_command() {
    // Doc table: "xpath attribute missing, or malformed | The patcher rejects it before <ensure> runs, as for any
    // command." Vanilla dispatch evaluates the xpath, so this surfaces as an exception, not a logged error —
    // the same shape as foreach's A_bad_xpath_is_an_error.
    Exception thrown = Assert.ThrowsAny<Exception>(() => PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """));

    Assert.Contains("XPath", thrown.Message);
  }

  [Fact]
  public void A_missing_xpath_is_rejected_by_the_patcher_itself() {
    // Same table row, other half: ensure is registered as a command that requires an xpath, so the patcher
    // throws before the engine is ever called.
    Exception thrown = Assert.ThrowsAny<Exception>(() => PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure>
        <property name="Stacknumber" value="65000" />
      </ensure>
      """));

    Assert.Contains("'xpath' attribute", thrown.Message);
  }

  [Fact]
  public void An_attribute_targeting_xpath_names_setattribute() {
    // Doc, "Ensuring an attribute": an attribute-targeting xpath can only ever select attributes that already
    // exist, so it cannot express add-if-absent. The diagnostic names the vanilla command that can, and — since
    // the matched attribute is in hand — names it exactly.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="bossAlpha" extends="oldBoss" />
      </items>
      """, """
      <ensure xpath="/items/item[starts-with(@name, 'boss')]/@extends">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.Contains("""extends="oldBoss" """.TrimEnd() + " />", result.Xml);
    Assert.Contains(result.Warnings, w => w.Contains("attribute node(s) rather than elements"));
    Assert.Contains(result.Warnings, w =>
      w.Contains("""<setattribute xpath="/items/item[starts-with(@name, 'boss')]" name="extends">"""));
  }

  [Fact]
  public void The_attribute_shape_authors_actually_write_names_setattribute() {
    // The exact patch someone writes when they expect <ensure> to cover attributes. It has no template children,
    // so it never reaches the xpath — the hint has to be attached to that error too, or the most likely spelling
    // of the mistake gets the least helpful message.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="bossAlpha" />
      </items>
      """, """
      <ensure xpath="/items/item[starts-with(@name, 'boss')]/@extends">masterBoss</ensure>
      """);

    Assert.False(result.Applied);
    Assert.Contains(result.Errors, e => e.Contains("no template children"));
    Assert.Contains(result.Errors, e =>
      e.Contains("""<setattribute xpath="/items/item[starts-with(@name, 'boss')]" name="extends">masterBoss"""));
  }

  [Fact]
  public void An_empty_block_is_not_mistaken_for_an_attribute_attempt() {
    // The hint keys on the block declaring a value. A genuinely empty block is a different mistake and must not
    // be told to go use setattribute.
    PatchOutcome result = PatcherHost.Instance.Value.Apply(OneItem, """
      <ensure xpath="/items/item[@name='alpha']/@extends" />
      """);

    Assert.False(result.Applied);
    Assert.Contains(result.Errors, e => e.Contains("no template children"));
    Assert.DoesNotContain(result.Errors, e => e.Contains("setattribute"));
  }

  [Fact]
  public void Non_element_matches_are_reported_once_for_the_block() {
    // Whether the xpath selected one attribute or forty, aiming a block at attributes is one mistake about the
    // block. Reporting it per node would bury a 40-item patch in identical warnings.
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="bossAlpha" extends="a" />
        <item name="bossBeta" extends="b" />
        <item name="bossGamma" extends="c" />
      </items>
      """, """
      <ensure xpath="/items/item/@extends">
        <property name="Stacknumber" value="65000" />
      </ensure>
      """);

    Assert.False(result.Applied);
    Assert.Single(result.Warnings);
    Assert.Contains("3 attribute node(s)", result.Warnings.First());
  }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.Inheritance;

/// <summary>Conformance for StrongMods\Docs\inheritance.md's chain and effective-value basics.</summary>
[Collection(PatcherHostCollection.Name)]
public class InheritanceFunctionTests {
  [Fact]
  public void Inherited_returns_the_nearest_declared_or_inherited_value() {
    List<XObject> matches = Matches("""
      <items>
        <item name="root"><property name="Class" value="LootContainer" /></item>
        <item name="middle"><property name="Extends" value="root" /><property name="Class" value="Weapon" /></item>
        <item name="leaf"><property name="Extends" value="middle" /></item>
      </items>
      """, "/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'Weapon']");

    Assert.Equal(new[] { "middle", "leaf" },
      matches.Cast<XElement>().Select(e => (string)e.Attribute("name")));
  }

  [Fact]
  public void Chain_includes_self_and_walks_transitively() {
    List<XObject> matches = Matches("""
      <items>
        <item name="root" />
        <item name="middle"><property name="Extends" value="root" /></item>
        <item name="leaf"><property name="Extends" value="middle" /></item>
      </items>
      """, "/items/item[sm:chain(., '#Extends', '@name')[@name='root']]");

    Assert.Equal(new[] { "root", "middle", "leaf" }, matches.Cast<XElement>().Select(e => (string)e.Attribute("name")));
  }

  [Fact]
  public void Explicit_population_and_general_link_form_work() {
    List<XObject> matches = Matches("""
      <document>
        <parents><entry id="base"><value state="ready" /></entry></parents>
        <children><entry parent="base" /></children>
      </document>
      """, "/document/children/entry[sm:inherited(., 'value/@state', '@parent', '@id', '/document/parents/entry') = 'ready']");

    Assert.Single(matches);
  }

  [Fact]
  public void Marked_xpath_runs_through_the_real_xml_file_selector_funnel() {
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="root"><property name="Class" value="LootContainer" /></item>
        <item name="child"><property name="Extends" value="root" /></item>
      </items>
      """, """
      <setattribute xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']"
                    name="matched">yes</setattribute>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("<item name=\"root\" matched=\"yes\">", result.Xml);
    Assert.Contains("<item name=\"child\" matched=\"yes\">", result.Xml);
  }

  [Fact]
  public void Empty_node_argument_returns_an_empty_node_set_without_an_error() {
    PatcherHost host = PatcherHost.Instance.Value;
    Assembly strongMods = host.StrongModsAssembly;
    MethodInfo method = strongMods.GetType("StrongMods.XPathInheritance")!
      .GetMethod("TryGetMatches", BindingFlags.Public | BindingFlags.Static)!;
    var matches = new List<XObject>();

    var applied = (bool)method.Invoke(null, new object[] {
      XDocument.Parse("<items><item name=\"root\" /></items>"),
      "/items/item[sm:chain(./missing, '#Extends', '@name')]", matches
    })!;

    Assert.False(applied);
    Assert.Empty(matches);
  }

  [Fact]
  public void Invalid_argument_cardinality_and_population_are_logged_as_xpath_errors() {
    PatchOutcome multipleNodes = PatcherHost.Instance.Value.Apply("""
      <items><item name="one" /><item name="two" /></items>
      """, """
      <setattribute xpath="sm:chain(/items/item, '#Extends', '@name')" name="matched">yes</setattribute>
      """);
    PatchOutcome relativePopulation = PatcherHost.Instance.Value.Apply("""
      <items><item name="one" /></items>
      """, """
      <setattribute xpath="/items/item[sm:chain(., '#Extends', '@name', 'items/item')]" name="matched">yes</setattribute>
      """);

    // XPath wraps extension-function exceptions as "Function ... has failed" before the selector funnel logs them.
    // Both must still fail closed rather than apply a partial patch.
    Assert.Contains(multipleNodes.Errors, error => error.Contains("sm:chain") && error.Contains("failed to evaluate"));
    Assert.Contains(relativePopulation.Errors, error => error.Contains("sm:chain") && error.Contains("failed to evaluate"));
  }

  [Fact]
  public void Broken_chain_data_warns_and_does_not_hide_unrelated_matches() {
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="valid"><property name="Class" value="LootContainer" /></item>
        <item name="missing"><property name="Extends" value="does-not-exist" /></item>
        <item name="ambiguous"><property name="Extends" value="valid" /><property name="Extends" value="other" /></item>
        <item name="duplicate"><property name="Class" value="LootContainer" /></item>
        <item name="duplicate"><property name="Class" value="LootContainer" /></item>
        <item name="duplicateChild"><property name="Extends" value="duplicate" /></item>
        <item name="cycleA"><property name="Extends" value="cycleB" /></item>
        <item name="cycleB"><property name="Extends" value="cycleA" /></item>
      </items>
      """, """
      <setattribute xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']"
                    name="matched">yes</setattribute>
      """);

    Assert.Contains("<item name=\"valid\" matched=\"yes\">", result.Xml);
    Assert.Contains(result.Warnings, warning => warning.Contains("missing parent key \"does-not-exist\""));
    Assert.Contains(result.Warnings, warning => warning.Contains("has 2 parent links"));
    Assert.Contains(result.Warnings, warning => warning.Contains("parent key \"duplicate\" is duplicated"));
    Assert.Contains(result.Warnings, warning => warning.Contains("cycle while resolving"));
  }

  [Fact]
  public void Foreach_source_selector_resolves_inheritance_in_the_source_document() {
    PatchOutcome result = PatcherHost.Instance.Value.Apply("<items />", """
      <foreach source="entityclasses"
               xpath="/entity_classes/entity_class[sm:inherited(., '#Class', '@extends', '@name') = 'EntityLootContainer']"
               as="container">
        <append xpath="/items"><item name="{$container/@name}" /></append>
      </foreach>
      """, new Dictionary<string, string> {
        ["entityclasses"] = """
          <entity_classes>
            <entity_class name="base"><property name="Class" value="EntityLootContainer" /></entity_class>
            <entity_class name="child" extends="base" />
          </entity_classes>
          """
      });

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("<item name=\"base\" />", result.Xml);
    Assert.Contains("<item name=\"child\" />", result.Xml);
  }

  [Fact]
  public void Foreach_interpolation_resolves_inheritance_over_a_bound_node() {
    PatchOutcome result = PatcherHost.Instance.Value.Apply("""
      <items>
        <item name="base"><property name="Class" value="LootContainer" /></item>
        <item name="child"><property name="Extends" value="base" /></item>
      </items>
      """, """
      <foreach xpath="/items/item" as="item">
        <append xpath="/items">
          <effective name="{$item/@name}"
                     class="{sm:inherited($item, '#Class', '#Extends', '@name')/parent::property/@value}" />
        </append>
      </foreach>
      """);

    Assert.True(result.Applied, string.Join("\n", result.Logs));
    Assert.Contains("<effective name=\"base\" class=\"LootContainer\" />", result.Xml);
    Assert.Contains("<effective name=\"child\" class=\"LootContainer\" />", result.Xml);
  }

  [Fact]
  public void Separate_commands_rebuild_the_index_after_the_document_changes() {
    PatcherHost host = PatcherHost.Instance.Value;
    object items = host.CreateXmlFile("""
      <items>
        <item name="base"><property name="Class" value="OldClass" /></item>
        <item name="child"><property name="Extends" value="base" /></item>
      </items>
      """, "items.xml");

    IReadOnlyList<LogEntry> logs = host.ApplyPatchFile(items, """
      <config>
        <set xpath="/items/item[@name='base']/property[@name='Class']/@value">LootContainer</set>
        <setattribute xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']"
                      name="matched">yes</setattribute>
      </config>
      """, "items.xml");

    Assert.True(logs.Count == 0, string.Join("\n", logs) + "\n" + host.XmlOf(items));
    Assert.Contains("<item name=\"child\" matched=\"yes\">", host.XmlOf(items));
  }

  private static List<XObject> Matches(string xml, string xpath) {
    PatcherHost host = PatcherHost.Instance.Value;
    Assembly strongMods = host.StrongModsAssembly;
    MethodInfo method = strongMods.GetType("StrongMods.XPathInheritance")!
      .GetMethod("TryGetMatches", BindingFlags.Public | BindingFlags.Static)!;
    var matches = new List<XObject>();
    var applied = (bool)method.Invoke(null, new object[] { XDocument.Parse(xml), xpath, matches })!;
    Assert.True(applied);
    return matches;
  }
}

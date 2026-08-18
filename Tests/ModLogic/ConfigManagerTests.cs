using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongUtils' ConfigManager — the on-disk config store every other StrongUtils feature registers with.
///   It needs no world, only a directory, so it runs for real in a <see cref="ModLogicHost" /> against a
///   scratch directory. The one game type it touches is <c>Log</c>, which the stubbed universe satisfies.
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class ConfigManagerTests : IDisposable {
  private readonly Type configManager;
  private readonly string root;
  private readonly object instance;

  public ConfigManagerTests() {
    ModLogicHost host = ModLogicHost.For("StrongUtils");
    configManager = host.ModType("StrongUtils.ConfigManager");
    root = Path.Combine(Path.GetTempPath(), "strongmods-cfg-" + Guid.NewGuid().ToString("N"));
    Reset();
    ModLogicHost.CallStatic(configManager, "Init", root);
    instance = ModLogicHost.GetStatic(configManager, "Instance");
  }

  public void Dispose() {
    Reset();
    if (Directory.Exists(root)) {
      Directory.Delete(root, true);
    }
  }

  /// <summary>Instance is a process-wide singleton, so a previous test's leftover must go first.</summary>
  private void Reset() {
    if (ModLogicHost.GetStatic(configManager, "Instance") is { } live) {
      ModLogicHost.Call(live, "Dispose");
    }
  }

  [Fact]
  public void Init_creates_the_directory_when_it_does_not_exist() {
    Assert.True(Directory.Exists(root));
  }

  [Fact]
  public void Init_a_second_time_is_rejected() {
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () => ModLogicHost.CallStatic(configManager, "Init", root));
    Assert.Contains("already been initialized", error.Message);
  }

  [Fact]
  public void Init_rejects_a_blank_directory() {
    Reset();
    Assert.Throws<ArgumentException>(() => ModLogicHost.CallStatic(configManager, "Init", "   "));
  }

  [Fact]
  public void Register_writes_the_default_contents_when_the_file_is_absent() {
    Register("greeting.xml", "<config><hello /></config>");

    Assert.Equal("<config><hello /></config>", File.ReadAllText(Path.Combine(root, "greeting.xml")));
  }

  [Fact]
  public void Register_leaves_an_existing_file_alone() {
    var path = Path.Combine(root, "existing.xml");
    File.WriteAllText(path, "<config><kept /></config>");

    Register("existing.xml", "<config><default /></config>");

    Assert.Equal("<config><kept /></config>", File.ReadAllText(path));
  }

  [Fact]
  public void Register_creates_intermediate_directories() {
    Register("nested/deeper/file.xml");

    Assert.True(File.Exists(Path.Combine(root, "nested", "deeper", "file.xml")));
  }

  [Fact]
  public void Register_rejects_an_absolute_path() {
    Assert.Throws<ArgumentException>(() => Register(Path.Combine(root, "rooted.xml")));
  }

  [Fact]
  public void Register_rejects_null_default_contents() {
    Assert.Throws<ArgumentNullException>(
      () => ModLogicHost.Call(instance, "RegisterConfigFile", "n.xml", null, null));
  }

  /// <summary>
  ///   Registration keys are normalized (case-folded, separators unified), so these three spellings are one
  ///   file. The second and third registrations are ignored — including their default contents.
  /// </summary>
  [Theory]
  [InlineData("Sub/Case.xml")]
  [InlineData("sub\\case.xml")]
  [InlineData("SUB/CASE.XML")]
  public void Register_treats_case_and_separator_variants_as_the_same_file(string variant) {
    Register("sub/case.xml", "<config><first /></config>");

    Register(variant, "<config><second /></config>");

    Assert.Equal("<config><first /></config>", File.ReadAllText(Path.Combine(root, "sub", "case.xml")));
  }

  [Fact]
  public void Write_then_read_round_trips_the_document() {
    Register("state.xml");

    ModLogicHost.Call(instance, "WriteConfigFile", "state.xml",
      XElement.Parse("<config><zone name=\"alpha\" /></config>"));
    var read = (XElement)ModLogicHost.Call(instance, "ReadConfigFile", "state.xml");

    Assert.Equal("alpha", read.Element("zone")!.Attribute("name")!.Value);
  }

  [Fact]
  public void Write_to_an_unregistered_file_is_rejected() {
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () => ModLogicHost.Call(instance, "WriteConfigFile", "stranger.xml", XElement.Parse("<config />")));
    Assert.Contains("is not registered", error.Message);
  }

  [Fact]
  public void Append_adds_to_the_existing_root_rather_than_replacing_it() {
    Register("list.xml", "<config><a /></config>");

    ModLogicHost.Call(instance, "AppendConfig", "list.xml", XElement.Parse("<b />"));

    var read = (XElement)ModLogicHost.Call(instance, "ReadConfigFile", "list.xml");
    Assert.Equal(new[] { "a", "b" }, read.Elements().Select(e => e.Name.LocalName));
  }

  [Fact]
  public void Append_to_an_unregistered_file_is_rejected() {
    Assert.Throws<InvalidOperationException>(
      () => ModLogicHost.Call(instance, "AppendConfig", "stranger.xml", XElement.Parse("<b />")));
  }

  [Fact]
  public void Remove_deletes_the_deep_equal_child() {
    Register("list.xml", "<config><cmd command=\"a\" /><cmd command=\"b\" /></config>");

    ModLogicHost.Call(instance, "RemoveConfig", "list.xml", XElement.Parse("<cmd command=\"a\" />"));

    var read = (XElement)ModLogicHost.Call(instance, "ReadConfigFile", "list.xml");
    Assert.Equal("b", Assert.Single(read.Elements()).Attribute("command")!.Value);
  }

  /// <summary>Deep equality, not identity: only ONE of two identical children goes per call.</summary>
  [Fact]
  public void Remove_deletes_a_single_child_when_duplicates_exist() {
    Register("dupes.xml", "<config><cmd command=\"a\" /><cmd command=\"a\" /></config>");

    ModLogicHost.Call(instance, "RemoveConfig", "dupes.xml", XElement.Parse("<cmd command=\"a\" />"));

    var read = (XElement)ModLogicHost.Call(instance, "ReadConfigFile", "dupes.xml");
    Assert.Single(read.Elements());
  }

  [Fact]
  public void Remove_of_an_absent_child_leaves_the_document_untouched() {
    Register("list.xml", "<config><a /></config>");

    ModLogicHost.Call(instance, "RemoveConfig", "list.xml", XElement.Parse("<z />"));

    var read = (XElement)ModLogicHost.Call(instance, "ReadConfigFile", "list.xml");
    Assert.Equal("a", Assert.Single(read.Elements()).Name.LocalName);
  }

  /// <summary>Dispose is what lets a server reload: it drops the singleton so Init can run again.</summary>
  [Fact]
  public void Dispose_releases_the_singleton() {
    ModLogicHost.Call(instance, "Dispose");

    Assert.Null(ModLogicHost.GetStatic(configManager, "Instance"));
    ModLogicHost.CallStatic(configManager, "Init", root);
    Assert.NotNull(ModLogicHost.GetStatic(configManager, "Instance"));
  }

  private void Register(string filename, string defaultContents = "<config></config>") =>
    ModLogicHost.Call(instance, "RegisterConfigFile", filename, defaultContents, null);
}

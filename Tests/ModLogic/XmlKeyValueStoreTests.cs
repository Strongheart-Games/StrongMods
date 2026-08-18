using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongUtils' <c>XmlKeyValueStore</c> — the persistent key-value store behind <c>KeyValueStore.Instance</c>.
///   Pure BCL over a file plus <c>Log</c>, so it runs for real in the stubbed universe against a scratch file.
///   The feature is dormant this season (its callers are commented out), but the code ships in the DLL, so it
///   is worth pinning before anything switches it on.
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class XmlKeyValueStoreTests : IDisposable {
  private readonly Type storeType;
  private readonly Type varType;
  private readonly string path;
  private readonly object store;

  public XmlKeyValueStoreTests() {
    ModLogicHost host = ModLogicHost.For("StrongUtils");
    storeType = host.ModType("StrongUtils.KeyValueStore.XmlKeyValueStore");
    varType = host.ModType("StrongUtils.KeyValueStore.VarType");
    path = Path.Combine(Path.GetTempPath(), "strongmods-kv-" + Guid.NewGuid().ToString("N") + ".xml");
    store = Open();
  }

  public void Dispose() {
    if (File.Exists(path)) {
      File.Delete(path);
    }
  }

  private object Open() => Activator.CreateInstance(storeType, path);

  [Fact]
  public void A_missing_file_starts_an_empty_store() {
    Assert.Empty(Keys(store));
    Assert.False((bool)ModLogicHost.Call(store, "Contains", "anything"));
  }

  [Fact]
  public void A_null_path_is_rejected() {
    TargetInvocationException wrapper =
      Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(storeType, new object[] { null }));
    Assert.IsType<ArgumentNullException>(wrapper.InnerException);
  }

  [Fact]
  public void An_absent_key_reads_back_as_the_default() {
    Assert.Equal(42, Get<int>("nope", 42));
    Assert.Null(ModLogicHost.Call(store, "GetRaw", "nope"));
    Assert.Null(ModLogicHost.Call(store, "GetVarType", "nope"));
  }

  [Theory]
  [InlineData("String", "hello")]
  [InlineData("Int", 7)]
  [InlineData("Long", 8L)]
  [InlineData("Float", 1.5f)]
  [InlineData("Double", 2.25d)]
  [InlineData("Bool", true)]
  public void Each_supported_type_round_trips_and_records_its_tag(string tag, object value) {
    ModLogicHost.Call(store, "Set", "k", value);

    Assert.Equal(Enum.Parse(varType, tag), ModLogicHost.Call(store, "GetVarType", "k"));
    Assert.Equal(value, ModLogicHost.CallGeneric(store, "Get", new[] { value.GetType() }, "k", null));
  }

  [Fact]
  public void A_value_of_the_wrong_shape_falls_back_to_the_default() {
    ModLogicHost.Call(store, "Set", "word", "not-a-number");

    Assert.Equal(-1, Get("word", -1));
  }

  [Fact]
  public void Setting_an_existing_key_overwrites_it() {
    ModLogicHost.Call(store, "Set", "k", 1);
    ModLogicHost.Call(store, "Set", "k", 2);

    Assert.Equal(2, Get("k", 0));
    Assert.Single(Keys(store));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void A_blank_key_is_rejected(string key) {
    Assert.Throws<ArgumentException>(() => ModLogicHost.Call(store, "Set", key, "v"));
    Assert.Throws<ArgumentException>(() => ModLogicHost.Call(store, "TestAndSet", key, "a", "b"));
  }

  [Fact]
  public void Keys_are_case_sensitive() {
    ModLogicHost.Call(store, "Set", "Key", 1);

    Assert.False((bool)ModLogicHost.Call(store, "Contains", "key"));
  }

  [Fact]
  public void Removing_drops_the_key_and_leaves_the_others() {
    ModLogicHost.Call(store, "Set", "gone", 1);
    ModLogicHost.Call(store, "Set", "kept", 2);

    ModLogicHost.Call(store, "Remove", "gone");

    Assert.Equal(new[] { "kept" }, Keys(store));
  }

  [Fact]
  public void Removing_an_absent_key_is_a_no_op() {
    ModLogicHost.Call(store, "Remove", "never-there");

    Assert.Empty(Keys(store));
  }

  [Fact]
  public void Clearing_empties_the_store() {
    ModLogicHost.Call(store, "Set", "a", 1);
    ModLogicHost.Call(store, "Set", "b", 2);

    ModLogicHost.Call(store, "Clear");

    Assert.Empty(Keys(store));
  }

  // ---------------------------------------------------------------------------------------------
  // Test-and-set — the store's one concurrency primitive
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void Test_and_set_applies_when_the_current_value_matches() {
    ModLogicHost.Call(store, "Set", "counter", 1);

    Assert.True((bool)ModLogicHost.Call(store, "TestAndSet", "counter", 1, 2));
    Assert.Equal(2, Get("counter", 0));
  }

  [Fact]
  public void Test_and_set_declines_when_the_current_value_differs() {
    ModLogicHost.Call(store, "Set", "counter", 1);

    Assert.False((bool)ModLogicHost.Call(store, "TestAndSet", "counter", 9, 2));
    Assert.Equal(1, Get("counter", 0));
  }

  [Fact]
  public void Test_and_set_declines_on_an_absent_key() {
    Assert.False((bool)ModLogicHost.Call(store, "TestAndSet", "absent", 1, 2));
    Assert.False((bool)ModLogicHost.Call(store, "Contains", "absent"));
  }

  /// <summary>
  ///   The stored type tag is part of the comparison: an int 1 is not the string "1", even though both are
  ///   raw "1" on disk. This is what keeps a typed caller from clobbering another type's key.
  /// </summary>
  [Fact]
  public void Test_and_set_declines_when_the_stored_type_differs() {
    ModLogicHost.Call(store, "Set", "k", 1);

    Assert.False((bool)ModLogicHost.Call(store, "TestAndSet", "k", "1", "2"));
    Assert.Equal(Enum.Parse(varType, "Int"), ModLogicHost.Call(store, "GetVarType", "k"));
  }

  // ---------------------------------------------------------------------------------------------
  // Persistence
  // ---------------------------------------------------------------------------------------------

  /// <summary>Every mutation flushes, so a second store over the same path sees the first one's writes.</summary>
  [Fact]
  public void Values_survive_a_reopen() {
    ModLogicHost.Call(store, "Set", "name", "alpha");
    ModLogicHost.Call(store, "Set", "count", 3);

    object reopened = Open();

    Assert.Equal("alpha", ModLogicHost.CallGeneric(reopened, "Get", new[] { typeof(string) }, "name", null));
    Assert.Equal(3, ModLogicHost.CallGeneric(reopened, "Get", new[] { typeof(int) }, "count", 0));
    Assert.Equal(Enum.Parse(varType, "Int"), ModLogicHost.Call(reopened, "GetVarType", "count"));
  }

  [Fact]
  public void The_file_on_disk_is_one_entry_element_per_key() {
    ModLogicHost.Call(store, "Set", "k", 5);

    XElement root = XDocument.Load(path).Root!;
    Assert.Equal("Store", root.Name.LocalName);
    XElement entry = Assert.Single(root.Elements());
    Assert.Equal("Entry", entry.Name.LocalName);
    Assert.Equal("k", entry.Attribute("key")!.Value);
    Assert.Equal("Int", entry.Attribute("type")!.Value);
    Assert.Equal("5", entry.Attribute("value")!.Value);
  }

  [Fact]
  public void Removing_the_last_key_leaves_an_empty_store_element_behind() {
    ModLogicHost.Call(store, "Set", "k", 5);
    ModLogicHost.Call(store, "Remove", "k");

    Assert.Empty(XDocument.Load(path).Root!.Elements());
  }

  /// <summary>Reload is how a store picks up a file another writer changed underneath it.</summary>
  [Fact]
  public void Reload_replaces_memory_with_what_is_on_disk() {
    ModLogicHost.Call(store, "Set", "k", 1);
    File.WriteAllText(path,
      "<Store><Entry key=\"k\" type=\"Int\" value=\"99\" /><Entry key=\"extra\" type=\"String\" value=\"x\" /></Store>");

    ModLogicHost.Call(store, "Reload");

    Assert.Equal(99, Get("k", 0));
    Assert.Equal(new[] { "extra", "k" }, Keys(store).OrderBy(k => k));
  }

  [Fact]
  public void An_entry_with_a_blank_key_is_dropped_on_load() {
    File.WriteAllText(path,
      "<Store><Entry key=\"\" type=\"Int\" value=\"1\" /><Entry key=\"real\" type=\"Int\" value=\"2\" /></Store>");

    ModLogicHost.Call(store, "Reload");

    Assert.Equal(new[] { "real" }, Keys(store));
  }

  // ---------------------------------------------------------------------------------------------
  // Change notification
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void A_new_key_raises_Created_with_no_old_value() {
    List<object> seen = Observe(store);

    ModLogicHost.Call(store, "Set", "k", "v");

    object change = Assert.Single(seen);
    Assert.Equal("k", ModLogicHost.Call(change, "get_Key"));
    Assert.Null(ModLogicHost.Call(change, "get_OldRawValue"));
    Assert.Equal("v", ModLogicHost.Call(change, "get_NewRawValue"));
    Assert.Equal("Created", ModLogicHost.Call(change, "get_ChangeType").ToString());
  }

  [Fact]
  public void An_overwrite_raises_Updated_carrying_both_values() {
    ModLogicHost.Call(store, "Set", "k", "old");
    List<object> seen = Observe(store);

    ModLogicHost.Call(store, "Set", "k", "new");

    object change = Assert.Single(seen);
    Assert.Equal("old", ModLogicHost.Call(change, "get_OldRawValue"));
    Assert.Equal("new", ModLogicHost.Call(change, "get_NewRawValue"));
    Assert.Equal("Updated", ModLogicHost.Call(change, "get_ChangeType").ToString());
  }

  [Fact]
  public void A_removal_raises_Deleted_carrying_the_value_that_went() {
    ModLogicHost.Call(store, "Set", "k", "v");
    List<object> seen = Observe(store);

    ModLogicHost.Call(store, "Remove", "k");

    object change = Assert.Single(seen);
    Assert.Equal("v", ModLogicHost.Call(change, "get_OldRawValue"));
    Assert.Null(ModLogicHost.Call(change, "get_NewRawValue"));
    Assert.Equal("Deleted", ModLogicHost.Call(change, "get_ChangeType").ToString());
  }

  [Fact]
  public void A_declined_test_and_set_raises_nothing() {
    ModLogicHost.Call(store, "Set", "k", 1);
    List<object> seen = Observe(store);

    ModLogicHost.Call(store, "TestAndSet", "k", 9, 2);

    Assert.Empty(seen);
  }

  [Fact]
  public void Clear_raises_one_Deleted_per_key() {
    ModLogicHost.Call(store, "Set", "a", 1);
    ModLogicHost.Call(store, "Set", "b", 2);
    List<object> seen = Observe(store);

    ModLogicHost.Call(store, "Clear");

    Assert.Equal(2, seen.Count);
    Assert.All(seen, c => Assert.Equal("Deleted", ModLogicHost.Call(c, "get_ChangeType").ToString()));
  }

  private T Get<T>(string key, T fallback) where T : IConvertible =>
    (T)ModLogicHost.CallGeneric(store, "Get", new[] { typeof(T) }, key, fallback);

  private static string[] Keys(object target) =>
    ((IEnumerable)ModLogicHost.Call(target, "GetAllKeys")).Cast<string>().ToArray();

  /// <summary>
  ///   Subscribes to VarChanged and collects what it carries. The event's argument type lives in the mod's
  ///   own load context, so the handler takes <c>object</c> and relies on delegate contravariance to bind.
  /// </summary>
  private static List<object> Observe(object target) {
    var seen = new List<object>();
    EventInfo varChanged = target.GetType().GetEvent("VarChanged")!;
    MethodInfo collector = typeof(Collector).GetMethod(nameof(Collector.OnChanged))!;
    varChanged.AddEventHandler(target,
      Delegate.CreateDelegate(varChanged.EventHandlerType!, new Collector(seen), collector));
    return seen;
  }

  private sealed class Collector {
    private readonly List<object> seen;
    public Collector(List<object> seen) => this.seen = seen;
    public void OnChanged(object sender, object args) => seen.Add(args);
  }
}

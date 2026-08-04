using Tests.Fixtures;
using Xunit;

namespace Tests.Patcher;

/// <summary>
///   The breadth-first patcher's cache and its LoadAndPatchConfig prefix — the seam that lets the mod reorder
///   patching without rewriting the game's per-file pipeline (see the class doc on
///   <c>StrongMods.BreadthFirstXmlPatcher</c>). The coroutine that fills the cache is not exercised here: it
///   needs the game's file loading, mod list and frame timing, which is in-game territory (#49). What is
///   exercised is everything downstream of it, seeded directly.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class PatcherCacheTests {
  private const string Items = """<items><item name="alpha" /></items>""";

  private static PatcherHost Host => PatcherHost.Instance.Value;

  [Fact]
  public void A_cached_document_is_found_by_name_without_regard_to_case() {
    // The cache is built with StringComparer.OrdinalIgnoreCase; config names come from several places and
    // must not have to agree on casing.
    Host.Cache.Clear();
    Host.Cache.Seed("items", Host.CreateXmlFile(Items, "items.xml"));
    try {
      Assert.True(Host.Cache.TryGetPatchedFile("items", out var exact));
      Assert.Contains("""<item name="alpha" />""", exact);
      Assert.True(Host.Cache.TryGetPatchedFile("ITEMS", out _));
      Assert.False(Host.Cache.TryGetPatchedFile("blocks", out _));
    } finally {
      Host.Cache.Clear();
    }
  }

  [Fact]
  public void The_prefix_serves_the_cached_document_and_suppresses_the_vanilla_load() {
    Host.Cache.Clear();
    Host.Cache.Seed("items", Host.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome outcome = Host.Cache.InvokePrefix("items");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.True(outcome.CallbackInvoked);
      Assert.Contains("""<item name="alpha" />""", outcome.ServedXml);
    } finally {
      Host.Cache.Clear();
    }
  }

  [Fact]
  public void The_prefix_accepts_the_config_name_with_or_without_the_xml_extension() {
    // Callers pass both spellings; the cache is keyed without the extension.
    Host.Cache.Clear();
    Host.Cache.Seed("items", Host.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome outcome = Host.Cache.InvokePrefix("items.xml");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.True(outcome.CallbackInvoked);
    } finally {
      Host.Cache.Clear();
    }
  }

  [Fact]
  public void A_served_entry_is_removed_so_its_document_can_be_collected() {
    // "Consumed entries are removed so already-loaded files can be collected while later files are still
    // working through phase 3." The second request therefore finds nothing and must fall through.
    Host.Cache.Clear();
    Host.Cache.Seed("items", Host.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome first = Host.Cache.InvokePrefix("items");
      Assert.False(first.FellThroughToVanilla);
      Assert.False(Host.Cache.Contains("items"));

      PrefixOutcome second = Host.Cache.InvokePrefix("items");
      Assert.True(second.FellThroughToVanilla);
      Assert.False(second.CallbackInvoked);
    } finally {
      Host.Cache.Clear();
    }
  }

  [Fact]
  public void A_file_outside_the_pipeline_falls_through_to_vanilla_untouched() {
    // The client's received-configs path and any reload call LoadAndPatchConfig without the coroutine having
    // run. Those must get the game's own behavior, not a miss.
    Host.Cache.Clear();

    PrefixOutcome outcome = Host.Cache.InvokePrefix("blocks");

    Assert.True(outcome.FellThroughToVanilla);
    Assert.False(outcome.CallbackInvoked);
  }

  [Fact]
  public void A_failed_base_load_suppresses_vanilla_without_invoking_the_callback() {
    // The null marker from phase 1. Vanilla's behavior on a failed base load is to log (phase 1 already did)
    // and never invoke the callback, which makes loadSingleXml skip the file — falling through instead would
    // re-load and re-patch it depth-first, the exact thing this mod exists to avoid.
    Host.Cache.Clear();
    Host.Cache.SeedFailure("items");
    try {
      PrefixOutcome outcome = Host.Cache.InvokePrefix("items");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.False(outcome.CallbackInvoked);
      Assert.False(Host.Cache.Contains("items"));
    } finally {
      Host.Cache.Clear();
    }
  }
}

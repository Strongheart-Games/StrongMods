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
[Collection(GameRoomCollection.Name)]
public class PatcherCacheTests {
  private const string Items = """<items><item name="alpha" /></items>""";

  private static GameRoom Room => GameRoom.Instance.Value;

  [Fact]
  public void A_cached_document_is_found_by_name_without_regard_to_case() {
    // The cache is built with StringComparer.OrdinalIgnoreCase; config names come from several places and
    // must not have to agree on casing.
    Room.Cache.Clear();
    Room.Cache.Seed("items", Room.CreateXmlFile(Items, "items.xml"));
    try {
      Assert.True(Room.Cache.TryGetPatchedFile("items", out var exact));
      Assert.Contains("""<item name="alpha" />""", exact);
      Assert.True(Room.Cache.TryGetPatchedFile("ITEMS", out _));
      Assert.False(Room.Cache.TryGetPatchedFile("blocks", out _));
    } finally {
      Room.Cache.Clear();
    }
  }

  [Fact]
  public void The_prefix_serves_the_cached_document_and_suppresses_the_vanilla_load() {
    Room.Cache.Clear();
    Room.Cache.Seed("items", Room.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome outcome = Room.Cache.InvokePrefix("items");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.True(outcome.CallbackInvoked);
      Assert.Contains("""<item name="alpha" />""", outcome.ServedXml);
    } finally {
      Room.Cache.Clear();
    }
  }

  [Fact]
  public void The_prefix_accepts_the_config_name_with_or_without_the_xml_extension() {
    // Callers pass both spellings; the cache is keyed without the extension.
    Room.Cache.Clear();
    Room.Cache.Seed("items", Room.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome outcome = Room.Cache.InvokePrefix("items.xml");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.True(outcome.CallbackInvoked);
    } finally {
      Room.Cache.Clear();
    }
  }

  [Fact]
  public void A_served_entry_is_removed_so_its_document_can_be_collected() {
    // "Consumed entries are removed so already-loaded files can be collected while later files are still
    // working through phase 3." The second request therefore finds nothing and must fall through.
    Room.Cache.Clear();
    Room.Cache.Seed("items", Room.CreateXmlFile(Items, "items.xml"));
    try {
      PrefixOutcome first = Room.Cache.InvokePrefix("items");
      Assert.False(first.FellThroughToVanilla);
      Assert.False(Room.Cache.Contains("items"));

      PrefixOutcome second = Room.Cache.InvokePrefix("items");
      Assert.True(second.FellThroughToVanilla);
      Assert.False(second.CallbackInvoked);
    } finally {
      Room.Cache.Clear();
    }
  }

  [Fact]
  public void A_file_outside_the_pipeline_falls_through_to_vanilla_untouched() {
    // The client's received-configs path and any reload call LoadAndPatchConfig without the coroutine having
    // run. Those must get the game's own behavior, not a miss.
    Room.Cache.Clear();

    PrefixOutcome outcome = Room.Cache.InvokePrefix("blocks");

    Assert.True(outcome.FellThroughToVanilla);
    Assert.False(outcome.CallbackInvoked);
  }

  [Fact]
  public void A_failed_base_load_suppresses_vanilla_without_invoking_the_callback() {
    // The null marker from phase 1. Vanilla's behavior on a failed base load is to log (phase 1 already did)
    // and never invoke the callback, which makes loadSingleXml skip the file — falling through instead would
    // re-load and re-patch it depth-first, the exact thing this mod exists to avoid.
    Room.Cache.Clear();
    Room.Cache.SeedFailure("items");
    try {
      PrefixOutcome outcome = Room.Cache.InvokePrefix("items");

      Assert.False(outcome.FellThroughToVanilla);
      Assert.False(outcome.CallbackInvoked);
      Assert.False(Room.Cache.Contains("items"));
    } finally {
      Room.Cache.Clear();
    }
  }
}

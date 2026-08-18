using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tests.Fixtures;
using Xunit;

namespace Tests.Patcher;

/// <summary>
///   The mod-major ordering guarantee (#50 gap F1). <c>StrongMods\BreadthFirstXmlPatcher.cs</c> promises that
///   a <c>&lt;foreach&gt;</c> can see vanilla XML and any mod EARLIER in load order, but never a mod AFTER it:
///   phase 2 applies all of one mod's patch files — mutating the cached documents in place through
///   <c>XmlPatcher.PatchXml</c> — before the next mod's first file, and cross-file <c>source=</c> resolution
///   reads that same cache.
///   These tests enact phase 2's per-(mod, file) sequence with synthetic mods: the REAL
///   <c>XmlPatcher.PatchXml</c>, the REAL cache, the REAL foreach <c>source=</c> resolution, in the same
///   mod-major order the coroutine runs (BreadthFirstXmlPatcher.cs lines 89–125), including its per-(mod,
///   file) catch. What stays out of reach is the coroutine itself — <c>LoadAllXmlsBreadthFirstCo</c> is
///   private, and its eligibility prologue and phases 1/3 need <c>WorldStaticData.xmlsToLoad</c>,
///   <c>GameIO</c> and <c>loadSingleXml</c>, none of which run headlessly. The loop-shape facts (eligibility
///   filters, the shipped try/catch, GameConfigMod skipping) are therefore pinned by emulation here and the
///   seam that would make them directly testable is specified in
///   <c>.ai/reports/2026-08-18-patcher-conformance.html</c>.
/// </summary>
[Collection(PatcherHostCollection.Name)]
public class BreadthFirstOrderingTests {
  private const string Items = """<items><item name="alpha" /></items>""";
  private const string Blocks = """<blocks><block name="stone" /></blocks>""";

  /// <summary>One synthetic mod: load-order name plus (entry point → patch file XML).</summary>
  private sealed record SyntheticMod(string Name, IReadOnlyDictionary<string, string> Patches);

  private sealed record ModMajorRun(
    IReadOnlyDictionary<string, string> Documents,
    IReadOnlyList<(string Mod, string File, IReadOnlyList<LogEntry> Logs)> Applications) {
    public IReadOnlyList<LogEntry> LogsOf(string mod) =>
      Applications.Where(a => a.Mod == mod).SelectMany(a => a.Logs).ToList();
  }

  /// <summary>
  ///   Phase 2, enacted: seed the real patcher cache with the base documents, then apply each mod's patch
  ///   files in mod-major order — every file of one mod (in the base documents' order, standing in for
  ///   xmlsToLoad order) before the next mod — mutating the cached documents in place. The per-application
  ///   try/catch mirrors the coroutine's own (BreadthFirstXmlPatcher.cs lines 110–118): a failure is
  ///   recorded and the loop continues, which is the resilience the assertions below rely on.
  /// </summary>
  private static ModMajorRun RunModMajor(
    IReadOnlyList<(string Name, string Xml)> baseDocs, params SyntheticMod[] mods) {
    PatcherHost host = PatcherHost.Instance.Value;
    host.Cache.Clear();
    var targets = new Dictionary<string, object>();
    var applications = new List<(string, string, IReadOnlyList<LogEntry>)>();
    try {
      foreach ((var name, var xml) in baseDocs) {
        object file = host.CreateXmlFile(xml, name + ".xml");
        targets[name] = file;
        host.Cache.Seed(name, file);
      }

      foreach (SyntheticMod mod in mods) {
        foreach ((var name, _) in baseDocs) {
          if (!mod.Patches.TryGetValue(name, out var patchXml)) {
            continue;
          }

          IReadOnlyList<LogEntry> logs;
          try {
            logs = host.ApplyPatchFile(targets[name], patchXml, name + ".xml");
          } catch (Exception ex) {
            logs = new[] { new LogEntry(LogLevel.Exception, $"{ex.GetType().Name}: {ex.Message}") };
          }

          applications.Add((mod.Name, name, logs));
        }
      }

      return new ModMajorRun(
        targets.ToDictionary(kv => kv.Key, kv => host.XmlOf(kv.Value)), applications);
    } finally {
      host.Cache.Clear();
    }
  }

  /// <summary>The two mods every ordering test wants: one appends an item, the other generates a block per
  ///   item by reading items.xml cross-file. Which one loads first is the whole experiment.</summary>
  private static readonly SyntheticMod ItemAppender = new("ItemAppender", new Dictionary<string, string> {
    ["items"] = """
      <config>
        <append xpath="/items"><item name="steelBeam" /></append>
      </config>
      """,
  });

  private static readonly SyntheticMod BlockGenerator = new("BlockGenerator", new Dictionary<string, string> {
    ["blocks"] = """
      <config>
        <foreach source="items" xpath="/items/item" as="item">
          <append xpath="/blocks"><block name="blockOf_{$item/@name}" /></append>
        </foreach>
      </config>
      """,
  });

  [Fact]
  public void A_later_mods_foreach_sees_an_earlier_mods_patch_to_another_file() {
    // The headline: BlockGenerator loads AFTER ItemAppender, so its cross-file read of items.xml must see
    // steelBeam already appended — mod-major ordering makes earlier mods' work visible regardless of which
    // file it landed in.
    ModMajorRun run = RunModMajor(
      new[] { ("items", Items), ("blocks", Blocks) }, ItemAppender, BlockGenerator);

    Assert.Contains("""<block name="blockOf_alpha" />""", run.Documents["blocks"]);
    Assert.Contains("""<block name="blockOf_steelBeam" />""", run.Documents["blocks"]);
  }

  [Fact]
  public void An_earlier_mods_foreach_does_not_see_a_later_mods_patch() {
    // The same two mods in the opposite load order: the foreach runs before steelBeam exists, so only
    // vanilla's item may produce a block — a later mod's patches must be invisible to an earlier mod.
    ModMajorRun run = RunModMajor(
      new[] { ("items", Items), ("blocks", Blocks) }, BlockGenerator, ItemAppender);

    Assert.Contains("""<block name="blockOf_alpha" />""", run.Documents["blocks"]);
    Assert.DoesNotContain("blockOf_steelBeam", run.Documents["blocks"]);
    Assert.Contains("""<item name="steelBeam" />""", run.Documents["items"]); // the later patch itself landed
  }

  [Fact]
  public void A_mods_failing_commands_do_not_abort_its_later_commands_or_later_mods() {
    // Within one patch file, XmlPatcher applies commands independently: a no-match xpath and an unknown
    // command element log, and the file's remaining commands still run — as does the next mod.
    ModMajorRun run = RunModMajor(
      new[] { ("items", Items) },
      new SyntheticMod("BrokenMod", new Dictionary<string, string> {
        ["items"] = """
          <config>
            <set xpath="/items/item[@name='doesNotExist']/@name">nope</set>
            <frobnicate xpath="/items"><item name="never" /></frobnicate>
            <append xpath="/items"><item name="fromBrokenMod" /></append>
          </config>
          """,
      }),
      new SyntheticMod("HealthyMod", new Dictionary<string, string> {
        ["items"] = """
          <config>
            <append xpath="/items"><item name="fromHealthyMod" /></append>
          </config>
          """,
      }));

    Assert.Contains("""<item name="fromBrokenMod" />""", run.Documents["items"]);
    Assert.Contains("""<item name="fromHealthyMod" />""", run.Documents["items"]);
    Assert.DoesNotContain("never", run.Documents["items"]);
    // The failures are reported, not swallowed — and the healthy mod's application stays clean.
    Assert.Contains(run.LogsOf("BrokenMod"), l => l.Level is LogLevel.Warning or LogLevel.Error);
    Assert.DoesNotContain(run.LogsOf("HealthyMod"), l => l.Level is LogLevel.Warning or LogLevel.Error);
  }

  [Fact]
  public void A_malformed_patch_file_throws_at_read_time_and_leaves_the_target_untouched() {
    // Phase 2 wraps each (mod, file) application in try/catch (BreadthFirstXmlPatcher.cs lines 110–118): a
    // patch file that cannot be parsed logs an error for that mod+file and the loop continues. The catch
    // itself lives in the unreachable coroutine; what can be pinned headlessly is the contract it relies
    // on — malformed XML surfaces as a thrown exception when the patch file is read, before any command
    // touches the target, so the target document cannot be half-patched by it.
    PatcherHost host = PatcherHost.Instance.Value;
    object target = host.CreateXmlFile(Items, "items.xml");

    Exception ex = Record.Exception(() => host.ApplyPatchFile(target, "<config><append", "broken.xml"));

    Assert.NotNull(ex);
    // Observed behavior (probed 2026-08-18): the game's XmlFile constructor throws System.Xml.XmlException
    // at parse time; the reflection harness surfaces it wrapped in TargetInvocationException.
    Assert.IsAssignableFrom<System.Xml.XmlException>(
      ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex);
    Assert.Contains("""<item name="alpha" />""", host.XmlOf(target));
  }
}

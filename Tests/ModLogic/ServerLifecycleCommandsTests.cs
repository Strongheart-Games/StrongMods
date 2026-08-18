using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongUtils' ServerLifecycleCommands — the queue of console commands a restart is supposed to run once,
///   at <c>on_game_start_done</c>, and then forget. Everything but the execution itself is file work over
///   <c>ConfigManager</c>, so it runs for real in the stubbed universe against a scratch directory.
///   <para>
///     The one thing this cannot assert is that a command REACHES the console: <c>SdtdConsole.Instance</c>
///     needs a running server (gap R1 in .ai/test-idea-tool-gap-seed.md). What it does assert is the part
///     that outlives a restart — that the queue is drained afterwards, which the mod does regardless of
///     whether the console was there, because it swallows each command's failure.
///   </para>
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class ServerLifecycleCommandsTests : IDisposable {
  private const string ConfigFileName = "server_lifecycle_commands.xml";

  private readonly Type configManager;
  private readonly Type commands;
  private readonly string root;

  public ServerLifecycleCommandsTests() {
    ModLogicHost host = ModLogicHost.For("StrongUtils");
    configManager = host.ModType("StrongUtils.ConfigManager");
    commands = host.ModType("StrongUtils.ServerLifecycleCommands");
    root = Path.Combine(Path.GetTempPath(), "strongmods-slc-" + Guid.NewGuid().ToString("N"));
    Reset();
    ModLogicHost.CallStatic(configManager, "Init", root);
    ModLogicHost.CallStatic(commands, "Init");
  }

  public void Dispose() {
    Reset();
    if (Directory.Exists(root)) {
      Directory.Delete(root, true);
    }
  }

  private void Reset() {
    if (ModLogicHost.GetStatic(configManager, "Instance") is { } live) {
      ModLogicHost.Call(live, "Dispose");
    }
  }

  [Fact]
  public void Init_creates_the_queue_file() {
    Assert.True(File.Exists(Path.Combine(root, ConfigFileName)));
  }

  [Fact]
  public void An_added_command_lands_as_an_on_game_start_done_element() {
    ModLogicHost.CallStatic(commands, "AddCommand", "say hello");

    XElement queued = Assert.Single(Document().Elements());
    Assert.Equal("on_game_start_done", queued.Name.LocalName);
    Assert.Equal("say hello", queued.Attribute("command")!.Value);
  }

  [Fact]
  public void Commands_keep_the_order_they_were_added_in() {
    ModLogicHost.CallStatic(commands, "AddCommand", "first");
    ModLogicHost.CallStatic(commands, "AddCommand", "second");
    ModLogicHost.CallStatic(commands, "AddCommand", "third");

    Assert.Equal(new[] { "first", "second", "third" }, Load());
  }

  [Fact]
  public void Loading_an_empty_queue_yields_nothing() {
    Assert.Empty(Load());
  }

  /// <summary>An element with no <c>command</c> attribute, or an empty one, is not a command.</summary>
  [Fact]
  public void Malformed_entries_are_skipped_rather_than_loaded() {
    Write("<config>" +
          "<on_game_start_done />" +
          "<on_game_start_done command=\"\" />" +
          "<on_game_start_done other=\"say x\" />" +
          "<on_game_start_done command=\"kept\" />" +
          "</config>");

    Assert.Equal(new[] { "kept" }, Load());
  }

  [Fact]
  public void Entries_under_another_element_name_are_not_loaded() {
    Write("<config><on_shutdown command=\"other\" /><on_game_start_done command=\"mine\" /></config>");

    Assert.Equal(new[] { "mine" }, Load());
  }

  /// <summary>
  ///   The point of the feature: the queue is one-shot. A second start must not replay it. Nothing here
  ///   reaches a console — every command fails inside the mod's own try/catch — so what is asserted is the
  ///   drain, which happens either way.
  /// </summary>
  [Fact]
  public void Running_the_queue_drains_it() {
    ModLogicHost.CallStatic(commands, "AddCommand", "say one");
    ModLogicHost.CallStatic(commands, "AddCommand", "say two");

    ModLogicHost.CallStatic(commands, "OnGameStartDone");

    Assert.Empty(Load());
  }

  [Fact]
  public void Running_the_queue_leaves_other_elements_alone() {
    Write("<config><on_shutdown command=\"kept\" /><on_game_start_done command=\"drained\" /></config>");

    ModLogicHost.CallStatic(commands, "OnGameStartDone");

    XElement survivor = Assert.Single(Document().Elements());
    Assert.Equal("on_shutdown", survivor.Name.LocalName);
  }

  /// <summary>Two identical queued commands are two entries, and the drain must take both.</summary>
  [Fact]
  public void Duplicate_commands_are_all_drained() {
    ModLogicHost.CallStatic(commands, "AddCommand", "say twice");
    ModLogicHost.CallStatic(commands, "AddCommand", "say twice");
    Assert.Equal(2, Load().Count);

    ModLogicHost.CallStatic(commands, "OnGameStartDone");

    Assert.Empty(Load());
  }

  [Fact]
  public void Running_an_empty_queue_is_a_no_op() {
    ModLogicHost.CallStatic(commands, "OnGameStartDone");

    Assert.Empty(Document().Elements());
  }

  private List<string> Load() =>
    (List<string>)ModLogicHost.CallStatic(commands, "LoadCommands", "on_game_start_done");

  private XElement Document() => XElement.Load(Path.Combine(root, ConfigFileName));

  private void Write(string xml) => File.WriteAllText(Path.Combine(root, ConfigFileName), xml);
}

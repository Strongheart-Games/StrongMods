using System;
using System.Collections;
using System.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongUtils' PlayerDamage — the per-player ring buffer of recent damage events that the death log and
///   the spoofed-damage check read. Pure data structure over game types, so it runs for real: the host keeps
///   the REAL UnityEngine.CoreModule (entity types will not load against the stub) and the paths under test
///   never log, which is the trade that universe makes.
///   <para>
///     The two game objects it needs are planted, not built: an <c>EntityPlayer</c> that only has to carry an
///     <c>entityId</c>, and a <c>ConnectionManager</c> whose <c>IsClient</c> is what gates recording. The
///     gate reads <c>protocolManager.CurrentMode</c>, so the mode is what a test sets.
///   </para>
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class PlayerDamageTests : IDisposable {
  private const int MaxEvents = 20;

  private readonly ModLogicHost host;
  private readonly Type playerDamage;
  private readonly Type damageSource;

  public PlayerDamageTests() {
    host = ModLogicHost.For("StrongUtils", false);
    playerDamage = host.ModType("StrongUtils.PlayerDamage");
    damageSource = host.GameType("DamageSource");
    SetNetworkMode("Server");
  }

  /// <summary>The history is a process-wide static, so every test clears the players it used.</summary>
  public void Dispose() {
    if (ModLogicHost.GetStatic(playerDamage, "s_history") is IDictionary history) {
      history.Clear();
    }
  }

  [Fact]
  public void History_of_an_unknown_player_is_empty() {
    Assert.Empty(History(Player(1)));
  }

  [Fact]
  public void A_null_player_is_ignored_rather_than_throwing() {
    ModLogicHost.CallStatic(playerDamage, "RecordDamage", null, Source(), 5, false, 1f);

    Assert.Empty((Array)ModLogicHost.CallStatic(playerDamage, "GetHistory", new object[] { null }));
  }

  [Fact]
  public void Recorded_events_come_back_oldest_first() {
    object player = Player(10);
    Record(player, 1);
    Record(player, 2);
    Record(player, 3);

    Assert.Equal(new[] { 1, 2, 3 }, Strengths(player));
  }

  [Fact]
  public void The_buffer_holds_the_cap_exactly() {
    object player = Player(11);
    for (var i = 1; i <= MaxEvents; i++) {
      Record(player, i);
    }

    Assert.Equal(Enumerable.Range(1, MaxEvents), Strengths(player));
  }

  /// <summary>The headline property: past the cap, the OLDEST event is the one that goes.</summary>
  [Fact]
  public void Past_the_cap_the_oldest_event_is_dropped() {
    object player = Player(12);
    for (var i = 1; i <= MaxEvents + 5; i++) {
      Record(player, i);
    }

    Assert.Equal(Enumerable.Range(6, MaxEvents), Strengths(player));
  }

  [Fact]
  public void Each_player_keeps_its_own_history() {
    object first = Player(20);
    object second = Player(21);
    Record(first, 1);
    Record(second, 2);
    Record(second, 3);

    Assert.Equal(new[] { 1 }, Strengths(first));
    Assert.Equal(new[] { 2, 3 }, Strengths(second));
  }

  /// <summary>
  ///   Keyed by entity id, deliberately — the mod never holds the EntityPlayer itself. Two different objects
  ///   with the same id are therefore the same player to the buffer.
  /// </summary>
  [Fact]
  public void Two_objects_with_the_same_entity_id_share_one_history() {
    Record(Player(30), 1);

    Assert.Equal(new[] { 1 }, Strengths(Player(30)));
  }

  [Fact]
  public void Clearing_drops_only_that_player() {
    object kept = Player(40);
    object cleared = Player(41);
    Record(kept, 1);
    Record(cleared, 2);

    ModLogicHost.CallStatic(playerDamage, "ClearHistory", cleared);

    Assert.Equal(new[] { 1 }, Strengths(kept));
    Assert.Empty(History(cleared));
  }

  [Fact]
  public void Clearing_an_unknown_player_is_a_no_op() {
    ModLogicHost.CallStatic(playerDamage, "ClearHistory", Player(42));
    ModLogicHost.CallStatic(playerDamage, "ClearHistory", new object[] { null });
  }

  /// <summary>The mod is server-side: on a client, recording is skipped entirely.</summary>
  [Fact]
  public void Nothing_is_recorded_on_a_client() {
    SetNetworkMode("Client");
    object player = Player(50);

    Record(player, 1);

    Assert.Empty(History(player));
  }

  [Fact]
  public void The_recorded_event_carries_what_it_was_given() {
    object player = Player(60);
    object source = Source();

    ModLogicHost.CallStatic(playerDamage, "RecordDamage", player, source, 77, true, 2.5f);

    object recorded = History(player).GetValue(0);
    Assert.Same(source, ModLogicHost.Call(recorded, "get_Source"));
    Assert.Equal(77, ModLogicHost.Call(recorded, "get_Strength"));
    Assert.Equal(true, ModLogicHost.Call(recorded, "get_IsCrit"));
    Assert.Equal(2.5f, ModLogicHost.Call(recorded, "get_ImpulseScale"));
  }

  private object Player(int entityId) {
    object player = ModLogicHost.Uninitialized(host.GameType("EntityPlayer"));
    ModLogicHost.SetInstance(player, "entityId", entityId);
    return player;
  }

  private object Source() => ModLogicHost.Uninitialized(damageSource);

  private void Record(object player, int strength) =>
    ModLogicHost.CallStatic(playerDamage, "RecordDamage", player, Source(), strength, false, 1f);

  private Array History(object player) =>
    (Array)ModLogicHost.CallStatic(playerDamage, "GetHistory", player);

  private int[] Strengths(object player) =>
    History(player).Cast<object>().Select(e => (int)ModLogicHost.Call(e, "get_Strength")).ToArray();

  /// <summary>
  ///   Plants the singleton ConnectionManager whose IsClient the mod gates on. IsClient reads
  ///   protocolManager.CurrentMode, so a protocol manager in the named mode is the whole fixture.
  /// </summary>
  private void SetNetworkMode(string mode) {
    Type connectionManager = host.GameType("ConnectionManager");
    Type protocolManager = host.GameType("ProtocolManager");
    Type networkType = protocolManager.GetNestedType("NetworkType");

    object protocol = ModLogicHost.Uninitialized(protocolManager);
    ModLogicHost.SetInstance(protocol, "CurrentMode", Enum.Parse(networkType, mode));

    object connection = ModLogicHost.Uninitialized(connectionManager);
    ModLogicHost.SetInstance(connection, "protocolManager", protocol);
    ModLogicHost.SetStatic(connectionManager, "Instance", connection);
  }
}

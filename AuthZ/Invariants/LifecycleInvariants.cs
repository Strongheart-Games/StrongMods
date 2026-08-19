using System;
using System.Collections.Generic;

namespace AuthZ.Invariants {
  /// <summary>
  /// Family D: a packet whose vanilla send site fires exactly once per connection arrives exactly once.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Static analysis of every construction and send site in V3.1.0-b14 found a fixed connection bootstrap sequence,
  /// and each of these packages has a single send site the client reaches once. Repetition is therefore free of false
  /// positives and needs no threshold — one bit of state per connection.
  /// </para>
  /// <para>
  /// Worth having beyond spoof detection: each of these makes the server do real work, so a repeat is load the server
  /// did not have to take.
  /// </para>
  /// </remarks>
  public abstract class OncePerConnection<TPackage> : Invariant where TPackage : NetPackage {
    private readonly HashSet<int> _seen = new();

    public override Family Family => Family.Lifecycle;
    public override Type Package => typeof(TPackage);
    public override string Statement => "this packet arrives at most once per connection";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      if (_seen.Add(sender.ClientNumber)) {
        return true;
      }

      detail = "sent " + typeof(TPackage).Name + " again on a connection that already sent it";
      return false;
    }

    /// <summary>Clears the connection's state so a reconnecting client starts clean.</summary>
    public void Forget(ClientInfo sender) {
      _seen.Remove(sender.ClientNumber);
    }
  }

  /// <summary>
  /// Entering the game is a one-shot. The handler runs a coroutine that resends localization, every XML config, world
  /// info, decorations, spawn points and world areas, and raises <c>ModEvents.PlayerJoinedGame</c> each time.
  /// </summary>
  public sealed class EnterGameOnce : OncePerConnection<NetPackageRequestToEnterGame> {
    public override string Id => "lifecycle.enter-game-once";
  }

  /// <summary>
  /// Spawning is a one-shot. The handler creates a player entity and binds it to the connection.
  /// </summary>
  public sealed class SpawnRequestOnce : OncePerConnection<NetPackageRequestToSpawnPlayer> {
    public override string Id => "lifecycle.spawn-once";
  }

  /// <summary>
  /// Family D: a client that is already bound to a player entity does not ask to be spawned again.
  /// </summary>
  /// <remarks>
  /// Complements <see cref="SpawnRequestOnce"/> rather than duplicating it: this one survives a mod or a game update
  /// that legitimately re-sends the request, because it asks the stronger question — is this connection already
  /// holding an entity?
  /// </remarks>
  public sealed class SpawnOnlyWhenUnbound : Invariant {
    public override string Id => "lifecycle.spawn-when-unbound";
    public override Family Family => Family.Lifecycle;
    public override Type Package => typeof(NetPackageRequestToSpawnPlayer);
    public override string Statement => "a spawn request comes from a connection with no player entity yet";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      if (sender.entityId == -1) {
        return true;
      }

      detail = "requested a spawn while already bound to entity " + sender.entityId;
      return false;
    }
  }
}

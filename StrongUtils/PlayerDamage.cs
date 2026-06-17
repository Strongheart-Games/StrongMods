using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UniLinq;

namespace StrongUtils {
  public static class PlayerDamage {
    private const int MaxEvents = 20;

    // Keyed by entity ID so we never hold a strong reference to the EntityPlayer itself.
    private static readonly ConcurrentDictionary<int, Queue<DamageEvent>> s_history = new();

    /// <summary>
    ///   Records a damage event for the given player, keeping only the most recent
    ///   <see cref="MaxEvents" /> entries. Thread-safe.
    /// </summary>
    public static void RecordDamage(EntityPlayer player, DamageSource source,
      int strength, bool crit, float impulseScale) {
      if (player is null) {
        return;
      }

      var evt = new DamageEvent(source, strength, crit, impulseScale);

      s_history.AddOrUpdate(
        player.entityId,
        // Key not yet present — seed a new queue with this event.
        _ => {
          var q = new Queue<DamageEvent>(MaxEvents);
          q.Enqueue(evt);
          return q;
        },
        // Key present — append and trim.
        (_, q) => {
          lock (q) {
            if (q.Count >= MaxEvents) {
              q.Dequeue(); // drop oldest
            }

            q.Enqueue(evt);
          }

          return q;
        }
      );
    }

    /// <summary>
    ///   Returns a snapshot of the damage history for the given player,
    ///   ordered oldest-first. Returns an empty array if no history exists.
    /// </summary>
    public static DamageEvent[] GetHistory(EntityPlayer player) {
      if (player is null || !s_history.TryGetValue(player.entityId, out Queue<DamageEvent> q)) {
        return Array.Empty<DamageEvent>();
      }

      lock (q) {
        return q.ToArray();
      }
    }

    /// <summary>
    ///   Clears the damage history for a given player. Call this on player disconnect
    ///   to avoid stale entries accumulating in memory.
    /// </summary>
    public static void ClearHistory(EntityPlayer player) {
      if (player is null) {
        return;
      }

      s_history.TryRemove(player.entityId, out _);
    }

    public static void HandlePlayerDisconnected(ref ModEvents.SPlayerDisconnectedData data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      if (GameManager.Instance?.World?.GetEntity(data.ClientInfo.entityId) is not EntityPlayer player) {
        return;
      }

      ClearHistory(player);
    }

    public static void HandleEntityKilled(ref ModEvents.SEntityKilledData data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      if (data.KilledEntitiy is not EntityPlayer player) {
        return;
      }

      Log.Out(
        $"[StrongUtils] Player killed: {player.PlayerDisplayName} ({player.entityId}); recent damage:\n  {string.Join<DamageEvent>("\n  ", GetHistory(player).Reverse())}");
      ClearHistory(player);
    }

    public static void ValidateDamageEntityPackage(NetPackageDamageEntity package) {
      Entity entity = GameManager.Instance?.World?.GetEntity(package.entityId);

      if (entity is not EntityPlayer player) {
        return;
      }

      if (package.Sender.entityId != player.entityId) {
        Log.Warning(
          $"[StrongUtils] DamageEntity: {package.Sender.playerName} ({package.Sender.entityId}) damaged {player.PlayerDisplayName} ({player.entityId})");
      }
    }
  }

  public sealed class DamageEvent {
    public DamageEvent(DamageSource source, int strength, bool isCrit, float impulseScale) {
      Timestamp = DateTime.UtcNow;
      Source = source;
      Strength = strength;
      IsCrit = isCrit;
      ImpulseScale = impulseScale;
    }

    public DateTime Timestamp { get; }
    public DamageSource Source { get; }
    public int Strength { get; }
    public bool IsCrit { get; }
    public float ImpulseScale { get; }

    public override string ToString() {
      return $"[{Timestamp:T}] source={Source} strength={Strength} crit={IsCrit} impulse={ImpulseScale:F2}";
    }
  }
}

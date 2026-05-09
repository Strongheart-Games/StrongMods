using ConcurrentCollections;

namespace BloodRain {
  public class BloodRainChallenge {

    // Keyed by entity ID so we never hold a strong reference to the EntityPlayer itself.
    private static readonly ConcurrentHashSet<int> s_participants = new();

    public static void OnBloodRainStart() {
      GameManager.Instance.World.Players.list.ForEach(p => s_participants.Add(p.entityId));
    }

    public static void OnBloodRainEnd() {
      foreach (var id in s_participants) {
        if (GameManager.Instance.World.GetEntity(id) is EntityPlayer player) {
          player.Buffs.AddBuff("buff_blood_rain_survived");
        }
      }
    }

    public static void OnAddedToWorld(EntityAlive entity) {
      TryRemovePlayer(entity);
    }

    public static void OnEntityUnload(EntityAlive entity) {
      TryRemovePlayer(entity);
    }

    public static void OnEntityDeath(EntityAlive entity) {
      TryRemovePlayer(entity);
    }

    // TODO: Remove participant if detected in a protected area such as a Trader or other safe zone

    private static bool TryRemovePlayer(EntityAlive entity) {
      return entity is EntityPlayer && s_participants.TryRemove(entity.entityId);
    }
  }
}

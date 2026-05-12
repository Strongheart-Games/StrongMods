using StrongUtils.KeyValueStore;

namespace StrongUtils {
  public static class FastTravel {
    private const string TotalDonationsKey = "fast_travel_donations";
    private const string PendingDonationsCVar = "pending_fast_travel_donations";

    private static readonly DonationTier[] s_donationTiers = {
      new("Pine Forest", 100),
      new("Burnt Forest", 500),
      new("Desert", 1000),
      new("Snow", 2000),
      new("Wasteland", 4000),
      new("Bonus", 8000),
    };

    public static void Init() {
      IKeyValueStore kvstore = KeyValueStore.KeyValueStore.Instance;
      if (!kvstore.Contains(TotalDonationsKey)) {
        // Initialize total donations so that it works well with TestAndSet()
        kvstore.Set(TotalDonationsKey, 0);
      }
    }

    public static void ProcessFastTravelDonations(EntityPlayer player) {
      if (player is null) {
        return;
      }

      var pending = (int)player.GetCVar(PendingDonationsCVar);
      if (pending <= 0) {
        return;
      }

      IKeyValueStore kvstore = KeyValueStore.KeyValueStore.Instance;
      for (var tries = 0; tries < 3; tries++) {
        var oldTotal = kvstore.Get(TotalDonationsKey, 0);
        var newTotal = oldTotal + pending;
        if (kvstore.TestAndSet(TotalDonationsKey, oldTotal, newTotal)) {
          // Don't just set to 0 in case the value was changed in the meantime
          player.SetCVar(PendingDonationsCVar, player.GetCVar(PendingDonationsCVar) - pending);
          Chat.Global($"{player.PlayerDisplayName} donated {pending} fast travel supplies (total: {newTotal}).");
          Chat.Global(GetDonationsStatus());
          return;
        }
      }

      Chat.Whisper(player, "Unable to process donation.");
    }

    public static string GetDonationsStatus()
    {
      var unlockedNames = "";
      DonationTier nextTier = null;
      var cumulativeGoal = 0;
      var nextTierTarget = 0;

      IKeyValueStore kvstore = KeyValueStore.KeyValueStore.Instance;
      var totalDonations = kvstore.Get(TotalDonationsKey, 0);

      foreach (DonationTier tier in s_donationTiers)
      {
        cumulativeGoal += tier.DonationsNeeded;

        if (totalDonations >= cumulativeGoal)
        {
          unlockedNames += (unlockedNames == "" ? "" : ", ") + tier.Name;
        }
        else
        {
          // This is the first tier we haven't reached yet
          nextTier = tier;
          nextTierTarget = cumulativeGoal;
          break;
        }
      }

      var status = "Unlocked: " + (unlockedNames == "" ? "None" : unlockedNames);

      if (nextTier != null)
      {
        status += $" | Next: {nextTier.Name} ({totalDonations}/{nextTierTarget})";
      }
      else
      {
        status += " | All donation tiers unlocked! Total: " + totalDonations;
      }

      return status;
    }

    public class DonationTier {
      public DonationTier(string name, int donationsNeeded) {
        Name = name;
        DonationsNeeded = donationsNeeded;
      }

      public string Name { get; }
      public int DonationsNeeded { get; }
    }
  }
}

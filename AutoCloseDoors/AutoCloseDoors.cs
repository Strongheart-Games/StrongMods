using HarmonyLib;

namespace AutoCloseDoors {
  public static class AutoCloseDoors {
    private const string AutoCloseTag = "auto_close";
    private const float MaxPlayerDistance = 10f;


    private static void TryCloseDoor(TEFeatureDoor door, World world) {
      if (!door.isOpen || !CanAutoClose(door)) {
        return;
      }

      if (world.IsPlayerAliveAndNear(door.ToWorldPos(), MaxPlayerDistance)) {
        Log.Out($"[AutoCloseDoors] Cannot auto-close door at {door.ToWorldPos()} because a player is nearby.");
        return;
      }

      Log.Out($"[AutoCloseDoors] Auto-closing door at {door.ToWorldPos()}.");
      door.SetOpen(false, true);
    }

    private static bool CanAutoClose(TEFeatureDoor door) {
      return !door.blockValue.ischild && door.blockValue.Block.Tags.GetTagNames().Contains(AutoCloseTag) &&
             GameManager.Instance.World.IsWithinTraderArea(door.ToWorldPos());
    }

    [HarmonyPatch(typeof(TEFeatureDoor), nameof(TEFeatureDoor.UpdateTick))]
    public class TEFeatureDoor_UpdateTick_Patch {
      private static void Postfix(TEFeatureDoor __instance, World world) {
        if (world.IsRemote()) {
          return;
        }

        TryCloseDoor(__instance, world);
      }
    }
  }
}

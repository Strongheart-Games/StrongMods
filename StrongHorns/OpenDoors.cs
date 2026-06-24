namespace StrongHorns {
  public static class OpenDoors {
    private const string BlockCategoryName = "trader_doors";
    private const string HonkToOpenTag = "honk_to_open";

    // Cannot be larger than the chunk width (16) since we're only scanning the current chunk and immediate neighbors
    private const int MaxDistance = 15;

    public static void Init() {
      NearbyBlockFinder.AddCategory(BlockCategoryName, CanHonkToOpen);
      StrongHorns.RegisterHonkListener(OnHonk);
    }

    private static bool CanHonkToOpen(Block block, WorldBase world, Vector3i pos, BlockValue blockValue) {
      return block is BlockCompositeTileEntity tileEntity &&
             tileEntity.CompositeData.TryGetFeatureData<TEFeatureDoor>(out TileEntityFeatureData _) &&
             !blockValue.ischild && block.Tags.GetTagNames().Contains(HonkToOpenTag) &&
             GameManager.Instance.World.IsWithinTraderArea(pos);
    }

    private static void OnHonk(EntityVehicle vehicle) {
      Log.Out($"[StrongHorns] Honk from {vehicle.vehicle.vehicleName} at {vehicle.position}");
      NearbyBlockFinder.ForeachNearbyBlock(BlockCategoryName, vehicle.GetBlockPosition(), MaxDistance, OpenDoor);
    }

    private static void OpenDoor(Block block, Vector3i pos) {
      TileEntity tileEntity = GameManager.Instance.World.GetTileEntity(pos);
      if (tileEntity is null) {
        Log.Out($"[StrongHorns] Can't open {block.blockName} at {pos}; it is not a Tile Entity.");
        return;
      }

      if (!tileEntity.TryGetSelfOrFeature(out TEFeatureDoor door)) {
        Log.Out($"[StrongHorns] Can't open {block.blockName} at {pos}; it is not a door.");
        return;
      }

      var currentState = door.IsOpen();
      var action = currentState ? "Closing" : "Opening";
      // There's a bug in the regular version of 7DtD that the boolean logic is flipped
      var newState = GameManager.IsDedicatedServer ? !currentState : currentState;
      Log.Out($"[StrongHorns] {action} {block.blockName} at {pos}");
      // TODO: Don't animate for slower doors like big wood doors and drawbridges
      door.SetOpen(newState, true);
    }
  }
}

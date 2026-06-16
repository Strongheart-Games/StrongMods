namespace StrongHonk {
  public static class OpenDoors {
    private const string BlockCategoryName = "trader_doors";
    private const string HonkToOpenTag = "honk_to_open";

    // Cannot be larger than the chunk width (16) since we're only scanning the current chunk and immediate neighbors
    private const int MaxDistance = 15;

    public static void Init() {
      NearbyBlockFinder.AddCategory(BlockCategoryName, CanHonkToOpen);
      StrongHonk.RegisterHonkListener(OnHonk);
    }

    private static bool CanHonkToOpen(Block block, WorldBase world, Vector3i pos, BlockValue blockValue) {
      return block is BlockCompositeTileEntity tileEntity &&
             tileEntity.CompositeData.TryGetFeatureData<TEFeatureDoor>(out TileEntityFeatureData _) &&
             !blockValue.ischild && block.Tags.GetTagNames().Contains(HonkToOpenTag) &&
             GameManager.Instance.World.IsWithinTraderArea(pos);
    }

    private static void OnHonk(EntityVehicle vehicle) {
      Log.Out($"[StrongHonk] Honk from {vehicle.vehicle.vehicleName} at {vehicle.position}");
      NearbyBlockFinder.ForeachNearbyBlock(BlockCategoryName, vehicle.GetBlockPosition(), MaxDistance, OpenDoor);
    }

    private static void OpenDoor(Block block, Vector3i pos) {
      World world = GameManager.Instance.World;
      BlockValue blockValue = world.GetBlock(pos);
      var chunkKey = WorldChunkCache.MakeChunkKey(World.toChunkXZ(pos.x), World.toChunkXZ(pos.z));
      Log.Out($"[StrongHonk] Activating {block.blockName} at {pos}");
      block.OnBlockActivated(world, pos, blockValue, null);
    }
  }
}

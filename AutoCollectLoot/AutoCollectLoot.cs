using UnityEngine;

namespace AutoCollectLoot {
  public static class AutoCollectLoot {
    public static bool Enabled = true;
    public static bool EnabledOutsideBloodMoon = false;
    public static float KillshotBonusLotteryChance = 0.5f;

    // Returns true iff loot was successfully collected (not dropped)
    public static bool TryCollect(EntityAlive entity) {
      if (entity is null || !Enabled) {
        return false;
      }

      Log.Out($"[AutoCollectLoot] Collecting loot for {entity.EntityName}");

      ItemClass lootItem = GetLootItem(entity);
      if (lootItem is null) {
        return false;
      }

      EntityPlayer player = ChooseRecipient(entity);
      if (player is null) {
        return false;
      }

      return TryGiveLoot(lootItem, player);
    }

    public static bool IsEnabledNow() {
      if (!Enabled) {
        return false;
      }

      if (EnabledOutsideBloodMoon) {
        return true;
      }

      var worldTime = GameManager.Instance.World.worldTime;
      var duskHour = GameManager.Instance.World.DuskHour;
      var dawnHour = GameManager.Instance.World.DawnHour;
      var bloodmoonDay = SkyManager.bloodmoonDay;
      return GameUtils.IsBloodMoonTime(worldTime, (duskHour, dawnHour), bloodmoonDay);
    }

    private static ItemClass GetLootItem(EntityAlive entity) {
      if (entity is null) {
        return null;
      }

      DictionarySave<int, EntityClass> classes = EntityClass.list;
      EntityClass dropped = classes[classes[entity.entityClass].LootDropPick(entity.rand)];
      if (dropped is null) {
        Log.Warning($"[AutoCollectLoot] No loot entity found for {entity.EntityName}");
        return null;
      }

      if (LootItems.TryGetLootItem(dropped.entityClassName, out ItemClass item)) {
        Log.Out($"[AutoCollectLoot] Found loot item {item.Name} for {entity.entityName}");
        return item;
      }

      Log.Warning($"[AutoCollectLoot] No loot item found for {dropped.entityClassName}");
      return null;
    }

    private static EntityPlayer ChooseRecipient(EntityAlive entity) {
      var lottery = new LootLottery(entity);
      return lottery.ChooseWinner(KillshotBonusLotteryChance);
    }

    private static bool TryGiveLoot(ItemClass item, EntityPlayer player) {
      if (item is null) {
        return false;
      }

      var itemStack = new ItemStack(new ItemValue(item.Id), 1);

      if (!player.isEntityRemote) {
        return player.bag.AddItem(itemStack);
      }

      ClientInfo client = ConnectionManager.Instance.Clients.ForEntityId(player.entityId);
      if (client is null) {
        return false;
      }

      World world = GameManager.Instance.World;
      var loot = (EntityItem)EntityFactory.CreateEntity(new EntityCreationData {
        entityClass = EntityClass.FromString("item"),
        id = EntityFactory.nextEntityID++,
        itemStack = itemStack,
        pos = player.position,
        rot = new Vector3(20f, 0f, 20f),
        lifetime = 60 * 20, // 20 minutes, in case their inventory is full
        belongsPlayerId = player.entityId
      });
      world.SpawnEntityInWorld(loot);
      client.SendPackage(NetPackageManager.GetPackage<NetPackageEntityCollect>().Setup(loot.entityId, client.entityId));
      world.RemoveEntity(loot.entityId, EnumRemoveEntityReason.Despawned);
      return true;
    }
  }
}

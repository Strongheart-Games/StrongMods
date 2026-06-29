using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AutoCollectLoot {
  public class AutoCollectLoot {
    public const string LootItemNameProperty = "loot_item_name_strong";
    public static bool Enabled = true;
    public static bool EnabledOutsideBloodMoon = false;


    // Returns true iff loot was successfully collected (not dropped)
    public static bool TryCollect(EntityAlive entity) {
      if (entity is null || !IsEnabled()) {
        return false;
      }

      Log.Out($"[AutoCollectLoot] Collecting loot for {entity.EntityName}");

      var lootDropItemName = GetLootItemName(entity);
      if (lootDropItemName is null || lootDropItemName.Length == 0) {
        return false;
      }

      EntityPlayer player = ChooseRecipient(entity);
      if (player is null) {
        return false;
      }

      return TryGiveLoot(lootDropItemName, player);
    }

    public static bool IsEnabled() {
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

    public static string GetLootItemName(EntityAlive entity) {
      DictionarySave<int, EntityClass> classes = EntityClass.list;
      EntityClass dropped = classes[classes[entity.entityClass].LootDropPick(entity.rand)];
      if (dropped is null) {
        Log.Warning($"[AutoCollectLoot] No loot item found for {entity.EntityName}");
        return null;
      }

      var lootItemName = dropped.Properties.GetString(LootItemNameProperty);
      if (lootItemName is null || lootItemName.Length == 0) {
        Log.Warning($"[AutoCollectLoot] No loot item found for {dropped.entityClassName}");
      } else {
        Log.Out($"[AutoCollectLoot] Found loot item {lootItemName} for {dropped.entityClassName}");
      }

      return lootItemName;
    }

    public static EntityPlayer ChooseRecipient(EntityAlive entity) {
      var lottery = new LootLottery(entity);
      return lottery.ChooseWinner();
    }

    public static bool TryGiveLoot(string itemName, EntityPlayer player) {
      ItemValue itemValue = ItemClass.GetItem(itemName);
      // GetItem() returns ItemValue.None, so we have to check for it with IsEmpty()
      if (itemValue is null || itemValue.IsEmpty()) {
        return false;
      }

      var itemStack = new ItemStack(itemValue, 1);

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

  public class LootLottery {
    private static GameRandom _random;
    private readonly EntityAlive _killed;

    public LootLottery(EntityAlive killed) {
      if (_random is null) {
        _random = new GameRandom();
        _random.SetSeed((int)Stopwatch.GetTimestamp());
      }

      _killed = killed;
    }

    public EntityPlayer ChooseWinner() {
      if (_killed is null) {
        return null;
      }

      // killer has a 50% chance to win off the bat
      var killer = _killed.entityThatKilledMe as EntityPlayer;
      if (killer is not null && _random.RandomRange(2) == 0) {
        return killer;
      }

      var range = GameStats.GetInt(EnumGameStats.PartySharedKillRange);
      List<Entity> candidates = new();
      if (killer is not null) {
        candidates.Add(killer);
        List<EntityPlayer> partyInRange = killer.Party?.MemberList?.FindAll(m => (m.position - _killed.position).magnitude <= range);
        if (partyInRange is not null) {
          candidates.AddRange(partyInRange); // This will add the killer twice, giving them an extra "lottery ticket"
        }
      } else {
        GameManager.Instance.World.GetEntitiesAround(EntityFlags.Player, _killed.position, range, candidates);
      }

      // TODO: bad luck protection

      if (candidates.Count == 0) {
        return null;
      }

      return (EntityPlayer)candidates[_random.RandomRange(candidates.Count)];
    }
  }
}

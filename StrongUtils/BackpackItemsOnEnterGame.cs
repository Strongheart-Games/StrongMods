using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace StrongUtils {
  public static class BackpackItemsOnEnterGame {
    private const string PropBackpackItemsOnEnterGame = "BackpackItemsOnEnterGame";

    public static void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData data) {
      if (data.RespawnType is RespawnType.NewGame or RespawnType.EnterMultiplayer) {
        SetupStartingBackpackItems(data);
      }
    }

    private static void SetupStartingBackpackItems(ModEvents.SPlayerSpawnedInWorldData data) {
      if (GameManager.Instance.World.GetEntity(data.EntityId) is not EntityAlive player ||
          player.GetBackpackItemsOnEnterGame().Count == 0) {
        Log.Out("[BackpackItemsOnEnterGame] No items configured to give");
        return;
      }

      foreach (ItemStack item in player.GetBackpackItemsOnEnterGame()) {
        if (TryGiveItem(item, data)) {
          Log.Out($"[BackpackItemsOnEnterGame] Gave item '{item}'");
        } else {
          Log.Error($"[BackpackItemsOnEnterGame] Failed to give item '{item}'");
        }
      }
    }

    private static bool TryGiveItem(ItemStack item, ModEvents.SPlayerSpawnedInWorldData data) {
      if (item is null) {
        return false;
      }

      if (data.IsLocalPlayer) {
        return GameManager.Instance.World.GetLocalPlayerFromID(data.EntityId)?.bag.AddItem(item) ?? false;
      }

      ClientInfo client = data.ClientInfo;
      if (client is null) {
        return false;
      }

      World world = GameManager.Instance.World;
      var loot = (EntityItem)EntityFactory.CreateEntity(new EntityCreationData {
        entityClass = EntityClass.FromString("item"),
        id = EntityFactory.nextEntityID++,
        itemStack = item,
        pos = data.Position,
        rot = new Vector3(20f, 0f, 20f),
        lifetime = 60 * 20, // 20 minutes, in case their inventory is full
        belongsPlayerId = data.EntityId
      });
      world.SpawnEntityInWorld(loot);
      client.SendPackage(NetPackageManager.GetPackage<NetPackageEntityCollect>().Setup(loot.entityId, client.entityId));
      world.RemoveEntity(loot.entityId, EnumRemoveEntityReason.Despawned);
      return true;
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.CopyPropertiesFromEntityClass))]
    public class EntityAlive_CopyPropertiesFromEntityClass_Patch {
      private static void Postfix(EntityAlive __instance) {
        EntityClass entityClass = EntityClass.list[__instance.entityClass];

        List<ItemStack> backpackItems = new();
        if (!entityClass.Properties.Classes.TryGetValue(PropBackpackItemsOnEnterGame,
              out DynamicProperties itemsByGameMode)) {
          Log.Out("[BackpackItemsOnEnterGame] BackpackItemsOnEnterGame class not found");
          __instance.SetBackpackItemsOnEnterGame(backpackItems);
          return;
        }

        var gameMode = GameMode.GetGameModeForId(GameStats.GetInt(EnumGameStats.GameModeId))?.GetTypeName();
        if (gameMode is null || !itemsByGameMode.TryGetValue(gameMode, out var items)) {
          Log.Out($"[BackpackItemsOnEnterGame] GameMode '{gameMode}' not found");
          __instance.SetBackpackItemsOnEnterGame(backpackItems);
          return;
        }

        foreach (var item in items.Split(",")) {
          var itemStack = ItemStack.FromString(item.Trim());
          if (itemStack.itemValue.IsEmpty()) {
            Log.Error($"Item with name '{item}' not found");
            continue;
          }

          backpackItems.Add(itemStack);
        }

        Log.Out($"[BackpackItemsOnEnterGame] BackpackItemsOnEnterGame: {string.Join(",", backpackItems)}");
        __instance.SetBackpackItemsOnEnterGame(backpackItems);
      }
    }
  }

  public static class EntityAliveExtensions {
    // When the EntityAlive instance is garbage collected, its attached list is automatically cleaned up.
    private static readonly ConditionalWeakTable<EntityAlive, List<ItemStack>> BackpackData = new();

    public static List<ItemStack> GetBackpackItemsOnEnterGame(this EntityAlive entity) {
      return BackpackData.GetValue(entity, _ => new List<ItemStack>());
    }

    public static void SetBackpackItemsOnEnterGame(this EntityAlive entity, List<ItemStack> items) {
      BackpackData.Remove(entity);
      BackpackData.Add(entity, items);
    }
  }
}

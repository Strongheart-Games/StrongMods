using System;
using System.Collections.Generic;

namespace AutoCollectLoot {
  public static class LootItems {
    private const string PropAutoLootSubstituteFor = "AutoLootSubstituteFor";

    private static Dictionary<string, ItemClass> s_entitiesToItems;

    public static bool TryGetLootItem(string entityClassName, out ItemClass lootItemClass) {
      if (s_entitiesToItems is null) {
        InitEntitiesToItems();
      }

      lootItemClass = null;
      return s_entitiesToItems is null || s_entitiesToItems.TryGetValue(entityClassName, out lootItemClass);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<ItemClass>> GetSubstituteItems() {
      var substitutes = new Dictionary<string, IReadOnlyList<ItemClass>>(StringComparer.OrdinalIgnoreCase);
      foreach (ItemClass item in ItemClass.nameToItem.Values) {
        if (item?.Properties is null || !item.Properties.Contains(PropAutoLootSubstituteFor)) {
          continue;
        }
        var entityClassName = "";
        item.Properties.ParseString(PropAutoLootSubstituteFor, ref entityClassName);
        if (string.IsNullOrEmpty(entityClassName)) {
          continue;
        }
        if (!substitutes.TryGetValue(entityClassName, out IReadOnlyList<ItemClass> mapped)) {
          mapped = new List<ItemClass>();
          substitutes.Add(entityClassName, mapped);
        }
        ((List<ItemClass>)mapped).Add(item);
      }
      return substitutes;
    }

    private static void InitEntitiesToItems() {
      var entitiesToItems = new Dictionary<string, ItemClass>(StringComparer.OrdinalIgnoreCase);
      foreach (ItemClass item in ItemClass.nameToItem.Values) {
        if (item is null) {
          Log.Error("[AutoCollectLoot] Null item found in ItemClass.nameToItem.");
          continue;
        }

        if (item.Properties is null) {
          Log.Error($"[AutoCollectLoot] {item.GetItemName()}.Properties is null.");
          continue;
        }

        if (item.Properties.Contains(PropAutoLootSubstituteFor)) {
          var entityClassName = "";
          item.Properties.ParseString(PropAutoLootSubstituteFor, ref entityClassName);
          if (string.IsNullOrEmpty(entityClassName)) {
            continue;
          }

          entitiesToItems.Add(entityClassName, item);
        }
      }

      s_entitiesToItems = entitiesToItems;
    }
  }
}

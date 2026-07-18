using System;
using System.Collections.Generic;
using System.Linq;
using StrongMods;

namespace AutoCollectLoot {
  public static class LootItems {
    private const string PropAutoLootSubstituteFor = "AutoLootSubstituteFor";

    private const string MeshZpackYellow = "@:Entities/LootContainers/zpackPrefab.prefab";
    private const string MeshZpackBlue = "@:Entities/LootContainers/zpackBluePrefab.prefab";
    private const string MeshZpackRed = "@:Entities/LootContainers/zpackRedPrefab.prefab";
    private const string MeshZpackGold = "@:Entities/LootContainers/zpackGoldPrefab.prefab";
    private const string MeshDuffle = "@:Entities/LootContainers/duffle01Prefab.prefab";

    private const string IconSportsBag = "cntSportsBag02White";
    private const string IconDuffle = "cntDuffle01";

    private const string TintZpackYellow = "#78783A";
    private const string TintZpackBlue = "#276182";
    private const string TintZpackRed = "#793B3E";
    private const string TintZpackGold = "#D34B08";
    private const string TintArmyGreen = "#4B5320";
    private const string TintWhite = "#FFFFFF";

    private static readonly Dictionary<string, IconConfig> s_meshesToTints = new() {
      { MeshZpackYellow, new IconConfig(IconSportsBag, TintZpackYellow) },
      { MeshZpackBlue, new IconConfig(IconSportsBag, TintZpackBlue) },
      { MeshZpackRed, new IconConfig(IconSportsBag, TintZpackRed) },
      { MeshZpackGold, new IconConfig(IconSportsBag, TintZpackGold) },
      { MeshDuffle, new IconConfig(IconDuffle, TintWhite) }
    };

    private static Dictionary<string, ItemClass> s_entitiesToItems;

    public static bool TryGetLootItem(string entityClassName, out ItemClass lootItemClass) {
      if (s_entitiesToItems is null) {
        InitEntitiesToItems();
      }

      lootItemClass = null;
      return s_entitiesToItems is null || s_entitiesToItems.TryGetValue(entityClassName, out lootItemClass);
    }

    [XmlPatchFunction]
    public static string CustomIcon(string mesh) {
      if (s_meshesToTints.TryGetValue(mesh, out IconConfig config)) {
        return config.Icon;
      }

      Log.Warning($"[AutoCollectLoot] No icon found for {mesh}; using default.");
      return IconSportsBag;
    }

    [XmlPatchFunction]
    public static string CustomTint(string mesh) {
      if (s_meshesToTints.TryGetValue(mesh, out IconConfig config)) {
        return config.Tint;
      }

      Log.Warning($"[AutoCollectLoot] No tint found for {mesh}; using default.");
      return TintArmyGreen;
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

    private readonly struct IconConfig {
      public string Icon { get; }
      public string Tint { get; }

      public IconConfig(string icon, string tint) {
        Icon = icon;
        Tint = tint;
      }
    }
  }
}

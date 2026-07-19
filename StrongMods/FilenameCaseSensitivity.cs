using System;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace StrongMods {
  public static class FilenameCaseSensitivity {
    private const string ModInfoFilename = "ModInfo.xml";

    public static void ValidateModInfos() {
      for (var index = 0; index < ModManager.loadedMods.Count; ++index) {
        Mod mod = ModManager.loadedMods.list[index];
        mod.SetInvalidModInfo(!HasValidModInfo(mod));
      }
    }

    public static void UnloadInvalidModInfos() {
      for (var index = 0; index < ModManager.loadedMods.Count; ++index) {
        Mod mod = ModManager.loadedMods.list[index];
        if (!mod.HasInvalidModInfo()) {
          continue;
        }

        Log.Error($"[MODS] Unloading invalid mod '{mod.Name}'.");
        ModManager.loadedMods.Remove(mod.Name);
        ModManager.failedMods.Add(mod);
      }
    }

    private static bool HasValidModInfo(Mod mod) {
      if (mod is null) {
        return false;
      }

      var directory = mod.Path;
      if (string.IsNullOrEmpty(directory)) {
        return false;
      }

      var files = Directory.GetFiles(directory, ModInfoFilename);
      if (files.Length == 0) {
        return false;
      }

      var actualFilename = Path.GetFileName(files[0]);
      if (string.Equals(actualFilename, ModInfoFilename, StringComparison.Ordinal)) {
        return true;
      }

      Log.Error(
        $"[MODS]     Folder {directory} contains '{actualFilename}'. It must be renamed to exactly '{ModInfoFilename}' to work on Linux.");
      return false;
    }

    [HarmonyPatch(typeof(Mod), nameof(Mod.InitModCode))]
    public class Mod_InitModCode_Patch {
      private static bool Prefix(Mod __instance) {
        if (!__instance.HasInvalidModInfo()) {
          return true;
        }

        Log.Error($"[MODS]   Skipping initialization of invalid mod {__instance.Name}");
        return false;
      }
    }

    [HarmonyPatch(typeof(Localization), nameof(Localization.LoadPatchDictionaries))]
    public class Localization_LoadPatchDictionaries_Patch {
      private static bool Prefix(string _modName) {
        Mod mod = ModManager.GetMod(_modName);
        if (mod is null || !mod.HasInvalidModInfo()) {
          return true;
        }

        Log.Error($"[MODS] Skipping localization from invalid mod: {mod.Name}");
        return false;
      }
    }
  }

  public static class ModExtensions {
    // Negative logic: assume valid until a problem is confirmed.
    private static readonly ConditionalWeakTable<Mod, StrongBox<bool>> s_hasInvalidModInfo = new();

    public static bool HasInvalidModInfo(this Mod mod) {
      return s_hasInvalidModInfo.TryGetValue(mod, out StrongBox<bool> flag) && flag.Value;
    }

    public static void SetInvalidModInfo(this Mod mod, bool value) {
      s_hasInvalidModInfo.GetOrCreateValue(mod).Value = value;
    }
  }
}

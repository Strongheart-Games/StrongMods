using System.Runtime.CompilerServices;
using HarmonyLib;

namespace StrongMods {
  /// <summary>
  ///   Central mod-unloading machinery. Features that detect a fatal problem with a mod (invalid ModInfo casing,
  ///   unsatisfied dependencies, ...) report it via <see cref="MarkForUnload" />. The mod can't be removed immediately —
  ///   detection happens inside a foreach over the loaded-mod list — so it is marked and suppressed in the meantime:
  ///   code initialization and localization are skipped via Harmony prefixes, and the actual unload runs at GameAwake,
  ///   before XML patches and other passive content are applied, so a doomed mod never half-loads.
  /// </summary>
  public static class ModUnloader {
    public const string Category = "ModUnloader";

    private static readonly ConditionalWeakTable<Mod, StrongBox<string>> s_unloadReasons = new();
    private static bool s_initialized;

    /// <summary>Applies the suppression patches and registers the unload hook. Safe to call more than once.</summary>
    public static void Init(Harmony harmony) {
      if (s_initialized) {
        return;
      }

      s_initialized = true;
      harmony.PatchCategory(Category);
      // Can't unload during mod init because that runs within a foreach over the mod list we want to modify
      ModEvents.GameAwake.RegisterHandler(OnGameAwake);
    }

    /// <summary>Marks the mod for unloading; the removal itself happens at GameAwake.</summary>
    public static void MarkForUnload(Mod mod, string reason) {
      StrongBox<string> box = s_unloadReasons.GetOrCreateValue(mod);
      // First reported reason wins; it's the root cause
      box.Value ??= reason;
    }

    public static bool IsMarkedForUnload(Mod mod) {
      return TryGetUnloadReason(mod, out _);
    }

    public static bool TryGetUnloadReason(Mod mod, out string reason) {
      reason = null;
      if (!s_unloadReasons.TryGetValue(mod, out StrongBox<string> box) || box.Value is null) {
        return false;
      }

      reason = box.Value;
      return true;
    }

    public static void UnloadPendingMods() {
      // Iterate backwards so removals don't skip the following entry
      for (var index = ModManager.loadedMods.Count - 1; index >= 0; index--) {
        Mod mod = ModManager.loadedMods.list[index];
        if (!TryGetUnloadReason(mod, out var reason)) {
          continue;
        }

        Log.Error($"[ModUnloader] Mod '{mod.Name}' was prevented from loading: {reason}");
        ModManager.loadedMods.Remove(mod.Name);
        ModManager.failedMods.Add(mod);
      }
    }

    private static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      UnloadPendingMods();
    }

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(Mod), nameof(Mod.InitModCode))]
    public class Mod_InitModCode_Patch {
      private static bool Prefix(Mod __instance) {
        if (!TryGetUnloadReason(__instance, out var reason)) {
          return true;
        }

        Log.Error($"[ModUnloader]   Skipping code initialization of mod '{__instance.Name}' marked for unloading: " +
                  reason);
        return false;
      }
    }

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(Localization), nameof(Localization.LoadPatchDictionaries))]
    public class Localization_LoadPatchDictionaries_Patch {
      private static bool Prefix(string _modName) {
        Mod mod = ModManager.GetMod(_modName);
        if (mod is null || !TryGetUnloadReason(mod, out var reason)) {
          return true;
        }

        Log.Error($"[ModUnloader] Skipping localization from mod '{mod.Name}' marked for unloading: {reason}");
        return false;
      }
    }
  }
}

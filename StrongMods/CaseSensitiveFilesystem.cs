using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace StrongMods {
  public static class CaseSensitiveFilesystem {
    private const string ModInfoFilename = "ModInfo.xml";

    public static bool Exists(string path) {
      if (string.IsNullOrEmpty(path)) {
        return false;
      }

      var fullPath = Path.GetFullPath(path);
      var root = Path.GetPathRoot(fullPath);

      // Extract everything after the root to parse segment by segment
      var pathWithoutRoot = fullPath.Substring(root.Length);
      var segments = pathWithoutRoot.Split(
        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
        StringSplitOptions.RemoveEmptyEntries
      );

      var currentPath = root;

      // Use for here to get maximum performance given this method is called frequently.
      // ReSharper disable once LoopCanBeConvertedToQuery ForCanBeConvertedToForeach
      for (var i = 0; i < segments.Length; i++) {
        var expectedSegment = segments[i];

        // Failsafe: if the current directory path doesn't exist, the chain is broken
        if (!Directory.Exists(currentPath)) {
          return false;
        }

        var found = false;
        var isExactMatch = false;
        string actualName = null;

        // EnumerateFileSystemEntries is used here instead of GetFiles/GetDirectories
        // to minimize memory allocations since this method is called frequently.
        // ReSharper disable once LoopCanBeConvertedToQuery
        IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(currentPath);
        foreach (var entry in entries) {
          var entryName = Path.GetFileName(entry);

          if (!string.Equals(entryName, expectedSegment, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }

          found = true;
          actualName = entryName;
          isExactMatch = string.Equals(entryName, expectedSegment, StringComparison.Ordinal);
          break;
        }

        // If it's completely missing (even ignoring case), then the path is just invalid
        if (!found) {
          return false;
        }

        if (!isExactMatch) {
          Log.Error("[StrongMods] Path casing mismatch detected! You might want to check your casing. " +
                    $"Expected '{expectedSegment}' but found '{actualName}' inside '{currentPath}'. " +
                    $"Full requested path: '{path}'");
          return false;
        }

        currentPath = Path.Combine(currentPath, actualName);
      }

      return true;
    }

    public static void ValidateModInfos() {
      for (var index = 0; index < ModManager.loadedMods.Count; ++index) {
        Mod mod = ModManager.loadedMods.list[index];
        var valid = Exists(Path.Combine(mod.Path, ModInfoFilename));
        mod.SetInvalidModInfo(!valid);
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

    // Replace only specific calls to Exists() because we don't want to incur the extra but unnecessary costs of the
    // case-sensitive checks while the game is running, only during startup and loading.
    public static void ApplyExistsPatches(Harmony harmony) {
      List<MethodInfo> targetMethods = new() {
        AccessTools.Method(typeof(Localization), nameof(Localization.LoadPatchDictionaries)),
        GetMoveNext(AccessTools.Method(typeof(ModManager), nameof(ModManager.LoadUiAtlases))),
        GetMoveNext(AccessTools.Method(typeof(ModManager), nameof(ModManager.LoadLocalizations))),
        AccessTools.Method(typeof(XmlPatchMethods), nameof(XmlPatchMethods.Include))
      };
      MethodInfo transpiler = AccessTools.Method(typeof(CaseSensitiveFilesystem), nameof(ReplaceFileOrDirectoryExists));
      foreach (MethodInfo target in targetMethods) {
        if (target is null) {
          continue;
        }

        Log.Out($"[CaseSensitiveFilesystem] Replacing Exists() calls in {target.Name}");
        harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
      }
    }

    private static MethodInfo GetMoveNext(MethodInfo method) {
      Type[] nestedTypes = method?.DeclaringType?.GetNestedTypes();
      if (nestedTypes is null) {
        return null;
      }

      foreach (Type type in nestedTypes) {
        // The compiler-generated classes implementing IEnumerator typically have MoveNext()
        if (!type.Name.Contains($"<{method.Name}>d__")) {
          continue;
        }

        MethodInfo moveNextMethod = type.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public);
        if (moveNextMethod != null) {
          return moveNextMethod;
        }
      }

      Log.Warning($"[CaseSensitiveFilesystem] No MoveNext() method found for {method.Name}");
      return null;
    }

    private static IEnumerable<CodeInstruction> ReplaceFileOrDirectoryExists(
      IEnumerable<CodeInstruction> instructions) {
      MethodInfo replacementMethod = SymbolExtensions.GetMethodInfo(() => Exists(null));
      HashSet<MethodInfo> callsToMatch = new() {
        SymbolExtensions.GetMethodInfo(() => File.Exists(null)),
        SymbolExtensions.GetMethodInfo(() => Directory.Exists(null)),
        SymbolExtensions.GetMethodInfo(() => SdFile.Exists(null)),
        SymbolExtensions.GetMethodInfo(() => SdDirectory.Exists(null))
      };

      var found = false;
      var codes = new List<CodeInstruction>(instructions);
      for (var i = 0; i < codes.Count; i++) {
        CodeInstruction instruction = codes[i];
        if (instruction.opcode != OpCodes.Call ||
            instruction.operand is not MethodInfo calledMethod ||
            !callsToMatch.Contains(calledMethod)) {
          continue;
        }

        codes[i] = new CodeInstruction(OpCodes.Call, replacementMethod);
        found = true;
      }

      if (!found) {
        // Throw rather than logging because the code was expecting to find a call; this is a bug
        throw new InvalidOperationException("[CaseSensitiveFilesystem] Cannot find any Exists() calls.");
      }

      return codes.AsEnumerable();
    }

    [HarmonyPatchCategory("CaseSensitiveFilesystem")]
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

    [HarmonyPatchCategory("CaseSensitiveFilesystem")]
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
}

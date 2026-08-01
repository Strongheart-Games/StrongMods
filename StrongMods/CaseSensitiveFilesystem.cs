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

    public const string Category = "CaseSensitiveFilesystem";

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
        if (Exists(Path.Combine(mod.Path, ModInfoFilename))) {
          continue;
        }

        ModUnloader.MarkForUnload(mod, $"its {ModInfoFilename} could not be found using case-sensitive path matching");
      }
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

    // Replace only specific calls to Exists() because we don't want to incur the extra but unnecessary costs of the
    // case-sensitive checks while the game is running, only during startup and loading. The four patch classes
    // share one transpiler body, ReplaceFileOrDirectoryExists — each Transpiler is a one-line delegate to it.
    // The two coroutine targets use MethodType.Enumerator, which resolves the compiler-generated MoveNext for us.

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(Localization), nameof(Localization.LoadPatchDictionaries))]
    public static class LocalizationLoadPatchDictionariesPatch {
      public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        return ReplaceFileOrDirectoryExists(instructions);
      }
    }

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LoadUiAtlases), MethodType.Enumerator)]
    public static class ModManagerLoadUiAtlasesPatch {
      public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        return ReplaceFileOrDirectoryExists(instructions);
      }
    }

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LoadLocalizations), MethodType.Enumerator)]
    public static class ModManagerLoadLocalizationsPatch {
      public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        return ReplaceFileOrDirectoryExists(instructions);
      }
    }

    [HarmonyPatchCategory(Category)]
    [HarmonyPatch(typeof(XmlPatchMethods), nameof(XmlPatchMethods.Include))]
    public static class XmlPatchMethodsIncludePatch {
      public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        return ReplaceFileOrDirectoryExists(instructions);
      }
    }
  }
}

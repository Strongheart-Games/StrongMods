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

    // Directories already reported as unlistable. Exists() runs once per mod per XML file, so without
    // this one unreadable ancestor would log on every call.
    private static readonly HashSet<string> s_unlistableDirs = new();

    // The roots the walk may start from, deepest first. Empty until Init.
    private static string[] s_anchors = new string[0];

    /// <summary>
    ///   Resolves the directories the casing walk may start from. Casing ABOVE one of them is not
    ///   mod-authored — the game resolved that prefix itself — so checking it catches no modder's mistake,
    ///   costs a directory listing per ancestor, and on a shared host reads directories belonging to other
    ///   tenants. That last one crashed a server (#89).
    /// </summary>
    public static void Init() {
      s_anchors = ResolveAnchors();
    }

    /// <summary>
    ///   The roots the GAME itself declares its content lives under, deepest first.
    ///   <para>
    ///     Taken from <c>ModManager.LoadMods</c>, which loads from TWO independently resolved folders and
    ///     skips the second only when <c>GameIO.PathsEquals</c> says it is the same place:
    ///     <c>ModsBasePath</c> (<c>GetDeviceLocalUserGameDataDir() + "/Mods"</c>, resolved through the
    ///     platform layer, so on a dedicated server it is wherever the user data folder points — routinely a
    ///     different disk or mount from the install) and <c>ModsBasePathLegacy</c>
    ///     (<c>&lt;game&gt;/Mods</c>). The game path itself is third, for the game's own Data and Config.
    ///   </para>
    ///   <para>
    ///     Anchoring at the game directory alone would be wrong, not merely narrow: every mod under
    ///     <c>ModsBasePath</c> would fall back to walking from the filesystem root, which is the behavior
    ///     #89 is about.
    ///   </para>
    /// </summary>
    private static string[] ResolveAnchors() {
      var roots = new List<string>();
      AddRoot(roots, () => ModManager.ModsBasePath);
      AddRoot(roots, () => ModManager.ModsBasePathLegacy);
      AddRoot(roots, () => GameIO.GetGamePath());
      // Deepest first, so a mods folder nested inside the game directory wins over the game directory.
      roots.Sort((left, right) => right.Length.CompareTo(left.Length));
      return roots.ToArray();
    }

    private static void AddRoot(List<string> roots, Func<string> resolve) {
      string root;
      try {
        root = resolve();
        if (string.IsNullOrWhiteSpace(root)) {
          return;
        }

        // The game's own normalizer: GetFullPath plus its trailing-separator trim. Using it rather than a
        // private equivalent keeps this agreeing with GameIO.PathsEquals, which is how the game compares
        // these same roots to each other.
        root = GameIO.GetNormalizedPath(root);
      } catch (Exception e) {
        Log.Warning($"[StrongMods] Could not resolve a content root ({e.GetType().Name}: {e.Message}), so " +
                    "paths under it will be case-checked from the filesystem root.");
        return;
      }

      // LoadMods compares its two roots with PathsEquals(..., ignoreCase: true), so de-duplicate the same
      // way. On an install where the user data folder IS the game folder, these collapse to one root.
      if (!roots.Exists(existing => string.Equals(existing, root, StringComparison.OrdinalIgnoreCase))) {
        roots.Add(root);
      }
    }

    /// <summary>
    ///   Stands in for File.Exists and Directory.Exists inside the patched game methods, answering the same
    ///   question but case-sensitively.
    ///   <para>
    ///     Those BCL methods never throw — they swallow every error and return false — and the game code
    ///     they were called from carries no handler for one. So this method must be total too: a failure it
    ///     cannot interpret logs once and degrades to the BCL answer (#89).
    ///   </para>
    /// </summary>
    public static bool Exists(string path) {
      // Whitespace-only is checked here rather than left to the catch below: File.Exists answers false for
      // it, and routing it through Path.GetFullPath would log a warning on every call for a path the game
      // may well probe repeatedly.
      if (string.IsNullOrWhiteSpace(path)) {
        return false;
      }

      try {
        return HasMatchingCase(path);
      } catch (Exception e) {
        Log.Warning($"[StrongMods] Could not check the casing of '{path}' ({e.GetType().Name}: {e.Message}), " +
                    "so it was checked case-insensitively instead.");
        return File.Exists(path) || Directory.Exists(path);
      }
    }

    private static bool HasMatchingCase(string path) {
      var fullPath = Path.GetFullPath(path);
      var anchor = AnchorFor(fullPath);

      // Extract everything after the anchor to parse segment by segment
      var pathBelowAnchor = fullPath.Substring(anchor.Length);
      var segments = pathBelowAnchor.Split(
        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
        StringSplitOptions.RemoveEmptyEntries
      );

      var currentPath = anchor;

      // Use for here to get maximum performance given this method is called frequently.
      // ReSharper disable once LoopCanBeConvertedToQuery ForCanBeConvertedToForeach
      for (var i = 0; i < segments.Length; i++) {
        var expectedSegment = segments[i];

        // Failsafe: if the current directory path doesn't exist, the chain is broken
        if (!Directory.Exists(currentPath)) {
          return false;
        }

        SegmentMatch match = FindEntry(currentPath, expectedSegment, out var actualName);

        // If it's completely missing (even ignoring case), then the path is just invalid
        if (match == SegmentMatch.Missing) {
          return false;
        }

        // Traversable but not listable, so the real spelling is unreadable and this segment goes
        // unchecked. Reporting absence instead would make ValidateModInfos unload the mod over a
        // permission quirk, which is a far worse answer than an unverified segment (#89).
        if (match == SegmentMatch.Unverifiable) {
          currentPath = Path.Combine(currentPath, expectedSegment);
          continue;
        }

        if (!string.Equals(actualName, expectedSegment, StringComparison.Ordinal)) {
          Log.Error("[StrongMods] Path casing mismatch detected! You might want to check your casing. " +
                    $"Expected '{expectedSegment}' but found '{actualName}' inside '{currentPath}'. " +
                    $"Full requested path: '{path}'");
          return false;
        }

        currentPath = Path.Combine(currentPath, actualName);
      }

      return true;
    }

    /// <summary>
    ///   The deepest declared content root holding <paramref name="fullPath" />, or the path root when no
    ///   root holds it. The fallback still walks from the filesystem root, which is wasteful but no longer
    ///   fatal: <see cref="Exists" /> absorbs whatever it hits on the way.
    /// </summary>
    private static string AnchorFor(string fullPath) {
      string[] anchors = s_anchors;
      // ReSharper disable once ForCanBeConvertedToForeach
      for (var i = 0; i < anchors.Length; i++) {
        if (IsUnder(fullPath, anchors[i])) {
          return anchors[i];
        }
      }

      return Path.GetPathRoot(fullPath);
    }

    private static bool IsUnder(string fullPath, string anchor) {
      if (fullPath.Length <= anchor.Length ||
          !fullPath.StartsWith(anchor, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      // Guard against a prefix that merely starts the same: "<game>Backup" is not inside "<game>".
      var next = fullPath[anchor.Length];
      return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    private enum SegmentMatch {
      Found,
      Missing,
      Unverifiable
    }

    /// <summary>
    ///   Looks <paramref name="expected" /> up in <paramref name="directory" /> ignoring case, reporting the
    ///   entry's real spelling. Unverifiable means the directory may be traversed but not listed — normal on
    ///   a multi-tenant host, and not an error.
    /// </summary>
    private static SegmentMatch FindEntry(string directory, string expected, out string actualName) {
      actualName = null;
      try {
        // EnumerateFileSystemEntries is used here instead of GetFiles/GetDirectories to minimize memory
        // allocations since this method is called frequently. It is lazy, so a permission failure can
        // surface either from the call or from the first step of the loop — both are inside this try.
        // ReSharper disable once LoopCanBeConvertedToQuery
        IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(directory);
        foreach (var entry in entries) {
          var entryName = Path.GetFileName(entry);

          if (!string.Equals(entryName, expected, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }

          actualName = entryName;
          return SegmentMatch.Found;
        }
      } catch (Exception e) when (e is UnauthorizedAccessException or IOException) {
        ReportUnlistable(directory, e);
        return SegmentMatch.Unverifiable;
      }

      return SegmentMatch.Missing;
    }

    private static void ReportUnlistable(string directory, Exception e) {
      lock (s_unlistableDirs) {
        if (!s_unlistableDirs.Add(directory)) {
          return;
        }
      }

      Log.Warning($"[StrongMods] Cannot list '{directory}' ({e.GetType().Name}), so path casing inside it " +
                  "goes unverified. Mods there load normally; this is not an error.");
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

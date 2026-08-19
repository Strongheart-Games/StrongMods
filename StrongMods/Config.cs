using System;
using System.IO;

namespace StrongMods {
  public static class Config {
    public static bool ServerOnlyClassEnabled => true;
    public static bool BreadthFirstXmlPatcherEnabled => true;
    public static bool XmlPatchMethodForeachEnabled => true; // requires BreadthFirstXmlPatcherEnabled
    public static bool XmlPatchMethodEnsureEnabled => true;
    public static bool XPathInheritanceEnabled => true;
    public static bool CaseSensitiveFilesystemEnabled => IsFilesystemCaseInsensitive();
    public static bool ModInfoDependenciesEnabled => true;

    /// <summary>
    ///   Whether the filesystem the game is installed on ignores case. The whole
    ///   <see cref="CaseSensitiveFilesystem" /> feature exists to make a case-insensitive dev machine behave
    ///   like a case-sensitive server, so it must stay off wherever the answer is already "case-sensitive".
    /// </summary>
    private static bool IsFilesystemCaseInsensitive() {
      try {
        return IsCaseInsensitiveAt(GameIO.GetGamePath());
      } catch (Exception e) {
        // This decides whether an OPTIONAL feature runs, and it is read from InitMod, where an exception
        // aborts mod loading. Unsure answers "case-sensitive": that costs a dev-machine diagnostic, where
        // the other way round runs an expensive and destructive walk in production (#90).
        Log.Warning($"[StrongMods] Could not determine filesystem case sensitivity ({e.GetType().Name}: " +
                    $"{e.Message}), so CaseSensitiveFilesystem stays off.");
        return false;
      }
    }

    /// <summary>Whether the directory still resolves when its own name is spelled in the opposite case.</summary>
    private static bool IsCaseInsensitiveAt(string directoryPath) {
      var opposite = OppositeCaseSpelling(directoryPath);
      return opposite != null && Directory.Exists(opposite);
    }

    /// <summary>
    ///   The same directory with its own name's case inverted, or null when there is nothing to invert.
    ///   <para>
    ///     Normalizing FIRST is load-bearing. <c>GameIO.GetGamePath()</c> returns the Unity data path with a
    ///     literal "/.." concatenated on the end, and <c>Directory.Exists</c> collapses ".." lexically — so
    ///     inverting the raw string inverts only the data-directory segment, which the ".." then erases
    ///     before any syscall. The probe ends up asking "does the install directory exist?", always answers
    ///     yes, and reports every filesystem case-insensitive, Linux included (#90). Only the LEAF segment
    ///     is inverted, because no later ".." can cancel it.
    ///   </para>
    /// </summary>
    private static string OppositeCaseSpelling(string directoryPath) {
      if (string.IsNullOrWhiteSpace(directoryPath)) {
        return null;
      }

      var fullPath = Path.GetFullPath(directoryPath);
      var parent = Path.GetDirectoryName(fullPath);
      var leaf = Path.GetFileName(fullPath);
      if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) {
        return null;
      }

      var inverted = leaf.Equals(leaf.ToLowerInvariant(), StringComparison.Ordinal)
        ? leaf.ToUpperInvariant()
        : leaf.ToLowerInvariant();

      // Digits and punctuation invert to themselves, and asking about an identical spelling proves nothing.
      return inverted.Equals(leaf, StringComparison.Ordinal) ? null : Path.Combine(parent, inverted);
    }
  }
}

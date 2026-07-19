using System;
using System.IO;

namespace StrongMods {
  public static class Config {
    public static bool BreadthFirstXmlPatcherEnabled => true;
    public static bool XmlPatchMethodForeachEnabled => true; // requires BreadthFirstXmlPatcherEnabled
    public static bool CaseSensitiveFilesystemEnabled => IsFilesystemCaseInsensitive();

    private static bool IsFilesystemCaseInsensitive() {
      var directoryPath = GameIO.GetGamePath();
      if (!Directory.Exists(directoryPath)) {
        throw new DirectoryNotFoundException($"The path '{directoryPath}' does not exist.");
      }

      // Check if the current directory can be found using the opposite casing
      var mixedCasePath = directoryPath.Equals(directoryPath.ToLowerInvariant(), StringComparison.Ordinal)
        ? directoryPath.ToUpperInvariant()
        : directoryPath.ToLowerInvariant();

      return Directory.Exists(mixedCasePath);
    }
  }
}

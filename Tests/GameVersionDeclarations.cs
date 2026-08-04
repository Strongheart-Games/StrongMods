using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Tests;

/// <summary>
///   The declared game versions, read the way the build reads them (#23): repo-wide defaults and the version
///   registry from build\GameVersions.props, per-mod pins from each csproj's leading declaration block.
///   Shared by ModInventory (consumption: which versions each mod tests against) and the closure test
///   (validation), so the two cannot drift — except the closure test's FourPart recomputation, which is the
///   part that must stay independent.
/// </summary>
public sealed class GameVersionDeclarations {
  private GameVersionDeclarations(string defaultDev, IReadOnlyList<string> defaultTest,
                                  IReadOnlyDictionary<string, string> registry) {
    DefaultDevVersion = defaultDev;
    DefaultTestVersions = defaultTest;
    Registry = registry;
  }

  public string DefaultDevVersion { get; }
  public IReadOnlyList<string> DefaultTestVersions { get; }

  /// <summary>label → package version, straight from SdtdGameVersionMap. Lenient on malformed rows — the
  ///   closure test owns format validation and reports them readably.</summary>
  public IReadOnlyDictionary<string, string> Registry { get; }

  public static GameVersionDeclarations Load(string repoRoot) {
    XDocument props = XDocument.Load(Path.Combine(repoRoot, "build", "GameVersions.props"));
    string One(string name) => props.Descendants().First(e => e.Name.LocalName == name).Value;
    Dictionary<string, string> registry = SplitList(One("SdtdGameVersionMap"))
      .Select(pair => pair.Split('='))
      .Where(pair => pair.Length == 2)
      .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());
    return new GameVersionDeclarations(One("SdtdDevVersion").Trim(),
      SplitList(One("SdtdTestVersions")), registry);
  }

  /// <summary>This project's effective declaration: its leading block, else the repo defaults.</summary>
  public (string Dev, IReadOnlyList<string> Test) For(string csprojPath) {
    XDocument document = XDocument.Load(csprojPath);
    string Own(string name) =>
      document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
    var test = Own("SdtdTestVersions");
    return (Own("SdtdDevVersion") ?? DefaultDevVersion,
      test == null ? DefaultTestVersions : SplitList(test));
  }

  /// <summary>Semicolon-list split with MSBuild's item semantics: entries trimmed, empties dropped.</summary>
  public static List<string> SplitList(string value) =>
    value.Split(';').Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToList();
}

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tests;

/// <summary>
///   The code mods under test, derived from the repo rather than hardcoded (plan D5): every top-level
///   directory whose csproj imports build\Mod.props, except the Template* scaffolds (not shippable mods;
///   CLAUDE.md carves them out of "one directory = one mod"). A code mod whose DLL is missing is a hard
///   failure, not a silent coverage shrink.
/// </summary>
public sealed class ModInventory {
  private ModInventory(List<CodeMod> mods, List<string> missing) {
    Mods = mods;
    MissingDlls = missing;
  }

  public IReadOnlyList<CodeMod> Mods { get; }
  public IReadOnlyList<string> MissingDlls { get; }

  public static ModInventory Scan() {
    var repoRoot = Path.GetFullPath(GameTree.Metadata("RepoRoot"));
    var configuration = GameTree.Metadata("Configuration");
    var mods = new List<CodeMod>();
    var missing = new List<string>();

    foreach (var dir in Directory.GetDirectories(repoRoot).OrderBy(d => d)) {
      var name = Path.GetFileName(dir);
      var csproj = Path.Combine(dir, name + ".csproj");
      if (name.StartsWith("Template") || !File.Exists(csproj) ||
          !File.ReadAllText(csproj).Contains(@"build\Mod.props")) {
        continue;
      }

      var dllPath = Path.Combine(dir, "bin", configuration, name + ".dll");
      if (!File.Exists(dllPath)) {
        missing.Add($"{name}: expected '{dllPath}' — build the solution first (dotnet build StrongMods.sln " +
                    $"-c {configuration})");
        continue;
      }

      mods.Add(new CodeMod { Name = name, Dir = dir, DllPath = dllPath });
    }

    return new ModInventory(mods, missing);
  }

  /// <summary>Source files of a mod, excluding build output and AI artifacts — for the conformance scan.</summary>
  public static IEnumerable<string> SourceFiles(CodeMod mod) {
    return Directory.EnumerateFiles(mod.Dir, "*.cs", SearchOption.AllDirectories)
      .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                  !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                  !f.Contains($"{Path.DirectorySeparatorChar}.ai{Path.DirectorySeparatorChar}"));
  }

  public sealed class CodeMod {
    public string Dir;
    public string DllPath;
    public string Name;
  }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Tests;

/// <summary>
///   The unit under test: the game tree $(SdtdDir) resolved to at build time, with its assemblies (and the
///   mod DLLs) loaded into an isolated AssemblyLoadContext. 0Harmony is deliberately deferred to the default
///   context — the copy deployed beside the test assembly — so [HarmonyPatch] attribute instances read from
///   mod assemblies share type identity with the HarmonyLib types this project compiles against.
/// </summary>
public sealed class GameTree {
  private readonly UnitLoadContext context;

  private GameTree(string managedDir, IReadOnlyList<string> modBinDirs) {
    ManagedDir = managedDir;
    Root = Path.GetFullPath(Path.Combine(managedDir, "..", ".."));
    Label = DescribeVersion(managedDir);
    context = new UnitLoadContext(managedDir, modBinDirs);
    AssemblyCSharp = context.LoadFromAssemblyPath(Path.Combine(managedDir, "Assembly-CSharp.dll"));
  }

  public string ManagedDir { get; }
  public string Root { get; }

  /// <summary>Version identity carried by every failure message (plan D10), e.g. "V3.1.0-b14, game".</summary>
  public string Label { get; }

  public Assembly AssemblyCSharp { get; }

  public static GameTree Load(IReadOnlyList<string> modBinDirs) {
    var managedDir = Metadata("SdtdManagedDir");
    if (!Directory.Exists(managedDir)) {
      throw new DirectoryNotFoundException(
        $"Unit under test not found: '{managedDir}' does not exist. The tests run against whatever " +
        "$(SdtdDir) resolved to at build time — point it at a live install or a vendored tree, e.g. " +
        "dotnet test -p:SdtdDir=vendor/game/V3.1.0-b14");
    }

    return new GameTree(managedDir, modBinDirs);
  }

  public Assembly LoadModAssembly(string dllPath) {
    return context.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
  }

  public static string Metadata(string key) {
    return typeof(GameTree).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .First(a => a.Key == key).Value;
  }

  private static string DescribeVersion(string managedDir) {
    var dataDirName = new DirectoryInfo(Path.Combine(managedDir, "..")).Name;
    var unit = dataDirName.Contains("Server") ? "dedicated-server" : "game";
    var root = Path.GetFullPath(Path.Combine(managedDir, "..", ".."));

    // Vendored trees and CI package trees carry a manifest with the human version label; a live install
    // falls back to the assembly's file version.
    var manifestPath = Path.Combine(root, "manifest.json");
    if (File.Exists(manifestPath)) {
      using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (manifest.RootElement.TryGetProperty("label", out JsonElement label)) {
        return $"{label.GetString()}, {unit}";
      }
    }

    var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(managedDir, "Assembly-CSharp.dll"));
    var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
    return string.IsNullOrEmpty(version) || version.StartsWith("0.0.0")
      ? $"live install, {unit}"
      : $"{version}, {unit}";
  }

  private sealed class UnitLoadContext : AssemblyLoadContext {
    private readonly List<string> searchDirs;

    public UnitLoadContext(string managedDir, IReadOnlyList<string> modBinDirs) : base("unit-under-test") {
      searchDirs = new List<string> { managedDir };
      searchDirs.AddRange(modBinDirs);
    }

    protected override Assembly Load(AssemblyName name) {
      // The default context first: framework assemblies must unify with the host runtime's (the game's
      // Unity mscorlib must never load here), and 0Harmony must be the single copy deployed beside the
      // test assembly so HarmonyLib type identity is shared. Only what the host cannot provide is loaded
      // from the unit's own directories.
      try {
        return Default.LoadFromAssemblyName(name);
      } catch (Exception) {
        // not a framework or test-dependency assembly — resolve it from the unit under test
      }

      foreach (var dir in searchDirs) {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) {
          return LoadFromAssemblyPath(path);
        }
      }

      return null;
    }
  }
}

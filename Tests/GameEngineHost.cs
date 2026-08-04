using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Tests;

/// <summary>
///   Hosts the REAL game engine — not a stub. A HOST is one isolated assembly universe: the game's Managed
///   assemblies plus a deliberately chosen set of companions, loaded into a private AssemblyLoadContext so
///   its types never unify with the test runtime's or another host's; expensive to build, so lazily one per
///   game tree per session (SmokeTestCtx caches them). This host's companions are the mod DLLs declaring the
///   tree's version — with the real UnityEngine.CoreModule, so patch targets whose signatures use Unity
///   types resolve with full fidelity. The trade: nothing here can EXECUTE game code that logs (the real
///   CoreModule throws headlessly); executing the patcher is <see cref="Fixtures.PatcherHost" />'s job, with
///   a stub. Nothing crosses between the two.
/// </summary>
public sealed class GameEngineHost {
  private readonly UnitLoadContext context;

  public GameEngineHost(GameTree tree, IReadOnlyList<string> modBinDirs) {
    if (!Directory.Exists(tree.ManagedDir)) {
      throw new DirectoryNotFoundException(
        $"The game tree for {tree.Label} is missing at '{tree.Root}'. Every declared version must be present " +
        "to test against — unverifiable must not decay into unverified. Restore the declared versions:  " +
        "dotnet restore build/GameAssemblies.csproj --packages packages --configfile " +
        "build/GameAssemblies.nuget.config  (-p:SdtdDir=<tree> remains the one-off escape hatch).");
    }

    Tree = tree;
    EnsureHarmonyResolver();
    context = new UnitLoadContext(tree.ManagedDir, modBinDirs);
    AssemblyCSharp = context.LoadFromAssemblyPath(Path.Combine(tree.ManagedDir, "Assembly-CSharp.dll"));
  }

  public GameTree Tree { get; }
  public Assembly AssemblyCSharp { get; }

  public Assembly LoadModAssembly(string dllPath) => context.LoadFromAssemblyPath(Path.GetFullPath(dllPath));

  // 0Harmony is compile-time referenced with Private=false (see Tests.csproj for why nothing may be copied
  // from the game folder), so the default context must find it at runtime: fallback resolution to the
  // build's $(SdtdHarmonyDir), installed once. ONE copy process-wide, shared by every host, so HarmonyLib
  // type identity holds across hosts and with this assembly — risk R2 in .ai/testing-declared-versions.md:
  // assumed version-compatible across game versions while TFP ships Harmony 2.x; the failure mode is loud.
  private static readonly Lazy<bool> HarmonyResolver = new(() => {
    var harmonyDir = AssemblyMetadata.Get("SdtdHarmonyDir");
    AssemblyLoadContext.Default.Resolving += (context, name) => {
      var path = Path.Combine(harmonyDir, name.Name + ".dll");
      return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    };
    return true;
  });

  /// <summary>Installs the default-context 0Harmony resolver, once. PatcherHost calls this too — it used to
  ///   ride on a static-ctor side effect of "touching GameTree first"; now it is an explicit call.</summary>
  internal static void EnsureHarmonyResolver() => _ = HarmonyResolver.Value;

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

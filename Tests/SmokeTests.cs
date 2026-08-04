using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>
///   The smoke tests' shared, lazily-built state (#23 phase 6a): one <see cref="GameEngineHost" /> per
///   declared version label, each loading ONLY the mods that declare that label — a pinned-away mod is never
///   even type-loaded against a tree it disclaims, because that load blowing up is precisely what its pin
///   exempts it from.
/// </summary>
internal static class SmokeTestCtx {
  internal static readonly Lazy<ModInventory> Inventory = new(ModInventory.Scan);

  /// <summary>False when an explicit -p:SdtdDir override redirected the build (the escape hatch): the suite
  ///   then runs every mod against that ONE tree — declared-label filtering is meaningless against an
  ///   arbitrary tree. The build bakes the verdict; the tests never probe.</summary>
  internal static readonly bool TreeIsDeclared = AssemblyMetadata.Get("SdtdTreeIsDeclared") == "true";

  internal const string EscapeLabel = "SdtdDir override";

  /// <summary>The version axis: every label any mod declares, or the single escape pseudo-label.</summary>
  internal static readonly Lazy<IReadOnlyList<string>> Labels = new(() => TreeIsDeclared
    ? Inventory.Value.Mods.SelectMany(m => m.TestVersions).Distinct().OrderBy(l => l).ToList()
    : new List<string> { EscapeLabel });

  internal static IReadOnlyList<ModInventory.CodeMod> ModsFor(string label) => TreeIsDeclared
    ? Inventory.Value.Mods.Where(m => m.TestVersions.Contains(label)).ToList()
    : Inventory.Value.Mods;

  private static readonly ConcurrentDictionary<string, Lazy<LoadedLabel>> Loaded = new();

  internal static LoadedLabel For(string label) =>
    Loaded.GetOrAdd(label, l => new Lazy<LoadedLabel>(() => new LoadedLabel(l))).Value;

  /// <summary>One label's universe: its host and everything the tests derive from the loaded mods.</summary>
  internal sealed class LoadedLabel {
    internal LoadedLabel(string label) {
      IReadOnlyList<ModInventory.CodeMod> mods = ModsFor(label);
      GameTree tree = TreeIsDeclared ? GameTree.ForLabel(label) : GameTree.Default();
      Host = new GameEngineHost(tree,
        mods.Select(m => Path.GetDirectoryName(m.DllPath)).Distinct().ToList());
      Assemblies = mods.Select(m => (m.Name, Host.LoadModAssembly(m.DllPath))).ToList();
      PatchClasses = Assemblies
        .SelectMany(entry => Types(entry.Assembly).Select(t => (entry.Mod, t)))
        .Where(entry => HarmonyMethodExtensions.GetFromType(entry.t) is { Count: > 0 })
        .ToList();
      Manifests = Assemblies
        .SelectMany(entry => Types(entry.Assembly).Select(t => (entry.Mod, t)))
        .SelectMany(entry => entry.t
          .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
          .Where(IsManifest)
          .Select(m => (entry.Mod, m)))
        .ToList();
    }

    internal GameEngineHost Host { get; }
    internal IReadOnlyList<(string Mod, Assembly Assembly)> Assemblies { get; }
    internal IReadOnlyList<(string Mod, Type Type)> PatchClasses { get; }
    internal IReadOnlyList<(string Mod, MethodInfo Method)> Manifests { get; }

    private IEnumerable<Type> Types(Assembly assembly) {
      try {
        return assembly.GetTypes();
      } catch (ReflectionTypeLoadException ex) {
        var details = string.Join("\n  ", ex.LoaderExceptions.Where(e => e != null)
          .Select(e => e.Message).Distinct().Take(10));
        throw new InvalidOperationException(
          $"{assembly.GetName().Name} has types that fail to load against {Host.Tree.Label}:\n  {details}");
      }
    }
  }

  // Name-based so a future mod that does not reference StrongMods can carry its own copy of the attribute.
  internal static bool IsManifest(MethodInfo method) {
    return method.GetCustomAttributes(false).Any(a => a.GetType().Name == "PatchTargetManifestAttribute");
  }
}

public class SmokeTests {
  public static IEnumerable<object[]> PatchClassCases() {
    return SmokeTestCtx.Labels.Value.SelectMany(label => SmokeTestCtx.For(label).PatchClasses
      .Select(pc => new object[] { label, pc.Mod, pc.Type.FullName }));
  }

  [Fact]
  public void All_code_mods_are_built() {
    Assert.True(SmokeTestCtx.Inventory.Value.MissingDlls.Count == 0,
      "Missing mod DLLs:\n" + string.Join("\n", SmokeTestCtx.Inventory.Value.MissingDlls));
  }

  [Fact]
  public void Every_mod_is_tested_against_at_least_one_tree() {
    // The declared-version filter must never silently shrink coverage to zero for a mod: dev ∈ test (the
    // closure test's rule) makes each mod's dev tree the floor. Failing here means a declaration or the
    // label axis broke, not that the mod went untested by choice.
    var untested = SmokeTestCtx.Inventory.Value.Mods
      .Where(m => !SmokeTestCtx.Labels.Value.Any(label => SmokeTestCtx.ModsFor(label).Any(x => x.Name == m.Name)))
      .Select(m => $"{m.Name} (declares [{string.Join(", ", m.TestVersions)}])").ToList();
    Assert.True(untested.Count == 0,
      "Mods excluded from every tree of the version axis:\n" + string.Join("\n", untested));
  }

  [Theory]
  [MemberData(nameof(PatchClassCases))]
  public void Patch_targets_resolve(string label, string mod, string typeName) {
    SmokeTestCtx.LoadedLabel loaded = SmokeTestCtx.For(label);
    (_, Type type) = loaded.PatchClasses.First(pc => pc.Mod == mod && pc.Type.FullName == typeName);
    TargetResolver.Result result = TargetResolver.CheckType(type, loaded.Host.Tree);
    Assert.True(result.Failures.Count == 0, string.Join("\n\n", result.Failures));
  }

  [Fact]
  public void Manifest_targets_resolve() {
    // Zero manifests is the expected state while no mod patches programmatically (#44 removed the last
    // site); this is a Fact rather than a Theory because xunit fails a Theory whose MemberData is empty.
    // Any manifest that does exist must enumerate fully against every tree its mod declares (manifests are
    // lazy yield methods: throws surface at enumeration, not invocation) and yield no nulls.
    foreach (var label in SmokeTestCtx.Labels.Value) {
      SmokeTestCtx.LoadedLabel loaded = SmokeTestCtx.For(label);
      foreach ((_, MethodInfo method) in loaded.Manifests) {
        var manifestName = $"{method.DeclaringType.FullName}.{method.Name}";
        try {
          var targets = ((IEnumerable)method.Invoke(null, null)).Cast<MethodBase>().ToList();
          Assert.True(targets.All(t => t != null),
            $"{manifestName} yielded a null target against {loaded.Host.Tree.Label} — manifests must throw " +
            "descriptively instead (see PatchTargetManifestAttribute)");
        } catch (Exception ex) when (ex is not XunitException) {
          Exception inner = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
          Assert.Fail($"{manifestName} failed against {loaded.Host.Tree.Label} ({loaded.Host.Tree.Root}):\n" +
                      $"  {inner.GetType().Name}: {inner.Message}");
        }
      }
    }
  }

  [Fact]
  public void Coverage_sanity() {
    // Guards against a refactor making the suite vacuously green: the repo demonstrably contains patches,
    // so finding none means the discovery broke, not that the mods went patchless. Checked per label so a
    // broken tree of the axis cannot hide behind a healthy sibling.
    foreach (var label in SmokeTestCtx.Labels.Value) {
      SmokeTestCtx.LoadedLabel loaded = SmokeTestCtx.For(label);
      Assert.True(loaded.PatchClasses.Count > 0,
        $"No [HarmonyPatch] classes found in any mod DLL loaded against {loaded.Host.Tree.Label}");
      var totalSpecs = loaded.PatchClasses.Sum(pc => TargetResolver.CheckType(pc.Type, loaded.Host.Tree).SpecCount);
      Assert.True(totalSpecs > 0,
        $"Patch classes found but zero resolvable specs derived from them against {loaded.Host.Tree.Label}");
    }
  }

  [Fact]
  public void Negative_control_bogus_target_fails_with_diagnostics() {
    // The suite carries its own proof that it can fail: a target that cannot exist must produce a failure
    // message carrying the version identity and near-miss info (plan D8 test 4 / D10). One tree suffices —
    // the resolver is label-independent; the theories above cover the axis.
    SmokeTestCtx.LoadedLabel loaded = SmokeTestCtx.For(SmokeTestCtx.Labels.Value[0]);
    TargetResolver.Result result = TargetResolver.CheckType(typeof(BogusPatch), loaded.Host.Tree);
    Assert.Equal(1, result.SpecCount);
    var failure = Assert.Single(result.Failures);
    Assert.Contains("ThisMemberDoesNotExist_NegativeControl", failure);
    Assert.Contains(loaded.Host.Tree.Label, failure);
  }

  [Fact]
  public void Programmatic_patchers_publish_manifests() {
    // A project calling harmony.Patch(...) directly is invisible to attribute enumeration; it must publish
    // its targets. This failure message is deliberately the documentation (plan D8 test 5).
    // Line-based so comment lines don't count: PatchTargetManifestAttribute's own doc-comment example
    // contains `harmony.Patch(...)` and must not flag StrongMods. Manifest presence is a property of the
    // assembly, not of a tree — any tree the mod declares serves to read it.
    var patchCall = new Regex(@"\.Patch\s*\(");
    var offenders = new List<string>();
    foreach (ModInventory.CodeMod mod in SmokeTestCtx.Inventory.Value.Mods) {
      var callSites = ModInventory.SourceFiles(mod)
        .Where(f => File.ReadLines(f).Any(line => !line.TrimStart().StartsWith("//") && patchCall.IsMatch(line)))
        .Select(f => Path.GetRelativePath(mod.Dir, f)).ToList();
      var label = SmokeTestCtx.Labels.Value.First(l => SmokeTestCtx.ModsFor(l).Any(x => x.Name == mod.Name));
      if (callSites.Count > 0 && SmokeTestCtx.For(label).Manifests.All(m => m.Mod != mod.Name)) {
        offenders.Add($"{mod.Name} calls harmony.Patch(...) directly ({string.Join(", ", callSites)}) but " +
                      "publishes no [PatchTargetManifest]. Prefer converting to [HarmonyPatch] classes in a " +
                      "config-gated [HarmonyPatchCategory] (see #44); if the patch must stay programmatic, " +
                      "tag a public static IEnumerable<MethodBase> method that yields every patch target " +
                      "(throwing on any it cannot resolve) and enumerate it from your patching code — " +
                      "see StrongMods/PatchTargetManifestAttribute.cs for the contract and example.");
      }
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }

  [HarmonyPatch(typeof(string), "ThisMemberDoesNotExist_NegativeControl")]
  private static class BogusPatch {
    private static void Prefix() {
    }
  }
}

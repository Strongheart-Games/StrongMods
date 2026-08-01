using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>Shared, lazily-built state: the unit under test and every mod assembly loaded into it.</summary>
internal static class Ctx {
  internal static readonly Lazy<ModInventory> Inventory = new(ModInventory.Scan);

  internal static readonly Lazy<GameTree> Tree = new(() =>
    GameTree.Load(Inventory.Value.Mods.Select(m => Path.GetDirectoryName(m.DllPath)).Distinct().ToList()));

  internal static readonly Lazy<IReadOnlyList<(string Mod, Assembly Assembly)>> Assemblies = new(() =>
    Inventory.Value.Mods.Select(m => (m.Name, Tree.Value.LoadModAssembly(m.DllPath))).ToList());

  internal static readonly Lazy<IReadOnlyList<(string Mod, Type Type)>> PatchClasses = new(() =>
    Assemblies.Value
      .SelectMany(entry => Types(entry.Assembly).Select(t => (entry.Mod, t)))
      .Where(entry => HarmonyMethodExtensions.GetFromType(entry.t) is { Count: > 0 })
      .ToList());

  internal static readonly Lazy<IReadOnlyList<(string Mod, MethodInfo Method)>> Manifests = new(() =>
    Assemblies.Value
      .SelectMany(entry => Types(entry.Assembly).Select(t => (entry.Mod, t)))
      .SelectMany(entry => entry.t
        .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(IsManifest)
        .Select(m => (entry.Mod, m)))
      .ToList());

  // Name-based so a future mod that does not reference StrongMods can carry its own copy of the attribute.
  internal static bool IsManifest(MethodInfo method) {
    return method.GetCustomAttributes(false).Any(a => a.GetType().Name == "PatchTargetManifestAttribute");
  }

  private static IEnumerable<Type> Types(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException ex) {
      var details = string.Join("\n  ", ex.LoaderExceptions.Where(e => e != null)
        .Select(e => e.Message).Distinct().Take(10));
      throw new InvalidOperationException(
        $"{assembly.GetName().Name} has types that fail to load against {Tree.Value.Label}:\n  {details}");
    }
  }
}

public class SmokeTests {
  public static IEnumerable<object[]> PatchClassCases() {
    return Ctx.PatchClasses.Value.Select(pc => new object[] { pc.Mod, pc.Type.FullName });
  }

  [Fact]
  public void All_code_mods_are_built() {
    Assert.True(Ctx.Inventory.Value.MissingDlls.Count == 0,
      "Missing mod DLLs:\n" + string.Join("\n", Ctx.Inventory.Value.MissingDlls));
  }

  [Theory]
  [MemberData(nameof(PatchClassCases))]
  public void Patch_targets_resolve(string mod, string typeName) {
    (_, Type type) = Ctx.PatchClasses.Value.First(pc => pc.Mod == mod && pc.Type.FullName == typeName);
    TargetResolver.Result result = TargetResolver.CheckType(type, Ctx.Tree.Value);
    Assert.True(result.Failures.Count == 0, string.Join("\n\n", result.Failures));
  }

  [Fact]
  public void Manifest_targets_resolve() {
    // Zero manifests is the expected state while no mod patches programmatically (#44 removed the last
    // site); this is a Fact rather than a Theory because xunit fails a Theory whose MemberData is empty.
    // Any manifest that does exist must enumerate fully (manifests are lazy yield methods: throws surface
    // at enumeration, not invocation) and yield no nulls.
    foreach ((_, MethodInfo method) in Ctx.Manifests.Value) {
      var manifestName = $"{method.DeclaringType.FullName}.{method.Name}";
      try {
        var targets = ((IEnumerable)method.Invoke(null, null)).Cast<MethodBase>().ToList();
        Assert.True(targets.All(t => t != null),
          $"{manifestName} yielded a null target against {Ctx.Tree.Value.Label} — manifests must throw " +
          "descriptively instead (see PatchTargetManifestAttribute)");
      } catch (Exception ex) when (ex is not XunitException) {
        Exception inner = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
        Assert.Fail($"{manifestName} failed against {Ctx.Tree.Value.Label} ({Ctx.Tree.Value.Root}):\n" +
                    $"  {inner.GetType().Name}: {inner.Message}");
      }
    }
  }

  [Fact]
  public void Coverage_sanity() {
    // Guards against a refactor making the suite vacuously green: the repo demonstrably contains patches,
    // so finding none means the discovery broke, not that the mods went patchless. (No manifest-count
    // assertion: zero manifests is the correct state since #44 — the conformance test below guards the
    // pattern instead.)
    Assert.True(Ctx.PatchClasses.Value.Count > 0, "No [HarmonyPatch] classes found in any mod DLL");
    var totalSpecs = Ctx.PatchClasses.Value.Sum(pc => TargetResolver.CheckType(pc.Type, Ctx.Tree.Value).SpecCount);
    Assert.True(totalSpecs > 0, "Patch classes found but zero resolvable specs derived from them");
  }

  [Fact]
  public void Negative_control_bogus_target_fails_with_diagnostics() {
    // The suite carries its own proof that it can fail: a target that cannot exist must produce a failure
    // message carrying the version identity and near-miss info (plan D8 test 4 / D10).
    TargetResolver.Result result = TargetResolver.CheckType(typeof(BogusPatch), Ctx.Tree.Value);
    Assert.Equal(1, result.SpecCount);
    var failure = Assert.Single(result.Failures);
    Assert.Contains("ThisMemberDoesNotExist_NegativeControl", failure);
    Assert.Contains(Ctx.Tree.Value.Label, failure);
  }

  [Fact]
  public void Programmatic_patchers_publish_manifests() {
    // A project calling harmony.Patch(...) directly is invisible to attribute enumeration; it must publish
    // its targets. This failure message is deliberately the documentation (plan D8 test 5).
    // Line-based so comment lines don't count: PatchTargetManifestAttribute's own doc-comment example
    // contains `harmony.Patch(...)` and must not flag StrongMods.
    var patchCall = new Regex(@"\.Patch\s*\(");
    var offenders = new List<string>();
    foreach (ModInventory.CodeMod mod in Ctx.Inventory.Value.Mods) {
      var callSites = ModInventory.SourceFiles(mod)
        .Where(f => File.ReadLines(f).Any(line => !line.TrimStart().StartsWith("//") && patchCall.IsMatch(line)))
        .Select(f => Path.GetRelativePath(mod.Dir, f)).ToList();
      if (callSites.Count > 0 && Ctx.Manifests.Value.All(m => m.Mod != mod.Name)) {
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

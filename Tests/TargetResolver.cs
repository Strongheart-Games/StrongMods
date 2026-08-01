using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace Tests;

/// <summary>
///   Resolves every [HarmonyPatch] target a patch class declares, using Harmony's own attribute reading and
///   merging (plan D7), and produces version-stamped, near-miss-bearing failure messages (plan D10) when a
///   target does not exist in the unit under test.
/// </summary>
public static class TargetResolver {
  public static Result CheckType(Type patchType, GameTree tree) {
    var result = new Result();
    List<HarmonyMethod> classAttrs = HarmonyMethodExtensions.GetFromType(patchType);
    if (classAttrs == null || classAttrs.Count == 0) {
      return result; // not a patch class
    }

    var classMerged = HarmonyMethod.Merge(classAttrs);
    var checkedAny = false;

    // Method-level [HarmonyPatch] attributes merge on top of the class-level ones — Harmony's semantics.
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                  BindingFlags.DeclaredOnly;
    foreach (MethodInfo method in patchType.GetMethods(Declared)) {
      List<HarmonyMethod> own = HarmonyMethodExtensions.GetFromMethod(method);
      if (own == null || !own.Any(HasTargetInfo)) {
        continue;
      }

      var merged = HarmonyMethod.Merge(classAttrs.Concat(own).ToList());
      CheckSpec(merged, $"{patchType.FullName}.{method.Name}", tree, result);
      checkedAny = true;
    }

    MethodInfo provider = FindTargetMethodProvider(patchType);
    if (provider != null) {
      CheckTargetMethodProvider(provider, patchType, tree, result);
      checkedAny = true;
    } else if (IsComplete(classMerged)) {
      CheckSpec(classMerged, patchType.FullName, tree, result);
      checkedAny = true;
    }

    if (!checkedAny) {
      result.SpecCount++;
      result.Failures.Add($"{Header(tree)}\n  patch  : {patchType.FullName}\n" +
                          "  problem: class declares [HarmonyPatch] but no complete target (no method name, " +
                          "no TargetMethod provider, no method-level targeting)");
    }

    return result;
  }

  // A spec resolvable on its own: Constructor/StaticConstructor need no name; everything else does.
  private static bool IsComplete(HarmonyMethod info) {
    if (info.declaringType == null) {
      return false;
    }

    MethodType methodType = info.methodType ?? MethodType.Normal;
    return info.methodName != null ||
           methodType == MethodType.Constructor || methodType == MethodType.StaticConstructor;
  }

  private static bool HasTargetInfo(HarmonyMethod info) {
    return info.declaringType != null || info.methodName != null || info.methodType != null ||
           info.argumentTypes != null;
  }

  private static void CheckSpec(HarmonyMethod info, string origin, GameTree tree, Result result) {
    result.SpecCount++;
    Type declaring = info.declaringType;
    var name = info.methodName;
    MethodType methodType = info.methodType ?? MethodType.Normal;
    Type[] args = info.argumentTypes;

    MethodBase found;
    switch (methodType) {
      case MethodType.Normal:
        found = AccessTools.DeclaredMethod(declaring, name, args) ?? AccessTools.Method(declaring, name, args);
        break;
      case MethodType.Getter:
        found = AccessTools.DeclaredPropertyGetter(declaring, name) ?? AccessTools.PropertyGetter(declaring, name);
        break;
      case MethodType.Setter:
        found = AccessTools.DeclaredPropertySetter(declaring, name) ?? AccessTools.PropertySetter(declaring, name);
        break;
      case MethodType.Constructor:
      case MethodType.StaticConstructor:
        found = AccessTools.DeclaredConstructor(declaring, args, methodType == MethodType.StaticConstructor);
        break;
      case MethodType.Enumerator:
        MethodInfo original = AccessTools.DeclaredMethod(declaring, name, args) ??
                              AccessTools.Method(declaring, name, args);
        found = original == null ? null : AccessTools.EnumeratorMoveNext(original);
        break;
      default:
        // A patch shape the resolver does not understand is a failure, not a skip (plan D7).
        result.Failures.Add($"{Header(tree)}\n  patch  : {origin}\n" +
                            $"  problem: MethodType.{methodType} is not handled by the smoke-test resolver — " +
                            "extend TargetResolver.CheckSpec");
        return;
    }

    if (found == null) {
      result.Failures.Add(Describe(info, origin, tree));
    }
  }

  private static MethodInfo FindTargetMethodProvider(Type patchType) {
    return patchType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
      .FirstOrDefault(m => m.Name == "TargetMethod" || m.Name == "TargetMethods" ||
                           m.GetCustomAttributes(true).Any(a =>
                             a is HarmonyTargetMethod || a is HarmonyTargetMethods));
  }

  private static void CheckTargetMethodProvider(MethodInfo provider, Type patchType, GameTree tree,
    Result result) {
    result.SpecCount++;
    var origin = $"{patchType.FullName}.{provider.Name}()";
    try {
      var returned = provider.Invoke(null, null);
      List<MethodBase> targets = returned is MethodBase single
        ? new List<MethodBase> { single }
        : ((IEnumerable)returned)?.Cast<MethodBase>().ToList();
      if (targets == null || targets.Count == 0 || targets.Any(t => t == null)) {
        result.Failures.Add($"{Header(tree)}\n  patch  : {origin}\n" +
                            "  problem: returned null, empty, or a null target");
      }
    } catch (Exception ex) {
      Exception inner = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
      result.Failures.Add($"{Header(tree)}\n  patch  : {origin}\n  problem: threw {inner.GetType().Name}: " +
                          inner.Message);
    }
  }

  private static string Header(GameTree tree) {
    return $"Patch target check failed against {tree.Label} ({tree.Root})";
  }

  private static string Describe(HarmonyMethod info, string origin, GameTree tree) {
    var message = new StringBuilder();
    message.AppendLine(Header(tree));
    message.AppendLine($"  patch  : {origin}");
    Type declaring = info.declaringType;
    var name = info.methodName;
    MethodType methodType = info.methodType ?? MethodType.Normal;
    var argList = info.argumentTypes == null
      ? "any signature"
      : "(" + string.Join(", ", info.argumentTypes.Select(t => t.Name)) + ")";

    if (declaring == null) {
      message.AppendLine($"  sought : ?.{name} [{methodType}] {argList}");
      message.Append("  problem: the patch declares no target type — a string-named type may have failed " +
                     "AccessTools.TypeByName, or the type no longer exists in this game version");
      return message.ToString();
    }

    message.AppendLine($"  sought : {declaring.FullName}.{name} [{methodType}] {argList}");

    // Near-miss diagnosis (plan D10): show what actually exists so a signature change or rename is
    // self-evident from the message alone.
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                             BindingFlags.Static | BindingFlags.FlattenHierarchy;
    var sameName = declaring.GetMethods(All).Cast<MethodBase>()
      .Concat(declaring.GetConstructors(All))
      .Concat(declaring.GetProperties(All).SelectMany(p => new[] { p.GetMethod, p.SetMethod }))
      .Where(m => m != null && (m.Name == name || m.Name == "get_" + name || m.Name == "set_" + name))
      .ToList();

    if (sameName.Count > 0) {
      message.AppendLine($"  found  : '{name}' exists on {declaring.Name} with different signature/kind:");
      foreach (MethodBase candidate in sameName.Take(10)) {
        var parameters = string.Join(", ", candidate.GetParameters().Select(p => p.ParameterType.Name));
        message.AppendLine($"           {candidate.Name}({parameters})");
      }
    } else {
      var similar = declaring.GetMethods(All)
        .Select(m => m.Name).Distinct()
        .Where(n => name != null && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
        .Take(10).ToList();
      message.AppendLine(similar.Count > 0
        ? $"  found  : no member '{name}' on {declaring.Name}; similar names: {string.Join(", ", similar)}"
        : $"  found  : no member '{name}' on {declaring.Name} at all");
    }

    message.Append("  meaning: the target changed or vanished in this game version — the patch (or the " +
                   "mod's supported-version claim) needs updating");
    return message.ToString();
  }

  public sealed class Result {
    public List<string> Failures = new();
    public int SpecCount;
  }
}

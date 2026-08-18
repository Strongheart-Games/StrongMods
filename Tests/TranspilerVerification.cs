using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Tests.Fixtures;

namespace Tests;

/// <summary>One transpiler run against its target's real IL, and what came of it.</summary>
public sealed record TranspilerRun(string Mod, string Patch, string Target, bool Changed, string Failure) {
  public string Describe() => $"{Mod}: {Patch} -> {Target}" + (Failure == null ? "" : $"\n    {Failure}");
}

/// <summary>
///   Runs each mod's transpilers against the unit's real IL (#50 gap S1). <see cref="TargetResolver" />
///   proves the target METHOD still exists; this proves the INSTRUCTION SEQUENCE inside it that the
///   transpiler keys on still exists — a distinction that matters because a game update can leave a method's
///   signature untouched while moving the call the transpiler is looking for.
///   <para>
///     The check is to execute the transpiler, not to model it. Every transpiler in this repo builds a
///     <c>CodeMatcher</c> and calls <c>ThrowIfInvalid</c>, so a lost match point raises the mod's own
///     diagnostic message; and a transpiler that matched must have CHANGED the instruction list, which is
///     what catches a defensive one that silently returns its input.
///   </para>
///   Nothing is patched: the instructions are read, transformed in memory, and thrown away.
/// </summary>
public static class TranspilerVerification {
  /// <summary>Every transpiler in a patch class, run against every target that class declares.</summary>
  public static IEnumerable<TranspilerRun> Run(string mod, Type patchType) {
    List<HarmonyMethod> classAttributes = HarmonyMethodExtensions.GetFromType(patchType);
    if (classAttributes == null || classAttributes.Count == 0) {
      yield break;
    }

    const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                  BindingFlags.DeclaredOnly;
    foreach (MethodInfo method in patchType.GetMethods(Declared)) {
      if (!IsTranspiler(method)) {
        continue;
      }

      List<HarmonyMethod> own = HarmonyMethodExtensions.GetFromMethod(method) ?? new List<HarmonyMethod>();
      HarmonyMethod merged = HarmonyMethod.Merge(classAttributes.Concat(own).ToList());
      MethodBase target = Resolve(merged);
      var name = $"{patchType.FullName}.{method.Name}";
      if (target == null) {
        // Unresolvable here is not a finding: SmokeTests owns "does the target exist", with far better
        // diagnostics. Reporting it twice would just duplicate that failure in a worse form.
        continue;
      }

      yield return Execute(mod, name, target, method);
    }
  }

  private static bool IsTranspiler(MethodInfo method) =>
    method.Name == "Transpiler" ||
    method.GetCustomAttributes(false).Any(a => a.GetType().Name == "HarmonyTranspiler");

  private static MethodBase Resolve(HarmonyMethod info) {
    if (info.declaringType == null) {
      return null;
    }

    MethodType methodType = info.methodType ?? MethodType.Normal;
    Type[] arguments = info.argumentTypes;
    try {
      return methodType switch {
        MethodType.Constructor => AccessTools.DeclaredConstructor(info.declaringType, arguments),
        MethodType.StaticConstructor => AccessTools.GetDeclaredConstructors(info.declaringType, true)
          .FirstOrDefault(c => c.IsStatic),
        MethodType.Getter => AccessTools.DeclaredProperty(info.declaringType, info.methodName)?.GetGetMethod(true),
        MethodType.Setter => AccessTools.DeclaredProperty(info.declaringType, info.methodName)?.GetSetMethod(true),
        // An iterator's real body is the compiler-generated MoveNext; the declared method is only the stub
        // that builds the state machine, and holds none of the code a transpiler is looking for. Harmony
        // makes the same hop when it patches, so the harness must make it to see the same IL.
        MethodType.Enumerator => AccessTools.EnumeratorMoveNext(
          AccessTools.Method(info.declaringType, info.methodName, arguments)),
        _ => info.methodName == null ? null : AccessTools.Method(info.declaringType, info.methodName, arguments)
      };
    } catch (Exception) {
      return null;
    }
  }

  private static TranspilerRun Execute(string mod, string patch, MethodBase target, MethodInfo transpiler) {
    var targetName = $"{target.DeclaringType?.FullName}.{target.Name}";
    List<CodeInstruction> original;
    ILGenerator generator;
    try {
      generator = new DynamicMethod("transpiler-probe", typeof(void), Type.EmptyTypes).GetILGenerator();
      original = IlReader.Read(target, generator);
    } catch (Exception e) {
      return new TranspilerRun(mod, patch, targetName, false,
        $"could not read the target's IL: {e.GetType().Name}: {e.Message}");
    }

    var before = Describe(original);
    object[] arguments;
    try {
      arguments = Arguments(transpiler, original, generator, target);
    } catch (InvalidOperationException e) {
      return new TranspilerRun(mod, patch, targetName, false, e.Message);
    }

    try {
      var result = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, arguments);
      var after = Describe(result.ToList());
      return new TranspilerRun(mod, patch, targetName, after != before,
        after == before
          ? "the transpiler ran but changed nothing — its match point is gone, or it silently gave up"
          : null);
    } catch (TargetInvocationException e) {
      Exception inner = e.InnerException ?? e;
      return new TranspilerRun(mod, patch, targetName, false,
        $"{inner.GetType().Name}: {inner.Message}");
    } catch (Exception e) {
      return new TranspilerRun(mod, patch, targetName, false, $"{e.GetType().Name}: {e.Message}");
    }
  }

  /// <summary>
  ///   Harmony passes a transpiler whatever its parameters ask for, by type. The three shapes this repo uses
  ///   are the instruction list alone, plus an ILGenerator, plus the original MethodBase.
  /// </summary>
  private static object[] Arguments(MethodInfo transpiler, List<CodeInstruction> instructions,
                                    ILGenerator generator, MethodBase original) {
    ParameterInfo[] parameters = transpiler.GetParameters();
    var arguments = new object[parameters.Length];
    for (var i = 0; i < parameters.Length; i++) {
      Type type = parameters[i].ParameterType;
      if (typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(type)) {
        arguments[i] = instructions;
      } else if (typeof(ILGenerator).IsAssignableFrom(type)) {
        arguments[i] = generator;
      } else if (typeof(MethodBase).IsAssignableFrom(type)) {
        arguments[i] = original;
      } else {
        throw new InvalidOperationException(
          $"parameter '{parameters[i].Name}' is a {type.Name}, which this harness does not know how to " +
          "supply — teach TranspilerVerification.Arguments about it");
      }
    }

    return arguments;
  }

  /// <summary>
  ///   A stable rendering of an instruction list, for before/after comparison.
  ///   <para>
  ///     The declaring type is spelled out deliberately. <c>MethodInfo.ToString()</c> omits it — both
  ///     <c>File.Exists</c> and <c>CaseSensitiveFilesystem.Exists</c> render as
  ///     "Boolean Exists(System.String)" — so a transpiler that swaps one call for another of the same
  ///     signature would look like a no-op. Measured: that is exactly what StrongMods'
  ///     CaseSensitiveFilesystem transpilers do, and without the type they all reported "changed nothing".
  ///   </para>
  /// </summary>
  private static string Describe(List<CodeInstruction> instructions) =>
    string.Join("\n", instructions.Select(i => $"{i.opcode} {Operand(i.operand)}"));

  private static string Operand(object operand) => operand switch {
    MemberInfo member => $"{member.DeclaringType?.FullName}.{member}",
    null => "",
    _ => operand.ToString()
  };
}

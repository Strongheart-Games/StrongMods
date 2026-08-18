using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Tests.Fixtures;

/// <summary>
///   Reads a method's IL into Harmony <see cref="CodeInstruction" />s using nothing but the BCL.
///   <para>
///     Harmony's own <c>PatchProcessor.GetOriginalInstructions</c> would do this, but it routes through
///     MonoMod, whose runtime detection refuses this test runner outright — measured 2026-08-17:
///     <c>PlatformNotSupportedException: CoreCLR version 10.0.10 is not supported</c>, raised from
///     <c>MonoMod.Core.Platforms.Runtimes.CoreBaseRuntime.CreateForVersion</c>, on both the pinned
///     MonoMod.Utils 25.0.6 and the newest published 25.0.14. Since the tests target modern .NET on purpose
///     (#14), reading the IL directly is the way through, and it removes a dependency rather than adding one.
///   </para>
///   <para>
///     Fidelity is deliberately scoped to what a transpiler MATCHES on: opcodes, and operands resolved to the
///     same reflection objects a <c>CodeMatch</c> compares against — <see cref="MethodBase" /> for calls,
///     <see cref="FieldInfo" /> for field access, <see cref="Type" />, strings and numeric constants. Branch
///     targets become real <see cref="Label" />s attached to the instruction they point at, so a transpiler
///     that reads or moves labels sees a coherent list. Exception-handler blocks are NOT reconstructed: no
///     transpiler here inspects them, and the game's IL is only ever read, never re-emitted.
///   </para>
/// </summary>
public static class IlReader {
  private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.FieldType == typeof(OpCode))
    .Select(f => (OpCode)f.GetValue(null))
    .ToDictionary(o => o.Value);

  public static List<CodeInstruction> Read(MethodBase method, ILGenerator generator) {
    MethodBody body = method.GetMethodBody() ??
                      throw new InvalidOperationException($"{method.Name} has no IL body (extern or abstract)");
    var il = body.GetILAsByteArray() ??
             throw new InvalidOperationException($"{method.Name} exposes no IL bytes");

    Type[] typeArguments = method.DeclaringType?.IsGenericType == true
      ? method.DeclaringType.GetGenericArguments()
      : Type.EmptyTypes;
    Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
    Module module = method.Module;

    var instructions = new List<CodeInstruction>();
    var offsets = new List<int>();
    var branchTargets = new List<(int Index, int TargetOffset)>();
    var switchTargets = new List<(int Index, int[] TargetOffsets)>();
    var position = 0;

    while (position < il.Length) {
      var offset = position;
      OpCode opcode = ReadOpCode(il, ref position);
      object operand = ReadOperand(opcode, il, ref position, module, body, typeArguments, methodArguments,
        out var branchTarget, out var switchTarget);

      offsets.Add(offset);
      if (branchTarget.HasValue) {
        branchTargets.Add((instructions.Count, branchTarget.Value));
      }

      if (switchTarget != null) {
        switchTargets.Add((instructions.Count, switchTarget));
      }

      instructions.Add(new CodeInstruction(opcode, operand));
    }

    AttachLabels(instructions, offsets, branchTargets, switchTargets, generator);
    return instructions;
  }

  private static OpCode ReadOpCode(byte[] il, ref int position) {
    var first = il[position++];
    if (first != 0xFE) {
      return Opcodes[first];
    }

    var second = il[position++];
    return Opcodes[(short)(0xFE00 | second)];
  }

  private static object ReadOperand(OpCode opcode, byte[] il, ref int position, Module module, MethodBody body,
                                    Type[] typeArguments, Type[] methodArguments, out int? branchTarget,
                                    out int[] switchTargets) {
    branchTarget = null;
    switchTargets = null;
    switch (opcode.OperandType) {
      case OperandType.InlineNone:
        return null;

      case OperandType.ShortInlineBrTarget: {
        var delta = (sbyte)il[position++];
        branchTarget = position + delta;
        return null;
      }

      case OperandType.InlineBrTarget: {
        var delta = BitConverter.ToInt32(il, position);
        position += 4;
        branchTarget = position + delta;
        return null;
      }

      case OperandType.InlineSwitch: {
        var count = BitConverter.ToInt32(il, position);
        position += 4;
        var deltas = new int[count];
        for (var i = 0; i < count; i++) {
          deltas[i] = BitConverter.ToInt32(il, position);
          position += 4;
        }

        var after = position;
        switchTargets = deltas.Select(d => after + d).ToArray();
        return null;
      }

      case OperandType.ShortInlineI:
        // Ldc_I4_S is the only signed one; the rest (Unaligned) are unsigned byte prefixes.
        return opcode == OpCodes.Ldc_I4_S ? (sbyte)il[position++] : il[position++];

      case OperandType.InlineI: {
        var value = BitConverter.ToInt32(il, position);
        position += 4;
        return value;
      }

      case OperandType.InlineI8: {
        var value = BitConverter.ToInt64(il, position);
        position += 8;
        return value;
      }

      case OperandType.ShortInlineR: {
        var value = BitConverter.ToSingle(il, position);
        position += 4;
        return value;
      }

      case OperandType.InlineR: {
        var value = BitConverter.ToDouble(il, position);
        position += 8;
        return value;
      }

      case OperandType.ShortInlineVar:
        return Variable(opcode, il[position++], body);

      case OperandType.InlineVar: {
        var index = BitConverter.ToUInt16(il, position);
        position += 2;
        return Variable(opcode, index, body);
      }

      case OperandType.InlineString:
        return module.ResolveString(Token(il, ref position));

      case OperandType.InlineField: {
        var token = Token(il, ref position);
        return Resolve(token, () => module.ResolveField(token, typeArguments, methodArguments));
      }

      case OperandType.InlineMethod: {
        var token = Token(il, ref position);
        return Resolve(token, () => module.ResolveMethod(token, typeArguments, methodArguments));
      }

      case OperandType.InlineType: {
        var token = Token(il, ref position);
        return Resolve(token, () => module.ResolveType(token, typeArguments, methodArguments));
      }

      case OperandType.InlineTok: {
        var token = Token(il, ref position);
        return Resolve(token, () => module.ResolveMember(token, typeArguments, methodArguments));
      }

      case OperandType.InlineSig: {
        var token = Token(il, ref position);
        return Resolve(token, () => module.ResolveSignature(token));
      }

      default:
        throw new NotSupportedException($"{opcode} has operand type {opcode.OperandType}");
    }
  }

  /// <summary>
  ///   A metadata token that will not resolve — a reference this load context cannot reach — must not stop
  ///   the read: the instruction is still worth having with its raw token as the operand, since a transpiler
  ///   matching on some OTHER instruction does not care.
  /// </summary>
  private static object Resolve(int token, Func<object> resolve) {
    try {
      return resolve();
    } catch (Exception) {
      return token;
    }
  }

  private static int Token(byte[] il, ref int position) {
    var token = BitConverter.ToInt32(il, position);
    position += 4;
    return token;
  }

  /// <summary>
  ///   Argument opcodes carry a plain index; local opcodes carry the local itself, which is what a
  ///   transpiler inspecting <c>LocalVariableInfo.LocalType</c> expects.
  /// </summary>
  private static object Variable(OpCode opcode, int index, MethodBody body) {
    var name = opcode.Name ?? "";
    if (name.StartsWith("ldarg") || name.StartsWith("starg")) {
      return index;
    }

    return index < body.LocalVariables.Count ? body.LocalVariables[index] : (object)index;
  }

  /// <summary>
  ///   Turns byte offsets into Harmony's label model: each branch target gets a <see cref="Label" /> from the
  ///   generator, attached to the instruction at that offset, and the branching instruction takes it as its
  ///   operand (a switch takes the array of them).
  /// </summary>
  private static void AttachLabels(List<CodeInstruction> instructions, List<int> offsets,
                                   List<(int Index, int TargetOffset)> branches,
                                   List<(int Index, int[] TargetOffsets)> switches, ILGenerator generator) {
    var labelByOffset = new Dictionary<int, Label>();

    Label LabelAt(int offset) {
      if (labelByOffset.TryGetValue(offset, out Label existing)) {
        return existing;
      }

      Label label = generator.DefineLabel();
      labelByOffset[offset] = label;
      var index = offsets.IndexOf(offset);
      if (index >= 0) {
        instructions[index].labels.Add(label);
      }

      return label;
    }

    foreach ((var index, var target) in branches) {
      instructions[index].operand = LabelAt(target);
    }

    foreach ((var index, var targets) in switches) {
      instructions[index].operand = targets.Select(LabelAt).ToArray();
    }
  }
}

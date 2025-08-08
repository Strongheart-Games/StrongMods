using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace AuthZ.Patches {
  [HarmonyPatch(typeof(World), nameof(World.CanPlaceLandProtectionBlockAt))]
  public class WorldCanPlaceLandProtectionBlockAtPatch {
    public const int TraderDeadZone = 100;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher.MatchStartForward(
          CodeMatch.StoresLocal("claimSize")
        )
        .ThrowIfInvalid("[AuthZ] Could not find claimSize declaration");
      var claimSize = codeMatcher.Instruction.operand as int? ?? 0;

      codeMatcher.MatchStartForward(
          // int num3 = deadZone / 2;
          // NB: deadZone is inclusive of claimSize
          CodeMatch.LoadsLocal(name: "deadZone"),
          CodeMatch.LoadsConstant(2),
          new CodeMatch(OpCodes.Div),
          CodeMatch.StoresLocal()
        )
        .ThrowIfInvalid("[AuthZ] Could not find deadZone calculation")
        .RemoveInstructions(3)
        .Insert(
          // claimSize + TraderDeadZone
          CodeInstruction.LoadLocal(claimSize), // claimSize
          new CodeInstruction(OpCodes.Ldc_I4_S, TraderDeadZone),
          new CodeInstruction(OpCodes.Add)
        );
      return codeMatcher.Instructions();
    }
  }
}

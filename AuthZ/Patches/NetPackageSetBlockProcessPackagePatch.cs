using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace AuthZ.Patches {
  [HarmonyPatch(typeof(NetPackageSetBlock), nameof(NetPackageSetBlock.ProcessPackage))]
  public class NetPackageSetBlockProcessPackagePatch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
      ILGenerator generator) {
      CodeMatcher codeMatcher = new(instructions, generator);
      /*codeMatcher
        .MatchStartForward(
          new CodeMatch(OpCodes.Ret)
        )
        .ThrowIfInvalid("[AuthZ] Could not find insertion point")
        .Advance(1);
      var originalInstruction = codeMatcher.Instruction;
      var labels = originalInstruction.labels;
      originalInstruction.labels = new List<Label>();
      codeMatcher
        .RemoveInstruction()
        .Insert(
        CodeInstruction.LoadArgument(0).WithLabels(labels), // this
        CodeInstruction.Call(() => LandClaims.AuthorizeBlockChanges(default)),
        originalInstruction
      );
      Log.Out($"[AuthZ] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");*/
      return codeMatcher.Instructions();
    }
  }
}

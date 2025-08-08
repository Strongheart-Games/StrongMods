using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace AutoCollectLoot.Patches {
  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.dropItemOnDeath))]
  public class EntityAliveDropItemOnDeathPatch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher.MatchStartForward(
          CodeMatch.LoadsField(typeof(Entity).Field(nameof(Entity.rand))),
          CodeMatch.Calls(typeof(GameRandom).Method(nameof(GameRandom.RandomFloat))),
          CodeMatch.Branches()
        )
        .ThrowIfInvalid("[AutoCollectLoot] Could not find loot roll")
        .Advance(2);
      var retLabel = codeMatcher.Instruction.operand;
      codeMatcher.Advance(1).Insert(
        CodeInstruction.LoadArgument(0), // this
        CodeInstruction.Call(() => AutoCollectLoot.TryCollect(null)),
        new CodeInstruction(OpCodes.Brtrue, retLabel)
      );
      //Log.Out($"[AutoCollectLoot] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }
}

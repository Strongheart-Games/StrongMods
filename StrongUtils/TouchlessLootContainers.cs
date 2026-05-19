using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace StrongUtils {
  public class TouchlessLootContainers {
    [HarmonyPatch(typeof(TileEntityLootContainer), nameof(TileEntityLootContainer.UpdateTick))]
    public class TileEntityLootContainer_UpdateTick_Patch {
      private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        CodeMatcher codeMatcher = new(instructions);
        codeMatcher.MatchEndForward(
            CodeMatch.Calls(() => GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays)),
            CodeMatch.Branches()
          )
          .ThrowIfInvalid("[StrongUtils] Could not find GamePrefs.GetInt() call")
          .Advance(1)
          .Insert(new CodeInstruction(OpCodes.Ret));
        //Log.Out($"[StrongUtils] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
        return codeMatcher.Instructions();
      }
    }
  }
}

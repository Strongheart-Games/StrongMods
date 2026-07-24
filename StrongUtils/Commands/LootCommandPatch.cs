using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace StrongUtils.Commands {
  // Fix for NRE using "loot container cars" console command
  // https://community.thefunpimps.com/threads/nre-using-loot-container-cars-console-command.48387/
  [HarmonyPatch(typeof(ConsoleCmdLoot), nameof(ConsoleCmdLoot.ContainerList))]
  public static class LootCommandPatch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher.MatchStartForward(
          new CodeMatch(OpCodes.Ldnull),
          new CodeMatch(OpCodes.Ldc_I4_0),
          CodeMatch.Calls(AccessTools.Method(typeof(LootContainer), nameof(LootContainer.SpawnLootItemsFromList)))
        )
        .ThrowIfInvalid("[LootCommandPatch] Could not find ldnull and SpawnLootItemsFromList() call")
        .RemoveInstruction()
        .Insert(
          new CodeInstruction(OpCodes.Newobj, AccessTools.Constructor(typeof(List<string>)))
        );
      //Log.Out($"[LootCommandPatch] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }
}

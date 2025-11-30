using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DynamicFeralSense {
  [HarmonyPatch(typeof(PlayerStealth), nameof(PlayerStealth.Tick))]
  public class PlayerStealth_Tick_Patch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
      ILGenerator generator) {
      CodeMatcher codeMatcher = new(instructions, generator);
      codeMatcher
        .MatchStartForward(
          CodeMatch.Calls(() => EAIManager.CalcSenseScale())
        )
        .ThrowIfInvalid("[DynamicFeralSense] Could not find call to CalcSenseScale()")
        .RemoveInstruction()
        .Insert(
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(PlayerStealth), nameof(PlayerStealth.player)),
          CodeInstruction.Call(() => DynamicFeralSense.CalcNoiseMultiplier(null))
        );
      //Log.Out($"[DynamicFeralSense] PlayerStealth.Tick instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }

  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetSeeDistance))]
  public class EntityAlive_GetSeeDistance_Patch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
      ILGenerator generator) {
      CodeMatcher codeMatcher = new(instructions, generator);
      codeMatcher
        .MatchStartForward(
          CodeMatch.Calls(() => EAIManager.CalcSenseScale())
        )
        .ThrowIfInvalid("[DynamicFeralSense] Could not find call to CalcSenseScale()")
        .RemoveInstruction()
        .Insert(
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.Call(() => DynamicFeralSense.CalcVisionMultiplier(null))
        );
      //Log.Out($"[DynamicFeralSense] EntityAlive.GetSeeDistance instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }

  public class HarmonyPatches {
    public class Initializer : IModApi {
      public void InitMod(Mod _modInstance) {
        Harmony harmony = new(_modInstance.Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
      }
    }
  }
}

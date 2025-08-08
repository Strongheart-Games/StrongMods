using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DynamicLandClaimCount {
  [HarmonyPatch(typeof(BlockLandClaim), nameof(BlockLandClaim.HandleDeactivatingCurrentLandClaims))]
  public class BlockLandClaimHandleDeactivatingCurrentLandClaimsPatch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
      ILGenerator generator) {
      CodeMatcher codeMatcher = new(instructions, generator);
      codeMatcher
        .MatchStartForward(
          //CodeMatch.LoadsConstant(EnumGameStats.LandClaimCount), // on linux this doesn't match; bug?
          CodeMatch.Calls(() => GameStats.GetInt(default)),
          CodeMatch.StoresLocal()
        )
        .ThrowIfInvalid("[DynamicLandClaimCount] Could not find insertion point")
        .Advance(-1) // make up for removing the LoadsConstant in the match
        .RemoveInstructions(2)
        .InsertAndAdvance(
          CodeInstruction.LoadArgument(1), // persistentPlayerData
          CodeInstruction.Call(() => DynamicLandClaimCount.GetLandClaimCount((PersistentPlayerData)null))
        );
      //Log.Out($"[DynamicLandClaimCount] HandleDeactivatingCurrentLandClaims instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }

  [HarmonyPatch(typeof(PersistentPlayerList), nameof(PersistentPlayerList.RemoveExtraLandClaims))]
  public class PersistentPlayerListRemoveExtraLandClaimsPatch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
      ILGenerator generator) {
      CodeMatcher codeMatcher = new(instructions, generator);
      codeMatcher
        .MatchStartForward(
          //CodeMatch.LoadsConstant(EnumGameStats.LandClaimCount), // on linux this doesn't match; bug?
          CodeMatch.Calls(() => GameStats.GetInt(default)),
          CodeMatch.StoresLocal()
        )
        .ThrowIfInvalid("[DynamicLandClaimCount] Could not find insertion point")
        .Advance(-1) // make up for removing the LoadsConstant in the match
        .RemoveInstructions(2)
        .InsertAndAdvance(
          CodeInstruction.LoadArgument(1), // persistentPlayerData
          CodeInstruction.Call(() => DynamicLandClaimCount.GetLandClaimCount((PersistentPlayerData)null))
        );
      //Log.Out($"[DynamicLandClaimCount] RemoveExtraLandClaims instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }

  [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
  public class WorldEnvironmentOnXMLChangedPatch {
    private static void Postfix() {
      DynamicLandClaimCount.OnXMLChanged();
    }
  }

  [HarmonyPatch(typeof(PersistentPlayerData), nameof(PersistentPlayerData.AddLandProtectionBlock))]
  public class PersistentPlayerDataAddLandProtectionBlockPatch {
    private static void Postfix(PersistentPlayerData __instance) {
      DynamicLandClaimCount.WhisperLandClaimCount(__instance);
    }
  }

  [HarmonyPatch(typeof(PersistentPlayerData), nameof(PersistentPlayerData.RemoveLandProtectionBlock))]
  public class PersistentPlayerDataRemoveLandProtectionBlockPatch {
    private static void Postfix(PersistentPlayerData __instance) {
      DynamicLandClaimCount.WhisperLandClaimCount(__instance);
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());

      ModEvents.ChatMessage.RegisterHandler(DynamicLandClaimCount.HandleChatMessage);
    }
  }
}

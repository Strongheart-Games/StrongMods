using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StrongUtils {
  [HarmonyPatch(typeof(ConsoleCmdShutdown), nameof(ConsoleCmdShutdown.Execute))]
  public class ConsoleCmdShutdown_Execute_Patch {
    private static bool Prefix(ConsoleCmdShutdown __instance, List<string> _params, CommandSenderInfo _senderInfo) {
      new ShutdownHandler(__instance, _params, _senderInfo).Execute();
      return false;
    }
  }

  [HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.QuestUnlockPOI))]
  public class QuestEventManager_QuestUnlockPOI_Patch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher
        .MatchStartForward(
          CodeMatch.LoadsLocal(name: "prefabFromWorldPos")
        )
        .ThrowIfInvalid("[StrongUtils] Could not find load instruction");
      CodeInstruction load = codeMatcher.Instruction;
      codeMatcher
        .MatchEndForward(
          CodeMatch.LoadsField(typeof(PrefabInstance).GetField(nameof(PrefabInstance.lockInstance))),
          CodeMatch.Branches()
        )
        .ThrowIfInvalid("[StrongUtils] Could not find branch instruction");
      CodeInstruction branch = codeMatcher.Instruction;
      codeMatcher
        .MatchStartBackwards(
          CodeMatch.StoresLocal("prefabFromWorldPos")
        )
        .ThrowIfInvalid("[StrongUtils] Could not find insertion point")
        .Advance(1)
        .Insert(
          load,
          branch
        );
      //Log.Out($"[StrongUtils] QuestUnlockPOI instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }

    private static void Prefix(int entityID, Vector3 prefabPos) {
      PrefabInstance prefabFromWorldPos = GameManager.Instance.GetDynamicPrefabDecorator().GetPrefabFromWorldPos((int) prefabPos.x, (int) prefabPos.z);
      if (prefabFromWorldPos is null) {
        Log.Error($"[StrongUtils] Could not find prefabFromWorldPos, skipping: entityID: {entityID} prefabPos: {prefabPos}\n{Environment.StackTrace}");
      }
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

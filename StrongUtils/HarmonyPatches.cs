using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace StrongUtils {
  [HarmonyPatch(typeof(SdtdConsole), nameof(SdtdConsole.executeCommand))]
  public class SdtdConsole_executeCommand_Patch {
    private static void Postfix(ref List<string> __result, string _command, CommandSenderInfo _senderInfo) {
      if (_senderInfo.NetworkConnection is not TelnetConnection) {
        return;
      }

      if (__result.Count == 0 || !__result[__result.Count - 1].StartsWith("*** ERROR:")) {
        __result.Add($"Done executing command '{_command}'.");
      }
    }
  }

  [HarmonyPatch(typeof(ServerStateAuthorizer), nameof(ServerStateAuthorizer.Authorize))]
  public class ServerStateAuthorizer_Authorize_Patch {
    private static void Postfix(ref (EAuthorizerSyncResult, GameUtils.KickPlayerData?) __result) {
      if (DenyAll.IsEnabled()) {
        __result = (EAuthorizerSyncResult.SyncDeny,
          new GameUtils.KickPlayerData(DenyAll.GetReason(), _customReason: DenyAll.GetMessage()));
      }
    }
  }

  /*[HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.QuestUnlockPOI))]
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
      PrefabInstance prefabFromWorldPos = GameManager.Instance.GetDynamicPrefabDecorator()
        .GetPrefabFromWorldPos((int)prefabPos.x, (int)prefabPos.z);
      if (prefabFromWorldPos is null) {
        Log.Error(
          $"[StrongUtils] Could not find prefabFromWorldPos, skipping: entityID: {entityID} prefabPos: {prefabPos}\n{Environment.StackTrace}");
      }
    }
  }*/

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

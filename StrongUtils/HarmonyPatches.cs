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

  [HarmonyPatch(typeof(RegionFileManager), nameof(RegionFileManager.UpdateChunkProtectionLevels))]
  public class RegionFileManager_UpdateChunkProtectionLevels_Patch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher.MatchStartForward(
          CodeMatch.Calls(() => ((Dictionary<HashSetLong, ChunkProtectionLevel>)null).Clear())
        )
        .ThrowIfInvalid("[StrongUtils] Could not find Clear() call")
        .Advance(1)
        .Insert(
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.chunkProtectionLevels)),
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.groupProtectionLevels)),
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.groupsByChunkKey)),
          CodeInstruction.Call(() => StrongZones.SeedChunkProtectionLevels(null, null, null))
        );
      //Log.Out($"[StrongUtils] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }
  }

  [HarmonyPatch(typeof(RegionFileManager), nameof(RegionFileManager.ResetAllChunks))]
  public class RegionFileManager_ResetAllChunks_Patch {
    private static void Postfix(ref HashSetLong __result) {
      Log.Out($"[StrongUtils] ResetAllChunks: reset {__result.Count} chunks");
    }
  }

  [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
  public class WorldEnvironment_OnXMLChanged_Patch {
    private static void Postfix() {
      StrongZones.OnXMLChanged();
      StrongCommands.OnXMLChanged();
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Postfix(PlatformUserIdentifierAbs persistentPlayerId, List<BlockChangeInfo> _blocksToChange) {
      StrongAudit.Audit_GameManager_ChangeBlocks(persistentPlayerId, _blocksToChange);
    }
  }

  [HarmonyPatch(typeof(EntityGroups), nameof(EntityGroups.Normalize))]
  public class EntityGroups_Normalize_Patch {
    private static void Prefix(string _sEntityGroupName, ref float totalp) {
      //SpawnScaler.ScaleEntityGroup(_sEntityGroupName, ref totalp);
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.ChatMessage.RegisterHandler(StrongCommands.HandleChatMessage);
    }
  }
}

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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
          CodeMatch.Calls(() => ((Dictionary<LongSetGroups.Group, ChunkProtectionLevel>)null).Clear())
        )
        .ThrowIfInvalid("[StrongUtils] Could not find Clear() call")
        .Advance(1)
        .Insert(
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.chunkProtectionLevels)),
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.groupProtectionLevels)),
          CodeInstruction.LoadArgument(0), // this
          CodeInstruction.LoadField(typeof(RegionFileManager), nameof(RegionFileManager.chunkGroups)),
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
    }
  }

  [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeBlocks))]
  public class GameManager_ChangeBlocks_Patch {
    private static void Prefix(PlatformUserIdentifierAbs persistentPlayerId, List<BlockChangeInfo> _blocksToChange) {
      NoClaimsEnforcer.RejectLandClaims(persistentPlayerId, _blocksToChange);
    }

    private static void Postfix(PlatformUserIdentifierAbs persistentPlayerId, List<BlockChangeInfo> _blocksToChange) {
      StrongAudit.Audit_GameManager_ChangeBlocks(persistentPlayerId, _blocksToChange);
    }
  }

  [HarmonyPatch(typeof(NetPackageDamageEntity), nameof(NetPackageDamageEntity.ProcessPackage))]
  public class NetPackageDamageEntity_ProcessPackage_Patch {
    private static void Prefix(NetPackageDamageEntity __instance) {
      PlayerDamage.ValidateDamageEntityPackage(__instance);
    }
  }

  [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.DamageEntity))]
  public class EntityPlayer_DamageEntity_Patch {
    private static void Prefix(EntityPlayer __instance, DamageSource _damageSource, int _strength, bool _criticalHit,
      float _impulseScale) {
      PlayerDamage.RecordDamage(__instance, _damageSource, _strength, _criticalHit, _impulseScale);
    }
  }

  [HarmonyPatch(typeof(EntityGroups), nameof(EntityGroups.Normalize))]
  public class EntityGroups_Normalize_Patch {
    private static void Prefix(string _sEntityGroupName, ref float totalp) {
      //SpawnScaler.ScaleEntityGroup(_sEntityGroupName, ref totalp);
    }
  }

  [HarmonyPatch(typeof(World), nameof(World.TickEntity))]
  public class World_TickEntity_Patch {
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
      CodeMatcher codeMatcher = new(instructions);
      codeMatcher.MatchStartForward(
          CodeMatch.LoadsArgument(),
          CodeMatch.Calls(() => ((Entity)null).OnUpdateEntity())
        )
        .ThrowIfInvalid("[StrongUtils] Could not find OnUpdateEntity() call");
      CodeInstruction loadEntityInstruction = codeMatcher.Instruction;
      codeMatcher.Insert(
        loadEntityInstruction,
        CodeInstruction.Call(() => OnEntityUpdate(null))
      );
      //Log.Out($"[StrongUtils] Instructions:\n    {string.Join("\n    ", codeMatcher.Instructions())}");
      return codeMatcher.Instructions();
    }

    private static void OnEntityUpdate(Entity entity) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      StrongZones.OnUpdateEntity(entity);
      if (entity is EntityPlayer player) {
        FastTravel.ProcessFastTravelDonations(player);
      }
    }
  }

  [HarmonyPatch(typeof(Prefab), MethodType.Constructor, typeof(Prefab), typeof(bool))]
  public class Prefab_Constructor_Patch {
    private static void Postfix(Prefab __instance, Prefab _other, bool _sharedData) {
      StrongZones.CloneStrongZoneExtensions(__instance, _other, _sharedData);
    }
  }

  [HarmonyPatch(typeof(Prefab), nameof(Prefab.ReadFromProperties))]
  public class Prefab_ReadFromProperties_Patch {
    private static void Postfix(Prefab __instance) {
      StrongZones.InitializeStrongZoneExtensions(__instance);
    }
  }

  [HarmonyPatch(typeof(Chunk), nameof(Chunk.CanMobsSpawnAtPos))]
  public class Chunk_CanMobsSpawnAtPos_Patch {
    private static bool Prefix(ref Chunk __instance, int _x, int _z, ref bool __result) {
      var worldX = __instance.GetBlockWorldPosX(_x);
      var worldZ = __instance.GetBlockWorldPosZ(_z);
      if (StrongZones.FindZonesForPosition(worldX, worldZ, __instance, "no_hostiles") is not null) {
        __result = false;
        return false;
      }
      return true;
    }
  }

  public class Initializer : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.GameAwake.RegisterHandler(ServerLifecycle.OnGameAwake);
      ModEvents.GameStartDone.RegisterHandler(ServerLifecycle.OnGameStartDone);
      ModEvents.GameShutdown.RegisterHandler(ServerLifecycle.OnGameShutdown);
      ModEvents.PlayerDisconnected.RegisterHandler(PlayerDamage.HandlePlayerDisconnected);
      ModEvents.EntityKilled.RegisterHandler(PlayerDamage.HandleEntityKilled);
    }
  }
}

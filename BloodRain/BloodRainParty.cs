using System.Collections.Generic;
using HarmonyLib;
using UniLinq;

namespace BloodRain {
  public class BloodRainParty {
    public static void WhisperSpawnGroup(AIDirectorGameStagePartySpawner spawner) {
      var message = $"Spawn group starting: {spawner.spawnGroup.groupName}";
      if (spawner.spawnGroup is not null) {
        message +=
          $"\n  interval: {spawner.interval}\n  nextStageTime: {spawner.nextStageTime}\n  numToSpawn: {spawner.numToSpawn}\n  spawnCount: {spawner.spawnCount}";
      }

      if (GameManager.IsDedicatedServer) {
        Log.Out(message);
      } else {
        Whisper(message, spawner.memberIDs.ToList());
      }
    }

    private static void Whisper(string message, List<int> recipientEntityIds) {
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        recipientEntityIds, EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported);
      package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
    }
  }

  [HarmonyPatch(typeof(AIDirectorGameStagePartySpawner), nameof(AIDirectorGameStagePartySpawner.SetupGroup))]
  public class AIDirectorGameStagePartySpawner_SetupGroup_Patch {
    private static void Postfix(AIDirectorGameStagePartySpawner __instance) {
      BloodRainParty.WhisperSpawnGroup(__instance);
    }
  }
}

using System.Collections.Generic;
using HarmonyLib;

namespace BountifulQuests {
  public class ReplaceCompletedQuests {

  }

  [HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.GetQuestList))]
  public class QuestEventManager_GetQuestList_Patch {
    private static void Postfix(ref QuestEventManager __instance, ref List<Quest> __result, World world, int npcEntityID, int playerEntityID) {
      // TODO: Implement
    }
  }
}

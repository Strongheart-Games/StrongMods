using HarmonyLib;
using WorldGenerationEngineFinal;

namespace RWGTools {
  public class StickySelection {
    public static XUiC_WorldGenerationWindowGroup PreviousWindowGroup;

    public static void ApplyStickySelections(XUiC_WorldGenerationWindowGroup windowGroup) {
      if (PreviousWindowGroup is null) {
        return;
      }
      windowGroup.BiomeLayoutComboBox.Value = PreviousWindowGroup.BiomeLayoutComboBox.Value;
    }
  }

  [HarmonyPatch(typeof(XUiC_WorldGenerationWindowGroup), nameof(XUiC_WorldGenerationWindowGroup.OnOpen))]
  public class XUiC_WorldGenerationWindowGroup_OnOpen_Patch {
    private static void Postfix(XUiC_WorldGenerationWindowGroup __instance) {
      StickySelection.ApplyStickySelections(__instance);
    }
  }

  [HarmonyPatch(typeof(XUiC_WorldGenerationWindowGroup), nameof(XUiC_WorldGenerationWindowGroup.OnClose))]
  public class XUiC_WorldGenerationWindowGroup_OnClose_Patch {
    private static void Prefix(XUiC_WorldGenerationWindowGroup __instance) {
      StickySelection.PreviousWindowGroup = __instance;
    }
  }

}

using System;
using HarmonyLib;

namespace StrongUtils {
  public class SignDiagnostics {
    [HarmonyPatch(typeof(TEFeatureCanvas), nameof(TEFeatureCanvas.SetBlockEntityData))]
    public class TEFeatureCanvas_SetBlockEntityData_Patch {
      private static Exception Finalizer(TEFeatureCanvas __instance, Exception __exception) {
        if (__exception is null) {
          return null;
        }

        Block block = __instance.blockValue.Block;
        Vector3i pos = __instance.ToWorldPos();
        PrefabInstance poi = GameManager.Instance.World.GetPOIAtPosition(pos);
        var location = poi is null ? "" : $" in {poi.prefab.LocalizedEnglishName} ({poi.prefab.PrefabName})";
        Log.Warning($"[SignDiagnostics] Could not load canvas block {block.blockName}{location}:\n{__exception}");

        return null;
      }
    }
  }
}

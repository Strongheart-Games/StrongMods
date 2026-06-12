using System.Collections.Generic;
using HarmonyLib;

namespace StrongFill {
  [HarmonyPatch(typeof(BlocksFromXml), nameof(BlocksFromXml.CreateBlock))]
  public class BlocksFromXml_CreateBlock_Patch {
    private static void Prefix(string blockName, ref DynamicProperties properties) {
      if (properties.Contains("ServerClass")) {
        properties.Values["Class"] = properties.Values["ServerClass"];
        Log.Out($"[ServerClass] Setting server class for {blockName} to {properties.Values["Class"]}");
      }
    }
  }
}

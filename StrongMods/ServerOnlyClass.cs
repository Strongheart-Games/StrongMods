using HarmonyLib;

namespace StrongMods {
  public static class ServerOnlyClass {
    public const string ServerOnlyClassCategory = "ServerOnlyClass";
    public const string ServerOnlyClassProperty = "ServerOnlyClass";
    public const string ClassProperty = "Class";

    [HarmonyPatchCategory(ServerOnlyClassCategory)]
    [HarmonyPatch(typeof(BlocksFromXml), nameof(BlocksFromXml.CreateBlock))]
    public class BlocksFromXml_CreateBlock_Patch {
      private static void Prefix(string blockName, ref DynamicProperties properties) {
        if (!properties.Contains(ServerOnlyClassProperty)) {
          return;
        }

        properties.Values[ClassProperty] = properties.Values[ServerOnlyClassProperty];
        Log.Out($"[ServerOnlyClass] Setting class for {blockName} to {properties.Values[ClassProperty]}");
      }
    }
  }

}

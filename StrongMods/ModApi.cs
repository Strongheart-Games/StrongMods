using System.Reflection;
using HarmonyLib;

namespace StrongMods {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAllUncategorized(Assembly.GetExecutingAssembly());
      InitBreadthFirstXmlPatcher(mod, harmony);
      InitXmlPatchMethodForeach(mod, harmony);
      InitCaseSensitiveFilesystem(mod, harmony);
    }

    private static void InitBreadthFirstXmlPatcher(Mod mod, Harmony harmony) {
      if (!Config.BreadthFirstXmlPatcherEnabled) {
        return;
      }

      harmony.PatchCategory("BreadthFirstXmlPatcher");
    }

    private static void InitXmlPatchMethodForeach(Mod mod, Harmony harmony) {
      if (!Config.XmlPatchMethodForeachEnabled) {
        return;
      }

      harmony.PatchCategory("XmlPatchMethodForeach");
      // Explicitly add this patch method so we control whether it's enabled or disabled
      MethodInfo method = AccessTools.Method(typeof(XmlPatchMethodForeach), nameof(XmlPatchMethodForeach.Foreach));
      XmlPatcher.addXmlFilePatchMethod("foreach", method);
    }

    private static void InitCaseSensitiveFilesystem(Mod mod, Harmony harmony) {
      if (!Config.CaseSensitiveFilesystemEnabled) {
        return;
      }

      harmony.PatchCategory("CaseSensitiveFilesystem");
      CaseSensitiveFilesystem.ApplyExistsPatches(harmony);
      CaseSensitiveFilesystem.ValidateModInfos();
      // Can't unload now because it's called within a foreach over the mod list we want to modify
      ModEvents.GameAwake.RegisterHandler(UnloadInvalidModInfos);
    }

    private static void UnloadInvalidModInfos(ref ModEvents.SGameAwakeData data) {
      CaseSensitiveFilesystem.UnloadInvalidModInfos();
    }
  }
}

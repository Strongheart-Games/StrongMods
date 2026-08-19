using System.Reflection;
using HarmonyLib;

namespace StrongMods {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAllUncategorized(Assembly.GetExecutingAssembly());
      InitServerOnlyClass(mod, harmony);
      InitBreadthFirstXmlPatcher(mod, harmony);
      InitXmlPatchMethodForeach(mod, harmony);
      InitXmlPatchMethodEnsure();
      InitXPathInheritance(harmony);
      InitCaseSensitiveFilesystem(mod, harmony);
      InitModInfoDependencies(mod, harmony);
    }

    private static void InitServerOnlyClass(Mod mod, Harmony harmony) {
      if (!Config.ServerOnlyClassEnabled) {
        return;
      }

      harmony.PatchCategory(ServerOnlyClass.ServerOnlyClassCategory);
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

    // Takes neither the mod nor Harmony: <ensure> patches nothing, it only adds a patch command.
    private static void InitXmlPatchMethodEnsure() {
      if (!Config.XmlPatchMethodEnsureEnabled) {
        return;
      }

      // Explicitly add this patch method so we control whether it's enabled or disabled
      MethodInfo method = AccessTools.Method(typeof(XmlPatchMethodEnsure), nameof(XmlPatchMethodEnsure.Ensure));
      XmlPatcher.addXmlFilePatchMethod("ensure", method);
    }

    private static void InitXPathInheritance(Harmony harmony) {
      if (!Config.XPathInheritanceEnabled) {
        return;
      }

      harmony.PatchCategory(XPathInheritance.Category);
    }

    private static void InitCaseSensitiveFilesystem(Mod mod, Harmony harmony) {
      if (!Config.CaseSensitiveFilesystemEnabled) {
        return;
      }

      CaseSensitiveFilesystem.Init();
      ModUnloader.Init(harmony);
      harmony.PatchCategory(CaseSensitiveFilesystem.Category);
      CaseSensitiveFilesystem.ValidateModInfos();
    }

    // Must run after InitCaseSensitiveFilesystem so dependency evaluation sees mods it marked for unloading as absent
    private static void InitModInfoDependencies(Mod mod, Harmony harmony) {
      if (!Config.ModInfoDependenciesEnabled) {
        return;
      }

      ModUnloader.Init(harmony);
      DependencyValidator.Validate(mod);
    }
  }
}

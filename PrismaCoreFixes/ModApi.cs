using System.Reflection;
using HarmonyLib;

namespace PrismaCoreFixes {
  public class ModApi : IModApi {
    public void InitMod(Mod _modInstance) {
      if (!ModManager.ModLoaded("PrismaCore")) {
        Log.Out("[PrismaCoreFixes] PrismaCore not loaded, aborting patching process.");
        return;
      }

      var harmony = new Harmony(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

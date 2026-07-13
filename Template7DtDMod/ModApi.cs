using System.Reflection;
using HarmonyLib;

namespace Template7DtDMod {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

using System.Reflection;
using HarmonyLib;

namespace Template7DtDMod {
  public class ModApi {
    public class Initializer : IModApi {
      public void InitMod(Mod _modInstance) {
        Harmony harmony = new(_modInstance.Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
      }
    }
  }
}

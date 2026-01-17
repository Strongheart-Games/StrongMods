using System.Reflection;
using HarmonyLib;

namespace RWGTools {
  public class HarmonyInitializer : IModApi {
    public void InitMod(Mod modInstance) {
      Harmony harmony = new(modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

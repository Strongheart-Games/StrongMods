using System.Reflection;
using HarmonyLib;

namespace BountifulQuests {
  public class ModApi : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

using System.Reflection;
using HarmonyLib;

namespace LootDiagnostics {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

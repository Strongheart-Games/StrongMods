using System.Reflection;
using HarmonyLib;

namespace StrongHonk {
  public class ModApi {
    public class Initializer : IModApi {
      public void InitMod(Mod _modInstance) {
        Harmony harmony = new(_modInstance.Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        ModEvents.GameAwake.RegisterHandler(OnGameAwake);
      }

      private static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
        OpenDoors.Init();
      }
    }
  }
}

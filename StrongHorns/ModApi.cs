using System.Reflection;
using HarmonyLib;

namespace StrongHorns {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.GameAwake.RegisterHandler(OnGameAwake);
    }

    private static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      OpenDoors.Init();
    }
  }
}

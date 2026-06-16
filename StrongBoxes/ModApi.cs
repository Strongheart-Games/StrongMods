using System.Reflection;
using HarmonyLib;

namespace StrongBoxes {
  public class ModApi : IModApi {
    public void InitMod(Mod _modInstance) {
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.GameAwake.RegisterHandler(OnGameAwake);
    }

    private static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      SortBoxes.Init();
    }
  }
}

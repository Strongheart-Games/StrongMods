using System.Reflection;
using HarmonyLib;

namespace StrongMods {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      FilenameCaseSensitivity.ValidateModInfos();
      ModEvents.GameAwake.RegisterHandler(OnGameAwake);
    }

    private static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      FilenameCaseSensitivity.UnloadInvalidModInfos();
    }
  }
}

using System.Reflection;
using HarmonyLib;

namespace AutoCollectLoot {
  public class ModApi : IModApi {
    public void InitMod(Mod mod) {
      Harmony harmony = new(mod.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ModEvents.GameStartDone.RegisterHandler(LootCoverageAuditRuntime.OnGameStartDone);
      ModEvents.GameUpdate.RegisterHandler(LootCoverageAuditRuntime.OnGameUpdate);
      ModEvents.GameShutdown.RegisterHandler(LootCoverageAuditRuntime.OnGameShutdown);
    }
  }
}

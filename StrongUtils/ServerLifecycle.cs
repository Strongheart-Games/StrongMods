using System.IO;

namespace StrongUtils {
  public class ServerLifecycle {

    public static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      ConfigManager.Init(Path.Combine(GameIO.GetSaveGameDir(), "StrongMods"));
      Log.Out("[StrongUtils] Initialized ConfigManager");
    }

    public static void OnGameStartDone(ref ModEvents.SGameStartDoneData data) {
      StrongZones.Init();
      Log.Out("[StrongUtils] Initialized StrongZones");
    }

    public static void OnGameShutdown(ref ModEvents.SGameShutdownData data) {
      ConfigManager.Instance?.Dispose();
      Log.Out("[StrongUtils] Disposed ConfigManager");
    }
  }
}

using System.IO;

namespace StrongUtils {
  public class ServerLifecycle {

    public static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      var configDirectory = Path.Combine(GameIO.GetSaveGameDir(), "StrongMods");
      ConfigManager.Init(configDirectory);
      Log.Out("[StrongUtils] Initialized ConfigManager");
      ServerLifecycleCommands.Init();
      Log.Out("[StrongUtils] Initialized ServerLifecycleCommands");
      KeyValueStore.KeyValueStore.Init(configDirectory);
      Log.Out("[StrongUtils] Initialized KeyValueStore");
    }

    public static void OnGameStartDone(ref ModEvents.SGameStartDoneData data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      StrongZones.Init();
      Log.Out("[StrongUtils] Initialized StrongZones");
      FastTravel.Init();
      Log.Out("[StrongUtils] Initialized FastTravel");
      StrongTeleport.Init();
      Log.Out("[StrongUtils] Initialized StrongTeleport");
      ServerLifecycleCommands.OnGameStartDone();
      Log.Out("[StrongUtils] Done running OnGameStartDone commands");
    }

    public static void OnGameShutdown(ref ModEvents.SGameShutdownData data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }
      ConfigManager.Instance?.Dispose();
      Log.Out("[StrongUtils] Disposed ConfigManager");
    }
  }
}

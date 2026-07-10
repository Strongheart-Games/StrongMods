using System.Collections.Generic;

namespace StrongUtils {
  public static class Reload {
    public static void ReloadXmlsSync(string[] _onlyXmls = null) {
      var xmlsToReload = new List<string>();
      foreach (WorldStaticData.XmlLoadInfo loadInfo in WorldStaticData.xmlsToLoad) {
        if (_onlyXmls is not null && !_onlyXmls.ContainsCaseInsensitive(loadInfo.XmlName)) {
          continue;
        }

        if (loadInfo.AllowReloadDuringGame) {
          xmlsToReload.Add(loadInfo.XmlName);
          loadInfo.CleanupMethod?.Invoke();
        } else if (_onlyXmls is not null) {
          Log.Warning($"XML loader: Config '{loadInfo.XmlName}' is not allowed to be reloaded during the game.");
        }
      }

      if (xmlsToReload.Count > 0) {
        ThreadManager.RunCoroutineSync(WorldStaticData.LoadAllXmlsCo(false, null, xmlsToReload.ToArray()));
      }
    }
  }
}

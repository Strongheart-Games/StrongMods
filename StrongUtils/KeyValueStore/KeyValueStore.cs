using System.IO;

namespace StrongUtils.KeyValueStore {
  public class KeyValueStore {
    public static IKeyValueStore Instance { get; private set; }

    public static void Init(string configDirectory) {
      Instance = new XmlKeyValueStore(Path.Combine(configDirectory, "kvstore.xml"));
    }
  }
}

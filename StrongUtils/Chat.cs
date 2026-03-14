using System.Collections.Generic;

namespace StrongUtils {
  public class Chat {
    public static void Whisper(EntityPlayer player, string message) {
      ClientInfo client = ConnectionManager.Instance.Clients.ForEntityId(player.entityId);
      Whisper(client, message);
    }

    public static void Whisper(ClientInfo client, string message) {
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        null, EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported);
      client.SendPackage(package);
    }

    public static void Global(string message) {
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, message,
        null, EMessageSender.None);
    }
  }
}

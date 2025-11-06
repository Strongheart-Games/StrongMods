using JetBrains.Annotations;

namespace StrongUtils {
  public class DenyAll {
    private static bool s_enabled;
    private static GameUtils.EKickReason s_reason;
    private static string s_message;

    public static bool IsEnabled() {
      return s_enabled;
    }

    public static GameUtils.EKickReason GetReason() {
      return s_reason;
    }

    public static string GetMessage() {
      return s_message;
    }

    public static void Enable(GameUtils.EKickReason reason, [CanBeNull] string message = null) {
      s_enabled = true;
      s_reason = reason;
      s_message = message;
    }

    public static void Disable() {
      s_enabled = false;
    }
  }
}

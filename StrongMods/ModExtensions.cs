using System.Runtime.CompilerServices;

namespace StrongMods {
  public static class ModExtensions {
    // Negative logic: assume valid until a problem is confirmed.
    private static readonly ConditionalWeakTable<Mod, StrongBox<bool>> s_hasInvalidModInfo = new();

    public static bool HasInvalidModInfo(this Mod mod) {
      return s_hasInvalidModInfo.TryGetValue(mod, out StrongBox<bool> flag) && flag.Value;
    }

    public static void SetInvalidModInfo(this Mod mod, bool value) {
      s_hasInvalidModInfo.GetOrCreateValue(mod).Value = value;
    }
  }
}

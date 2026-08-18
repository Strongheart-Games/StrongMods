using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AuthZ {
  /// <summary>
  /// The one Harmony patch the engine installs: a prefix on the <c>ProcessPackage</c> of every guarded package type.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Uses Harmony's multi-target form, so <see cref="TargetMethods"/> decides the patch surface at startup from the
  /// registered and enabled invariants. Turning every invariant off patches nothing at all.
  /// </para>
  /// <para>
  /// Server-side only. On a client the prefix returns immediately, because <c>Sender</c> is meaningless there and the
  /// whole question the invariants ask — does this agree with what the server knows — has no client-side meaning.
  /// </para>
  /// </remarks>
  [HarmonyPatch]
  public static class NetPackageGuard {
    public static IEnumerable<MethodBase> TargetMethods() {
      return InvariantEngine.PatchTargets();
    }

    private static bool Prefix(NetPackage __instance) {
      if (ConnectionManager.Instance == null || !ConnectionManager.Instance.IsServer) {
        return true;
      }

      // The world comes from GameManager rather than the patched method's own parameter. Every override in
      // V3.1.0-b14 names it _world, but injecting by parameter name would break silently on a rename, and this
      // reads the same value the handler is about to use.
      return InvariantEngine.Allow(__instance, GameManager.Instance?.World);
    }
  }
}

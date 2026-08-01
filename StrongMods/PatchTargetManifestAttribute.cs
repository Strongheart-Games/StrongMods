using System;

namespace StrongMods {
  /// <summary>
  ///   Marks a method as the published manifest of targets a programmatic Harmony patcher patches —
  ///   patches applied by calling <c>harmony.Patch(...)</c> directly, which the test suite cannot
  ///   discover through <c>[HarmonyPatch]</c> attribute enumeration.
  ///   <b>This attribute is inert: nothing in the game or Harmony invokes a tagged method, and
  ///   tagging patches nothing.</b> Unlike Harmony's <c>[HarmonyTargetMethod]</c>/<c>TargetMethod()</c>
  ///   lifecycle hooks, a manifest has exactly two callers, both explicit: the patching code itself
  ///   enumerates its own manifest as the single source of its target list (so the published list and
  ///   the patched list cannot drift apart), and the test suite discovers manifests by this attribute
  ///   and enumerates them headlessly to verify every target still exists in the game assemblies.
  ///   The pattern:
  ///   <code>
  ///     [PatchTargetManifest]
  ///     public static IEnumerable&lt;MethodBase&gt; MyPatchTargets() {
  ///       yield return AccessTools.Method(typeof(Foo), nameof(Foo.Bar))
  ///                    ?? throw new InvalidOperationException("[MyMod] Patch target not found: Foo.Bar");
  ///     }
  ///
  ///     public static void ApplyMyPatches(Harmony harmony) {
  ///       foreach (MethodBase target in MyPatchTargets()) {
  ///         harmony.Patch(target, prefix: ...);
  ///       }
  ///     }
  ///   </code>
  ///   Tagged methods must be <c>public static</c>, take no parameters, and return
  ///   <c>IEnumerable&lt;MethodBase&gt;</c>. They must be pure resolution — no patching, no other side
  ///   effects, safe to enumerate outside the game. A manifest must throw with a descriptive message
  ///   when a target cannot be resolved — never skip it or yield <c>null</c> — so a target lost to a
  ///   game update fails loudly at init and in the tests.
  ///   <see cref="CaseSensitiveFilesystem.ExistsPatchTargets" /> is the reference implementation.
  /// </summary>
  [AttributeUsage(AttributeTargets.Method)]
  public sealed class PatchTargetManifestAttribute : Attribute {
  }
}

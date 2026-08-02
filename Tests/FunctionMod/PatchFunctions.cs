using System;
using StrongMods;

namespace Tests.FunctionMod {
  /// <summary>
  ///   Patch functions the &lt;function&gt; conformance tests call. Deliberately written the way the spec
  ///   tells a modder to write them — public static, string in, string out, tagged — so the tests exercise
  ///   the documented contract rather than a special case.
  /// </summary>
  public static class PatchFunctions {
    [XmlPatchFunction]
    public static string Upper(string value) {
      return value.ToUpperInvariant();
    }

    [XmlPatchFunction]
    public static string Join(string left, string right) {
      return left + "-" + right;
    }

    /// <summary>Returns null, which the attribute's contract defines as "skip this iteration".</summary>
    [XmlPatchFunction]
    public static string Nothing(string value) {
      return null;
    }

    /// <summary>Throws, which the engine treats as a skip rather than letting it escape.</summary>
    [XmlPatchFunction]
    public static string Boom(string value) {
      throw new InvalidOperationException("boom");
    }

    /// <summary>Perfect signature, no attribute — must still be rejected.</summary>
    public static string Untagged(string value) {
      return value;
    }

    /// <summary>Tagged but wrongly shaped: the contract requires a string return.</summary>
    [XmlPatchFunction]
    public static int NotAString(string value) {
      return value.Length;
    }
  }
}

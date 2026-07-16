using System;

namespace StrongMods {
  /// <summary>
  ///   Marks a method as callable from a <c>&lt;foreach&gt;</c> patch body via a
  ///   <c>&lt;function name="..." method="[namespace.]Class.Method, [mod]" /&gt;</c> declaration.
  ///   Resolution is by reflection over the named type, so this attribute is purely an opt-in gate:
  ///   an untagged method with a matching signature is rejected.
  ///   Tagged methods must be <c>public static</c>, return <c>string</c>, take only <c>string</c>
  ///   parameters, be non-generic, and not be overloaded. Returning <c>null</c> tells the patcher to
  ///   skip the current iteration, the same verdict an XPath expression matching no nodes produces;
  ///   returning <c>string.Empty</c> is a legitimate empty value.
  ///   Patch functions run once per matched node during XML patching at startup. They should be pure:
  ///   the patcher makes no guarantees about call count or ordering, and side effects are not
  ///   reflected in <c>ConfigDump/</c>.
  /// </summary>
  [AttributeUsage(AttributeTargets.Method)]
  public sealed class XmlPatchFunctionAttribute : Attribute {
  }
}

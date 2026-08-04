using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Tests.Fixtures;

/// <summary>What the LoadAndPatchConfig prefix did with one request.</summary>
/// <param name="FellThroughToVanilla">
///   The prefix returned true, letting the game's own load-and-patch run — what must happen for any file
///   outside the breadth-first pipeline.
/// </param>
/// <param name="CallbackInvoked">Whether the caller's callback received a document.</param>
/// <param name="ServedXml">The document handed to the callback, or null if none was.</param>
public sealed record PrefixOutcome(bool FellThroughToVanilla, bool CallbackInvoked, string ServedXml);

/// <summary>
///   Reaches the breadth-first patcher's private cache of pre-patched documents — the seam the whole design
///   turns on. Phase 2 fills it, phase 3 drains it through the LoadAndPatchConfig prefix, and cross-file
///   <c>source=</c> reads it. Tests seed it directly instead of running the coroutine, which needs the game's
///   file loading, mod list and frame timing.
/// </summary>
public sealed class PatcherCache {
  private readonly IDictionary entries;
  private readonly MethodInfo prefix;
  private readonly MethodInfo tryGet;
  private readonly Type callbackType;
  private readonly Type xmlFileType;
  private readonly FieldInfo xmlDocField;

  internal PatcherCache(Type patcherType, Type xmlFileType, FieldInfo xmlDocField) {
    this.xmlFileType = xmlFileType;
    this.xmlDocField = xmlDocField;
    entries = (IDictionary)patcherType
      .GetField("s_patchedFiles", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    tryGet = patcherType.GetMethod("TryGetPatchedFile", BindingFlags.Public | BindingFlags.Static)!;
    prefix = patcherType.GetNestedType("XmlPatcherLoadAndPatchConfigPatch")!
      .GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)!;
    callbackType = typeof(Action<>).MakeGenericType(xmlFileType);
  }

  public int Count => entries.Count;

  /// <summary>Adds a pre-patched document under <paramref name="name" /> (no .xml extension).</summary>
  public void Seed(string name, object xmlFile) => entries[name] = xmlFile;

  /// <summary>Marks a base XML that failed to load — the null marker phase 1 leaves behind.</summary>
  public void SeedFailure(string name) => entries[name] = null;

  public void Clear() => entries.Clear();

  public bool Contains(string name) => entries.Contains(name);

  /// <summary>The engine's own public lookup, which cross-file source= resolution goes through.</summary>
  public bool TryGetPatchedFile(string name, out string xml) {
    object[] args = { name, null };
    var found = (bool)tryGet.Invoke(null, args)!;
    xml = args[1] == null ? null : ((XDocument)xmlDocField.GetValue(args[1])!).ToString(SaveOptions.DisableFormatting);
    return found;
  }

  /// <summary>
  ///   Runs the LoadAndPatchConfig prefix the way the game's phase 3 does: call it, then drive the coroutine
  ///   it produced, since that is what actually invokes the callback.
  /// </summary>
  public PrefixOutcome InvokePrefix(string configName) {
    object served = null;
    var callback = Delegate.CreateDelegate(callbackType, new CallbackSink(f => served = f),
      typeof(CallbackSink).GetMethod(nameof(CallbackSink.Receive))!);

    object[] args = { configName, callback, null };
    var fellThrough = (bool)prefix.Invoke(null, args)!;
    if (args[2] is IEnumerator coroutine) {
      while (coroutine.MoveNext()) {
        // the prefix's replacement coroutine runs to completion in one step
      }
    }

    return new PrefixOutcome(fellThrough, served != null,
      served == null ? null : ((XDocument)xmlDocField.GetValue(served)!).ToString(SaveOptions.DisableFormatting));
  }

  /// <summary>
  ///   Receives the callback. Declared as object so one method binds to Action&lt;XmlFile&gt; for the host's
  ///   XmlFile type, which this assembly cannot name at compile time — delegate creation allows a parameter
  ///   type the delegate's own is assignable to.
  /// </summary>
  private sealed class CallbackSink {
    private readonly Action<object> onReceive;

    public CallbackSink(Action<object> onReceive) => this.onReceive = onReceive;

    public void Receive(object file) => onReceive(file);
  }
}

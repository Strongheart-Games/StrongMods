using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace Tests.Fixtures;

public enum LogLevel { Error = 0, Assert = 1, Warning = 2, Info = 3, Exception = 4 }

public sealed record LogEntry(LogLevel Level, string Message);

public sealed record PatchOutcome(bool Applied, string Xml, IReadOnlyList<LogEntry> Logs) {
  public IEnumerable<string> Warnings => Logs.Where(l => l.Level == LogLevel.Warning).Select(l => l.Message);
  public IEnumerable<string> Errors => Logs.Where(l => l.Level == LogLevel.Error).Select(l => l.Message);
}

/// <summary>
///   The host that holds the game's XML patching pipeline, executable headlessly. A HOST is one isolated
///   assembly universe: the game's Managed assemblies plus a deliberately chosen set of companions, loaded
///   into a private AssemblyLoadContext so its types never unify with the test runtime's or another host's;
///   expensive to build (~50 MB of assemblies, static initializers run), so one per test session.
///   This host's companions are the STUB UnityEngine.CoreModule, StrongMods, and the fixture mod. The stub
///   is what makes game code that logs runnable outside the game: the real CoreModule's engine-internal
///   callback registration throws in LogLibrary's Log initializer, poisoning the type, and everything that
///   logs — the whole XML patcher — dies with it (#43 plan §2).
///   Deliberately a separate host from <see cref="GameEngineHost" /> (the real-engine host): the smoke tests
///   keep the REAL CoreModule so patch targets whose signatures use Unity types still resolve. Nothing
///   crosses between them.
/// </summary>
public sealed class PatcherHost {
  /// <summary>The mod name &lt;function&gt; references resolve against; see Tests\FunctionMod.</summary>
  public const string FixtureModName = "Tests.FunctionMod";

  public static readonly Lazy<PatcherHost> Instance = new(() => new PatcherHost());

  private readonly List<LogEntry> captured = new();
  private readonly object fixtureMod;
  private readonly FieldInfo xmlDocField;
  private readonly Type xmlFileType;
  private readonly MethodInfo singlePatch;
  private readonly MethodInfo patchXmlMethod;

  private PatcherHost() {
    // StrongMods needs 0Harmony resolvable in the default context; the resolver installs once, explicitly
    // (it used to ride on a static-ctor side effect of "touching GameTree first").
    GameEngineHost.EnsureHarmonyResolver();
    var managedDir = AssemblyMetadata.Get("SdtdManagedDir");
    var stubDir = AssemblyMetadata.Get("UnityStubDir");
    var strongModsDir = Path.Combine(AssemblyMetadata.Get("RepoRoot"), "StrongMods", "bin",
      AssemblyMetadata.Get("Configuration"));
    var functionModDir = AssemblyMetadata.Get("FunctionModDir");
    var context = new StubbedUnitContext(stubDir, new[] { managedDir, strongModsDir, functionModDir });

    Assembly acs = context.LoadFromAssemblyPath(Path.Combine(managedDir, "Assembly-CSharp.dll"));
    Assembly logLibrary = context.LoadFromAssemblyPath(Path.Combine(managedDir, "LogLibrary.dll"));
    Assembly strongMods = context.LoadFromAssemblyPath(Path.Combine(strongModsDir, "StrongMods.dll"));
    Assembly functionMod = context.LoadFromAssemblyPath(Path.Combine(functionModDir, FixtureModName + ".dll"));

    xmlFileType = acs.GetType("XmlFile")!;
    xmlDocField = xmlFileType.GetField("XmlDoc")!;
    fixtureMod = Activator.CreateInstance(acs.GetType("Mod")!)!;
    Type patcher = acs.GetType("XmlPatcher")!;
    singlePatch = patcher.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
      .First(m => m.Name == "singlePatch");
    patchXmlMethod = patcher.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
      .First(m => m.Name == "PatchXml" && m.GetParameters().Length == 4);

    SubscribeToLog(logLibrary);
    SeedPatchMethods(acs, strongMods, patcher);
    RegisterFixtureMod(acs, functionMod);
    Cache = new PatcherCache(strongMods.GetType("StrongMods.BreadthFirstXmlPatcher")!, xmlFileType, xmlDocField);
  }

  /// <summary>
  ///   The unit's own <c>Data\Config</c> — the vanilla XML and Localization.csv that mod patches are applied
  ///   on top of. Carried by vendored trees and the CI packages since #59, so this resolves wherever the
  ///   suite runs.
  /// </summary>
  public string VanillaConfigDir { get; } = AssemblyMetadata.Get("SdtdConfigDir");

  /// <summary>Builds one of the game's XmlFile objects from a string, in the host's own type identity.</summary>
  public object CreateXmlFile(string xml, string filename) => NewXmlFile(xml, filename);

  /// <summary>
  ///   Applies a whole patch <em>file</em> — every command in it — to a target document, the way the patcher's
  ///   phase 2 does (<c>XmlPatcher.PatchXml</c>), rather than the single command <see cref="Apply" /> takes.
  ///   The target is mutated in place; the return value is what the game logged while doing it.
  /// </summary>
  public IReadOnlyList<LogEntry> ApplyPatchFile(object targetFile, string patchXml, string patchFileName) {
    captured.Clear();
    object patchFile = NewXmlFile(patchXml, patchFileName);
    XElement patchRoot = Document(patchFile).Root!;
    try {
      patchXmlMethod.Invoke(null, new[] { targetFile, patchRoot, patchFile, fixtureMod });
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      throw e.InnerException;
    }

    return captured.ToList();
  }

  /// <summary>The document a host-typed XmlFile holds, as text with the patch-trace comments stripped.</summary>
  public string XmlOf(object xmlFile) => Normalize(Document(xmlFile));

  /// <summary>
  ///   Presents the fixture mod to the game as a loaded mod, which is how a &lt;function&gt; reference of the
  ///   form "Class.Method, ModName" finds an assembly at all (the engine asks ModManager.GetMod). Nothing
  ///   else about mod loading is simulated — this is exactly the lookup the reference performs.
  /// </summary>
  private static void RegisterFixtureMod(Assembly acs, Assembly functionMod) {
    Type modType = acs.GetType("Mod")!;
    object mod = Activator.CreateInstance(modType)!;
    SetMember(modType, mod, "Name", FixtureModName);
    SetMember(modType, mod, "allAssemblies", new List<Assembly> { functionMod });

    object loadedMods = acs.GetType("ModManager")!
      .GetField("loadedMods", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
      .GetValue(null)!;
    MethodInfo add = loadedMods.GetType().GetMethods()
      .First(m => m.Name == "Add" && m.GetParameters().Length == 2);
    add.Invoke(loadedMods, new[] { FixtureModName, mod });
  }

  private static void SetMember(Type type, object instance, string name, object value) {
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    MemberInfo member = type.GetMember(name, Instance).First();
    switch (member) {
      case PropertyInfo property when property.CanWrite:
        property.SetValue(instance, value);
        break;
      case FieldInfo field:
        field.SetValue(instance, value);
        break;
      default:
        type.GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
          .SetValue(instance, value);
        break;
    }
  }

  /// <summary>
  ///   The breadth-first patcher's cache of pre-patched documents, which is also what cross-file
  ///   <c>source=</c> resolution reads.
  /// </summary>
  public PatcherCache Cache { get; private set; }

  /// <summary>
  ///   Applies one patch command to one document and reports what happened — the whole surface the
  ///   conformance tests need. Both XML strings are documents in their own right: the patch is a single
  ///   command element such as <c>&lt;foreach …&gt;…&lt;/foreach&gt;</c>.
  ///   <paramref name="sources" /> seeds the patcher cache (name without <c>.xml</c> → document) for the
  ///   duration of the call, which is how a patch reaches another config file with <c>source=</c>.
  /// </summary>
  public PatchOutcome Apply(string targetXml, string patchXml,
                            IReadOnlyDictionary<string, string> sources = null) {
    if (sources != null) {
      foreach ((var name, var xml) in sources) {
        Cache.Seed(name, NewXmlFile(xml, name + ".xml"));
      }
    }

    try {
      return ApplyCore(targetXml, patchXml);
    } finally {
      Cache.Clear();
    }
  }

  private PatchOutcome ApplyCore(string targetXml, string patchXml) {
    captured.Clear();
    object target = NewXmlFile(targetXml, "target.xml");
    object patchFile = NewXmlFile(patchXml, "patch.xml");
    XElement patchElement = Document(patchFile).Root!;
    bool applied;
    try {
      applied = (bool)singlePatch.Invoke(null, new[] { target, patchElement, patchFile, fixtureMod })!;
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      // Tests assert on the game's own failures, not on reflection's wrapper around them.
      throw e.InnerException;
    }

    return new PatchOutcome(applied, Normalize(Document(target)), captured.ToList());
  }

  private object NewXmlFile(string xml, string filename) =>
    Activator.CreateInstance(xmlFileType, xml, "fixtures", filename, true)!;

  private XDocument Document(object xmlFile) => (XDocument)xmlDocField.GetValue(xmlFile)!;

  /// <summary>
  ///   Vanilla patching narrates every change into the document as XML comments. Useful in-game, noise in an
  ///   assertion, so tests see the document without them.
  /// </summary>
  private static string Normalize(XDocument document) {
    var copy = new XDocument(document);
    copy.DescendantNodes().OfType<XComment>().Remove();
    return copy.ToString(SaveOptions.DisableFormatting);
  }

  /// <summary>
  ///   Captures the game's own log output so tests can assert the spec'd skip-warnings and errors. Subscribes
  ///   to the extended callback for the unformatted message (the plain one carries a timestamp prefix). The
  ///   handler declares the LogType parameter as int: delegate binding accepts an enum's underlying type, and
  ///   the enum itself lives in the host, out of this assembly's compile-time reach.
  /// </summary>
  private void SubscribeToLog(Assembly logLibrary) {
    Type log = logLibrary.GetType("Log")!;
    RuntimeHelpers.RunClassConstructor(log.TypeHandle);
    EventInfo callbacks = log.GetEvent("LogCallbacksExtended")!;
    MethodInfo handler = typeof(PatcherHost).GetMethod(nameof(OnLog), BindingFlags.NonPublic | BindingFlags.Instance)!;
    callbacks.AddEventHandler(null, Delegate.CreateDelegate(callbacks.EventHandlerType!, this, handler));
  }

  private void OnLog(string formatted, string plain, string trace, int type, DateTime timestamp, long uptime) {
    captured.Add(new LogEntry((LogLevel)type, plain));
  }

  /// <summary>
  ///   Fills the patch-command registry by hand. In-game, XmlPatcher's initializer discovers the commands by
  ///   scanning every type for [XmlPatchMethod]; in here that scan comes up empty, because the stub CoreModule
  ///   only satisfies the types our paths touch and the scan needs all of them. So register the same vanilla
  ///   commands the scan would have found, plus foreach exactly as StrongMods' ModApi registers it.
  /// </summary>
  private static void SeedPatchMethods(Assembly acs, Assembly strongMods, Type patcher) {
    MethodInfo add = patcher.GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m => m.Name == "addXmlFilePatchMethod" && m.GetParameters()[1].ParameterType == typeof(MethodInfo));
    Type attribute = acs.GetType("XmlPatchMethodAttribute")!;
    FieldInfo patchName = attribute.GetField("PatchName")!;
    FieldInfo requiresXpath = attribute.GetField("RequiresXpath")!;

    foreach (MethodInfo method in acs.GetType("XmlPatchMethods")!
               .GetMethods(BindingFlags.Public | BindingFlags.Static)) {
      foreach (object attr in method.GetCustomAttributes(attribute, false)) {
        add.Invoke(null, new[] { patchName.GetValue(attr), method, requiresXpath.GetValue(attr) });
      }
    }

    MethodInfo foreachMethod = strongMods.GetType("StrongMods.XmlPatchMethodForeach")!.GetMethod("Foreach")!;
    add.Invoke(null, new object[] { "foreach", foreachMethod, true });
  }

  /// <summary>
  ///   Resolution order: our stub first (so it shadows the real CoreModule in here), then the host (framework
  ///   assemblies must unify with the runtime's own, and 0Harmony must stay the single shared copy), then the
  ///   unit's directories for the game's assemblies and the mod DLLs.
  /// </summary>
  private sealed class StubbedUnitContext : AssemblyLoadContext {
    private readonly string stubDir;
    private readonly IReadOnlyList<string> unitDirs;

    public StubbedUnitContext(string stubDir, IReadOnlyList<string> unitDirs) : base("stubbed-unit") {
      this.stubDir = stubDir;
      this.unitDirs = unitDirs;
    }

    protected override Assembly Load(AssemblyName name) {
      var stub = Path.Combine(stubDir, name.Name + ".dll");
      if (File.Exists(stub)) {
        return LoadFromAssemblyPath(stub);
      }

      try {
        return Default.LoadFromAssemblyName(name);
      } catch (Exception) {
        // not a framework or host-provided assembly — fall through to the unit's own
      }

      foreach (var dir in unitDirs) {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) {
          return LoadFromAssemblyPath(path);
        }
      }

      return null;
    }
  }
}

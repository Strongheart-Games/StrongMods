using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;


namespace Tests.Fixtures;

/// <summary>
///   A host for EXECUTING a mod's own decision logic with no game running — the "U1 seam" of
///   .ai/test-idea-tool-gap-seed.md, minus the per-mod refactors. A HOST is one isolated assembly universe
///   (see <see cref="PatcherHost" /> for the concept): the game's Managed assemblies plus the STUB
///   UnityEngine.CoreModule plus one mod DLL, in a private AssemblyLoadContext.
///   <para>
///     The stub is what makes this possible at all: the real CoreModule's initializer throws headlessly, so
///     any mod method that logs would die with it. With the stub, mod code that only touches the BCL, the
///     game's plain data types, and <c>Log</c> runs for real.
///   </para>
///   <para>
///     What it does NOT provide: a world, a connection, an engine loop. Game singletons are null unless a
///     test plants one (<see cref="Uninitialized" /> + <see cref="SetStatic" /> do that for the plain-object
///     ones). Logic that reaches into those is out of this host's reach by design — that is the R1/R2
///     world-control gap, not something a stub can fake.
///   </para>
///   One host per mod per session: building it loads ~50 MB of assemblies and runs their initializers.
/// </summary>
public sealed class ModLogicHost {
  private static readonly Dictionary<string, ModLogicHost> Hosts = new();
  private static readonly object HostsLock = new();

  private readonly Assembly modAssembly;
  private readonly StubbedContext context;

  private ModLogicHost(string modName, bool stubUnity) {
    GameTree tree = GameTree.Default();
    if (!Directory.Exists(tree.ManagedDir)) {
      throw new DirectoryNotFoundException(
        $"The game tree for {tree.Label} is missing at '{tree.Root}'. Restore the declared versions:  " +
        "dotnet restore build/GameAssemblies.csproj --packages packages --configfile " +
        "build/GameAssemblies.nuget.config");
    }

    Tree = tree;
    GameEngineHost.EnsureHarmonyResolver();
    var modDir = Path.Combine(AssemblyMetadata.Get("RepoRoot"), modName, "bin",
      AssemblyMetadata.Get("Configuration"));
    var modDll = Path.Combine(modDir, modName + ".dll");
    if (!File.Exists(modDll)) {
      throw new FileNotFoundException(
        $"'{modDll}' is missing — build the solution first (dotnet build StrongMods.sln -c " +
        AssemblyMetadata.Get("Configuration") + ").", modDll);
    }

    context = new StubbedContext(stubUnity ? AssemblyMetadata.Get("UnityStubDir") : null,
      new[] { tree.ManagedDir, modDir });
    GameAssembly = context.LoadFromAssemblyPath(Path.Combine(tree.ManagedDir, "Assembly-CSharp.dll"));
    if (stubUnity) {
      // Log's initializer must run in the stubbed universe before any mod code calls it. With the real
      // CoreModule it THROWS, which is exactly the trade RealUnity accepts (see For).
      Assembly logLibrary = context.LoadFromAssemblyPath(Path.Combine(tree.ManagedDir, "LogLibrary.dll"));
      RuntimeHelpers.RunClassConstructor(logLibrary.GetType("Log")!.TypeHandle);
    }

    modAssembly = context.LoadFromAssemblyPath(modDll);
  }

  /// <summary>The game tree this host loaded — which version, which unit, where.</summary>
  public GameTree Tree { get; }

  /// <summary>The unit's own Assembly-CSharp, in this host's type identity.</summary>
  public Assembly GameAssembly { get; }

  /// <summary>
  ///   The host for a mod, in one of two universes. <paramref name="stubUnity" /> true (the default) shadows
  ///   UnityEngine.CoreModule with the clean-room stub, which is what makes <c>Log</c> — and therefore any
  ///   mod method that logs — runnable; the price is that only the handful of Unity types the stub declares
  ///   exist, so nothing whose fields reach further will even load (measured: Assembly-CSharp references 225
  ///   CoreModule types the stub does not have, entity types among them).
  ///   <para>
  ///     False keeps the REAL CoreModule, so entity and other Unity-shaped game types load with full
  ///     fidelity — at the cost of <c>Log</c>: its initializer throws headlessly, so a method that logs on
  ///     the path under test cannot run here. Pick the universe by which of the two the code needs.
  ///   </para>
  /// </summary>
  public static ModLogicHost For(string modName, bool stubUnity = true) {
    lock (HostsLock) {
      var key = modName + (stubUnity ? "+stub" : "+unity");
      if (!Hosts.TryGetValue(key, out ModLogicHost host)) {
        host = new ModLogicHost(modName, stubUnity);
        Hosts[key] = host;
      }

      return host;
    }
  }

  /// <summary>A type from the mod under test.</summary>
  public Type ModType(string fullName) =>
    modAssembly.GetType(fullName) ??
    throw new InvalidOperationException($"'{fullName}' is not in {modAssembly.GetName().Name}.");

  /// <summary>
  ///   A type from any other assembly in this host's universe, by simple assembly name — Unity's own types
  ///   most of all. It must come from HERE, not from <c>Type.GetType</c>: the runtime's default context has
  ///   its own (or no) copy, and a Vector3 from there is a different type than the one the mod compiled
  ///   against.
  /// </summary>
  public Type TypeFrom(string assemblyName, string fullName) {
    Assembly assembly = context.LoadFromAssemblyName(new AssemblyName(assemblyName));
    return assembly.GetType(fullName) ??
           throw new InvalidOperationException($"'{fullName}' is not in {assemblyName}.");
  }

  /// <summary>A type from the game, in this host's identity.</summary>
  public Type GameType(string fullName) =>
    GameAssembly.GetType(fullName) ??
    throw new InvalidOperationException($"'{fullName}' is not in Assembly-CSharp.");

  /// <summary>
  ///   An instance of a game type with no constructor run — how a test gets an object the game would only
  ///   ever build inside a live world (an entity, a connection manager). Only its plain fields are
  ///   meaningful afterwards; anything the real constructor would have wired is null.
  /// </summary>
  public static object Uninitialized(Type type) => RuntimeHelpers.GetUninitializedObject(type);

  public static void SetStatic(Type type, string name, object value) =>
    Member(type, name, BindingFlags.Static).Set(null, value);

  public static object GetStatic(Type type, string name) => Member(type, name, BindingFlags.Static).Get(null);

  public static void SetInstance(object target, string name, object value) =>
    Member(target.GetType(), name, BindingFlags.Instance).Set(target, value);

  /// <summary>Calls a static method, surfacing the mod's own exception rather than reflection's wrapper.</summary>
  public static object CallStatic(Type type, string name, params object[] args) {
    MethodInfo method = FindMethod(type, name, args);
    return Unwrap(() => method.Invoke(null, args));
  }

  public static object Call(object target, string name, params object[] args) {
    MethodInfo method = FindMethod(target.GetType(), name, args);
    return Unwrap(() => method.Invoke(target, args));
  }

  private static object Unwrap(Func<object> call) {
    try {
      return call();
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      throw e.InnerException;
    }
  }

  /// <summary>
  ///   Calls a generic method by naming its type arguments — <c>Get&lt;int&gt;("k", 0)</c> and friends, which
  ///   arity alone cannot pick out.
  /// </summary>
  public static object CallGeneric(object target, string name, Type[] typeArguments, params object[] args) {
    MethodInfo method = FindMethod(target.GetType(), name, args, true).MakeGenericMethod(typeArguments);
    return Unwrap(() => method.Invoke(target, args));
  }

  /// <summary>
  ///   Picks the overload by name, arity, and — where a mod overloads on parameter type, as the key-value
  ///   store does — by which one the given arguments actually fit. An exact type match wins over an
  ///   assignable one, so <c>Set(string, int)</c> is not reached by way of <c>Set(string, string)</c>.
  /// </summary>
  private static MethodInfo FindMethod(Type type, string name, object[] args, bool generic = false) {
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                             BindingFlags.Instance | BindingFlags.FlattenHierarchy;
    MethodInfo best = null;
    var bestScore = -1;
    foreach (MethodInfo candidate in type.GetMethods(All)) {
      if (candidate.Name != name || candidate.GetParameters().Length != args.Length ||
          candidate.IsGenericMethodDefinition != generic) {
        continue;
      }

      var score = Fit(candidate.GetParameters(), args);
      if (score > bestScore) {
        best = candidate;
        bestScore = score;
      }
    }

    return best ?? throw new InvalidOperationException(
      $"{type.Name} has no {(generic ? "generic " : "")}method '{name}' taking {args.Length} argument(s).");
  }

  /// <summary>2 per exactly-typed argument, 1 per merely assignable one, -1 for an outright mismatch.</summary>
  private static int Fit(ParameterInfo[] parameters, object[] args) {
    var score = 0;
    for (var i = 0; i < args.Length; i++) {
      Type wanted = parameters[i].ParameterType;
      if (args[i] == null) {
        score += wanted.IsValueType ? -1 : 1;
      } else if (args[i].GetType() == wanted) {
        score += 2;
      } else if (wanted.IsInstanceOfType(args[i]) || wanted.IsGenericParameter) {
        score += 1;
      } else {
        score -= 1;
      }
    }

    return score;
  }

  private static Accessor Member(Type type, string name, BindingFlags scope) {
    const BindingFlags Both = BindingFlags.Public | BindingFlags.NonPublic;
    for (Type t = type; t != null; t = t.BaseType) {
      FieldInfo field = t.GetField(name, Both | scope);
      if (field != null) {
        return new Accessor(field.GetValue, field.SetValue);
      }

      PropertyInfo property = t.GetProperty(name, Both | scope);
      if (property != null) {
        // Auto-properties with a private setter are still reachable; a get-only one needs its field.
        if (property.SetMethod != null) {
          return new Accessor(o => property.GetValue(o), (o, v) => property.SetValue(o, v));
        }

        FieldInfo backing = t.GetField($"<{name}>k__BackingField", Both | scope);
        if (backing != null) {
          return new Accessor(backing.GetValue, backing.SetValue);
        }
      }
    }

    throw new InvalidOperationException($"{type.Name} has no reachable member '{name}'.");
  }

  private sealed record Accessor(Func<object, object> Get, Action<object, object> Set);

  /// <summary>Stub first when there is one (it must shadow the real CoreModule), then the host runtime, then
  ///   the unit. A null stub directory is the RealUnity universe.</summary>
  private sealed class StubbedContext : AssemblyLoadContext {
    private readonly string stubDir;
    private readonly IReadOnlyList<string> unitDirs;

    public StubbedContext(string stubDir, IReadOnlyList<string> unitDirs) : base("mod-logic") {
      this.stubDir = stubDir;
      this.unitDirs = unitDirs;
    }

    protected override Assembly Load(AssemblyName name) {
      var stub = stubDir == null ? null : Path.Combine(stubDir, name.Name + ".dll");
      if (stub != null && File.Exists(stub)) {
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

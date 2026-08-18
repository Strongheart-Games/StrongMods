// Classifies a 7 Days to Die mod's SIDE (client / server / both) and its EAC-off requirement from its shipped
// artifacts alone — prototype for #80.
//
// Why static analysis can decide most of this: the two things that make a mod client-required are (a) content
// the server never sends, and (b) code that only runs client-side. Both are readable. The game itself names
// (a): WorldStaticData.xmlsToLoad carries a per-entry-point SendToClients flag, and WorldStaticData
// .SendXmlsToClient forwards exactly the entries where it is set — so a Config patch to a SendToClients entry
// point reaches clients from the server and needs no client install. Localization is the same story one level
// over: Localization.PatchedData is streamed per client by NetPackageLocalization during
// GameManager.RequestToEnterGame, so a mod's Localization.csv reaches clients too.
//
// Two signals that LOOK usable are dead, and the tool deliberately does not use them:
//   * Type-set differencing between the game and dedicated-server Assembly-CSharp.dll. Measured on V3.1.0-b14:
//     7559 vs 7555 types, and every difference is Burst codegen ($BurstDirectCall / $PostfixBurstDelegate).
//     XUiC_*, EntityPlayerLocal and friends are present in BOTH. "Type missing from the server assembly" never
//     fires.
//   * Assembly-reference differencing. The two Managed directories hold the same 154 file names, and the Unity
//     modules are byte-identical. A mod DLL referencing UnityEngine.UIModule loads fine on a server.
// So client-side code is identified by a MARKER SET over game types (XUi hierarchy + a short curated list),
// not by anything the two units disagree about.
//
// The EAC clause is not re-derived here: #77 settled it against the IL — a mod
// needs EAC off IFF a .dll lands in the CLIENT's Mods folder, absent SkipWithAntiCheat. That makes EAC-off a
// FUNCTION of side, which is why this tool computes side first and EAC second.
//
// Paths are derived, never hardcoded: the repo root is found by walking up to StrongMods.sln, and the game
// tree comes from the version declared in build/GameVersions.props resolved through its own registry map.
//
// Usage (from anywhere inside the repo):
//   dotnet run StrongDev/.ai/tools/modclassify.cs                 classify every staged mod under */bin/Debug
//   dotnet run StrongDev/.ai/tools/modclassify.cs -- <dir>...     classify the named mod folders instead
//   dotnet run StrongDev/.ai/tools/modclassify.cs -- --evidence   add the per-mod evidence dump
// A staged folder is produced by `dotnet build StrongMods.sln -c Debug`, which never touches a live install.
#:package Mono.Cecil@0.11.6
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

var wantEvidence = args.Contains("--evidence");
string[] explicitDirs = args.Where(a => !a.StartsWith("--")).ToArray();

string repoRoot = FindRepoRoot(Environment.CurrentDirectory);
if (repoRoot == null) {
  Console.Error.WriteLine("!! run from inside the StrongMods repo (StrongMods.sln not found above the cwd)");
  return 1;
}

string managedDir = ResolveManagedDir(repoRoot, out string versionLabel);
if (managedDir == null) {
  Console.Error.WriteLine($"!! no restored game tree for the declared version {versionLabel}; run:\n" +
                          "   dotnet restore build/GameAssemblies.csproj --packages packages " +
                          "--configfile build/GameAssemblies.nuget.config");
  return 1;
}

GameSideFacts facts = GameSideFacts.Read(managedDir);
Console.WriteLine($"game tree : {versionLabel}  ({managedDir})");
Console.WriteLine($"game facts: {facts.EntryPoints.Count} XML entry points, " +
                  $"{facts.EntryPoints.Values.Count(e => !e.SendToClients)} not sent to clients, " +
                  $"{facts.ClientOnlyTypes.Count} client-only marker types");

List<string> modDirs = explicitDirs.Length > 0
  ? explicitDirs.Select(Path.GetFullPath).ToList()
  : Directory.EnumerateDirectories(repoRoot)
    .Select(d => Path.Combine(d, "bin", "Debug"))
    .Where(d => File.Exists(Path.Combine(d, "ModInfo.xml")))
    .OrderBy(d => d).ToList();

if (modDirs.Count == 0) {
  Console.Error.WriteLine("!! no staged mod folders found — build first: dotnet build StrongMods.sln -c Debug");
  return 1;
}

var verdicts = modDirs.Select(d => ModSideClassifier.Classify(d, facts, managedDir)).ToList();

Console.WriteLine();
Console.WriteLine("| Mod | Side | Confidence | EAC-off | Deciding signal |");
Console.WriteLine("|-----|------|-----------|---------|-----------------|");
foreach (SideVerdict v in verdicts) {
  Console.WriteLine($"| {v.ModName} | {v.Side} | {v.Confidence} | {v.EacOff} | {v.Deciding} |");
}

if (wantEvidence) {
  foreach (SideVerdict v in verdicts) {
    Console.WriteLine($"\n### {v.ModName}");
    foreach (string e in v.Evidence) {
      Console.WriteLine("  - " + e);
    }
  }
}

var withCaveats = verdicts.Where(v => v.Caveats.Count > 0).ToList();
Console.WriteLine($"\n## Shapes the classifier cannot fully call ({withCaveats.Count} of {verdicts.Count} mods)");
foreach (SideVerdict v in withCaveats) {
  foreach (string c in v.Caveats) {
    Console.WriteLine($"  {v.ModName,-30} {c}");
  }
}

return 0;

static string FindRepoRoot(string start) {
  for (var d = new DirectoryInfo(start); d != null; d = d.Parent) {
    if (File.Exists(Path.Combine(d.FullName, "StrongMods.sln"))) {
      return d.FullName;
    }
  }

  return null;
}

// The declared version and its label->package-version registry both live in build/GameVersions.props, which is
// the one place either is written down. Reading them keeps this tool honest when the repo adopts a new version.
static string ResolveManagedDir(string repoRoot, out string label) {
  string props = File.ReadAllText(Path.Combine(repoRoot, "build", "GameVersions.props"));
  string declared = Regex.Match(props, @"<SdtdDevVersion[^>]*>([^<]+)<").Groups[1].Value.Trim();
  label = declared;
  string map = Regex.Match(props, @"<SdtdGameVersionMap[^>]*>([^<]+)<").Groups[1].Value;
  string packageVersion = map.Split(';')
    .Select(pair => pair.Split('='))
    .Where(kv => kv.Length == 2 && kv[0].Trim() == declared)
    .Select(kv => kv[1].Trim()).FirstOrDefault();
  if (packageVersion == null) {
    return null;
  }

  // The dedicated-server unit is the right basis: side questions are about what a SERVER can carry alone, and
  // the two units' Assembly-CSharp differ only in Burst codegen (see the header).
  string dir = Path.Combine(repoRoot, "packages", "7dtd.assemblies.dedicatedserver", packageVersion,
    "7DaysToDieServer_Data", "Managed");
  return Directory.Exists(dir) ? dir : null;
}

/// <summary>
///   The side-relevant facts read out of the game's own assembly, so the classifier's rules are the game's
///   rules rather than community lore. Read once and shared by every mod classified in a run.
/// </summary>
sealed class GameSideFacts {
  /// <summary>XML patch entry points by name, carrying the flags that decide whether a patch reaches clients.</summary>
  public Dictionary<string, EntryPointFact> EntryPoints = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>Game types whose instances only ever exist on a client. Derivation: see <see cref="Read" />.</summary>
  public HashSet<string> ClientOnlyTypes = new(StringComparer.Ordinal);

  /// <summary>Config names that set <c>Mod.GameConfigMod</c>, read from <c>Mod.DetectContents</c>.</summary>
  public HashSet<string> GameConfigModTriggers = new(StringComparer.OrdinalIgnoreCase);

  public static GameSideFacts Read(string managedDir) {
    var facts = new GameSideFacts();
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(managedDir);
    AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
      Path.Combine(managedDir, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
    ModuleDefinition module = assembly.MainModule;

    // xmlsToLoad, from WorldStaticData's initializer. Same IL-not-reflection reasoning as
    // Tests/Fixtures/EntryPoints.cs: the type's initializer drags in Unity types that cannot load headlessly.
    // Each entry is `newobj XmlLoadInfo(string name, bool loadAtStartup, bool sendToClients, ...)`, so the
    // first ldstr since the previous entry is the name and the first two following ldc.i4 are the flags.
    MethodDefinition initializer = module.GetType("WorldStaticData").Methods.First(m => m.Name == ".cctor");
    string pendingName = null;
    var pendingFlags = new List<int>();
    foreach (Instruction instruction in initializer.Body.Instructions) {
      if (instruction.OpCode == OpCodes.Ldstr && pendingName == null) {
        pendingName = (string)instruction.Operand;
      } else if (pendingName != null && TryReadInt32Constant(instruction, out int flag)) {
        pendingFlags.Add(flag);
      } else if (instruction.OpCode == OpCodes.Newobj &&
                 ((MethodReference)instruction.Operand).DeclaringType.Name.Contains("XmlLoadInfo")) {
        if (pendingName != null && pendingFlags.Count >= 2) {
          facts.EntryPoints[pendingName] = new EntryPointFact {
            Name = pendingName, LoadAtStartup = pendingFlags[0] != 0, SendToClients = pendingFlags[1] != 0
          };
        }

        pendingName = null;
        pendingFlags.Clear();
      }
    }

    foreach (Instruction instruction in module.GetType("Mod").Methods
               .First(m => m.Name == "DetectContents").Body.Instructions) {
      if (instruction.OpCode == OpCodes.Ldstr) {
        facts.GameConfigModTriggers.Add((string)instruction.Operand);
      }
    }

    // Derived marker set: everything in the XUi hierarchy. 505 types on V3.1.0-b14 — the game's whole UI layer,
    // which only ever instantiates inside a LocalPlayerUI.
    foreach (TypeDefinition type in module.GetTypes()) {
      if (type.Name.StartsWith("XUi", StringComparison.Ordinal) || InheritsXui(type)) {
        facts.ClientOnlyTypes.Add(type.FullName);
      }
    }

    // Curated additions, one justification each. Kept deliberately short: every name here is a claim, and an
    // over-eager marker set is how a server-side mod gets mislabelled.
    foreach (string name in new[] {
               "EntityPlayerLocal",   // the local player; WorldBase.GetLocalPlayerFromID returns null on a server
               "LocalPlayerUI",       // the per-local-player UI root
               "GUIWindowManager",    // legacy IMGUI window stack
               "PlayerMoveController" // local input -> movement
             }) {
      if (module.GetType(name) != null) {
        facts.ClientOnlyTypes.Add(name);
      }
    }

    return facts;
  }

  private static bool InheritsXui(TypeDefinition type) {
    TypeReference baseType = type.BaseType;
    for (var depth = 0; baseType != null && depth < 20; depth++) {
      if (baseType.Name is "XUiController" or "XUiView") {
        return true;
      }

      baseType = SafeResolve(baseType)?.BaseType;
    }

    return false;
  }

  private static TypeDefinition SafeResolve(TypeReference reference) {
    try {
      return reference.Resolve();
    } catch {
      return null;
    }
  }

  private static bool TryReadInt32Constant(Instruction instruction, out int value) {
    value = 0;
    string code = instruction.OpCode.Code.ToString();
    if (code is "Ldc_I4" or "Ldc_I4_S") {
      value = Convert.ToInt32(instruction.Operand);
      return true;
    }

    if (code.StartsWith("Ldc_I4_", StringComparison.Ordinal) && int.TryParse(code[7..], out value)) {
      return true;
    }

    return false;
  }

  public sealed class EntryPointFact {
    public bool LoadAtStartup;
    public string Name;
    public bool SendToClients;
  }
}

/// <summary>
///   A mod's shipped artifacts, read from a staged or deployed mod folder — the same bytes the game would see.
/// </summary>
sealed class ModArtifactCensus {
  // Directories the game actually reads content out of. A loose .png at the mod root is a store screenshot,
  // not an asset — counting those would flag half of Nexus as client-side.
  private static readonly string[] AssetRoots = { "Config", "Resources", "UIAtlases", "ItemIcons" };

  private static readonly string[] AssetExtensions = {
    ".unity3d", ".png", ".jpg", ".jpeg", ".dds", ".tga", ".ogg", ".wav", ".mp3", ".mp4", ".ttf", ".otf"
  };

  public List<string> AssetFiles = new();
  public List<string> ConfigFiles = new();
  public List<string> Dlls = new();
  public string ModName;
  public string Root;
  public bool ServerOnlyClassDeclared;
  public bool SkipWithAntiCheat;
  public List<string> WorldFiles = new();

  public static ModArtifactCensus Read(string modDir) {
    var census = new ModArtifactCensus { Root = modDir, ModName = NameFromFolder(modDir) };
    foreach (string file in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)) {
      string relative = Path.GetRelativePath(modDir, file).Replace('\\', '/');
      string extension = Path.GetExtension(file).ToLowerInvariant();
      string topSegment = relative.Split('/')[0];

      if (extension == ".dll" && !relative.Contains('/')) {
        census.Dlls.Add(relative);
      } else if (relative.StartsWith("Config/", StringComparison.OrdinalIgnoreCase)) {
        census.ConfigFiles.Add(relative["Config/".Length..]);
      } else if (topSegment is "Prefabs" or "Worlds") {
        census.WorldFiles.Add(relative);
      }

      if (AssetExtensions.Contains(extension) && AssetRoots.Contains(topSegment)) {
        census.AssetFiles.Add(relative);
      }
    }

    string modInfo = Path.Combine(modDir, "ModInfo.xml");
    if (File.Exists(modInfo)) {
      string text = File.ReadAllText(modInfo);
      census.SkipWithAntiCheat = Regex.IsMatch(text,
        @"<SkipWithAntiCheat\s+value\s*=\s*""\s*true\s*""", RegexOptions.IgnoreCase);
      census.ModName = Regex.Match(text, @"<Name\s+value\s*=\s*""([^""]*)""").Groups[1].Value is { Length: > 0 } n
        ? n
        : census.ModName;
    }

    // StrongMods' own blocks.xml extension: an author declaring "this block class is server-side; clients
    // ignore it" (StrongMods/ServerOnlyClass.cs rewrites it into Class in a BlocksFromXml.CreateBlock prefix).
    // A declaration beats any inference the rest of this tool can make about a block-class DLL.
    string blocks = Path.Combine(modDir, "Config", "blocks.xml");
    census.ServerOnlyClassDeclared = File.Exists(blocks) && File.ReadAllText(blocks).Contains("ServerOnlyClass");
    return census;
  }

  // A staged folder is <Mod>/bin/Debug, a deployed one is Mods/<prefix->Mod; either way the mod's own name is
  // the nearest folder that is not a build directory.
  private static string NameFromFolder(string modDir) {
    var dir = new DirectoryInfo(modDir);
    while (dir?.Parent != null && dir.Name is "Debug" or "Release" or "bin") {
      dir = dir.Parent;
    }

    return dir?.Name ?? modDir;
  }
}

/// <summary>What a mod's own assemblies patch, call and declare — read with Cecil, never loaded.</summary>
sealed class ModDllFacts {
  public List<string> ClientMarkerHits = new();
  public List<string> HarmonyTargets = new();
  public bool HasModApi;
  public List<string> ProgrammaticTargets = new();
  public bool ReadFailed;
  public List<string> XmlInstantiatedClasses = new();

  public static ModDllFacts Read(ModArtifactCensus census, string managedDir, GameSideFacts facts) {
    var dllFacts = new ModDllFacts();
    foreach (string dll in census.Dlls) {
      var resolver = new DefaultAssemblyResolver();
      resolver.AddSearchDirectory(managedDir);
      resolver.AddSearchDirectory(Path.Combine(managedDir, "..", "..", "Mods", "0_TFP_Harmony"));
      resolver.AddSearchDirectory(census.Root);
      AssemblyDefinition assembly;
      try {
        assembly = AssemblyDefinition.ReadAssembly(Path.Combine(census.Root, dll),
          new ReaderParameters { AssemblyResolver = resolver });
      } catch {
        dllFacts.ReadFailed = true;
        continue;
      }

      foreach (TypeDefinition type in assembly.MainModule.GetTypes()) {
        if (type.Interfaces.Any(i => i.InterfaceType.Name == "IModApi")) {
          dllFacts.HasModApi = true;
        }

        // A DLL type the game instantiates from XML (a Block/Item/TileEntity subclass) is reachable wherever
        // that XML is loaded — including the client, since blocks.xml is SendToClients. Recorded as a shape,
        // not a verdict.
        if (BaseChainHits(type, "Block", "ItemAction", "ItemClass", "TileEntity", "EntityAlive")) {
          dllFacts.XmlInstantiatedClasses.Add(type.FullName);
        }

        CollectHarmony(type, type.CustomAttributes, type.FullName, dllFacts);
        foreach (MethodDefinition method in type.Methods) {
          CollectHarmony(type, method.CustomAttributes, $"{type.FullName}.{method.Name}", dllFacts);
          if (method.Name is "TargetMethod" or "TargetMethods" ||
              method.CustomAttributes.Any(a => a.AttributeType.Name.StartsWith("HarmonyTargetMethod")) ||
              method.CustomAttributes.Any(a => a.AttributeType.Name == "PatchTargetManifestAttribute")) {
            dllFacts.ProgrammaticTargets.Add($"{type.FullName}.{method.Name}");
          }

          if (!method.HasBody) {
            continue;
          }

          foreach (Instruction instruction in method.Body.Instructions) {
            if (instruction.Operand is MethodReference called) {
              if (facts.ClientOnlyTypes.Contains(called.DeclaringType.FullName)) {
                dllFacts.ClientMarkerHits.Add($"call {called.DeclaringType.Name}::{called.Name} " +
                                              $"in {type.Name}.{method.Name}");
              } else if (facts.ClientOnlyTypes.Contains(called.ReturnType.FullName)) {
                // Weaker than calling INTO a client type: obtaining one from a shared API is exactly what a
                // null-tolerant listen-server branch looks like. Recorded so review can tell the two apart.
                dllFacts.ClientMarkerHits.Add($"obtains {called.ReturnType.Name} from " +
                                              $"{called.DeclaringType.Name}::{called.Name} " +
                                              $"in {type.Name}.{method.Name}");
              }
            }

            if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string literal &&
                facts.ClientOnlyTypes.Contains(literal)) {
              dllFacts.ClientMarkerHits.Add($"string type name \"{literal}\" in {type.Name}.{method.Name}");
            }
          }
        }
      }
    }

    return dllFacts;
  }

  private static void CollectHarmony(TypeDefinition owner, IEnumerable<CustomAttribute> attributes, string origin,
    ModDllFacts dllFacts) {
    foreach (CustomAttribute attribute in attributes.Where(a => a.AttributeType.Name == "HarmonyPatch")) {
      CustomAttributeArgument[] arguments;
      try {
        arguments = attribute.ConstructorArguments.ToArray();
      } catch {
        continue;
      }

      string target = arguments.Select(a => a.Value is TypeReference t ? t.FullName : a.Value as string)
        .FirstOrDefault(s => !string.IsNullOrEmpty(s));
      if (target != null) {
        dllFacts.HarmonyTargets.Add($"{target} ({origin})");
      }
    }
  }

  private static bool BaseChainHits(TypeDefinition type, params string[] names) {
    TypeReference baseType = type.BaseType;
    for (var depth = 0; baseType != null && depth < 20; depth++) {
      if (names.Contains(baseType.Name)) {
        return true;
      }

      try {
        baseType = baseType.Resolve()?.BaseType;
      } catch {
        return false;
      }
    }

    return false;
  }
}

/// <summary>The verdict for one mod, with the evidence that produced it and the caveats that limit it.</summary>
sealed class SideVerdict {
  public List<string> Caveats = new();
  public string Confidence = "likely";
  public string Deciding = "";
  public string EacOff = "no";
  public List<string> Evidence = new();
  public string ModName;
  public string Side = "unknown";
}

/// <summary>
///   Applies the side rules to one mod. Two independent needs are decided first — does the SERVER need this
///   installed, does the CLIENT — and the side is their combination, because "both" is a conjunction, not a
///   third thing to detect.
/// </summary>
static class ModSideClassifier {
  // SendToClients is false for these, and unlike rwgmixer/spawning/gamestages/signs the CLIENT is what consumes
  // them — so a mod patching one must be installed client-side for the patch to have any effect.
  private static readonly string[] ClientConsumedUnsentEntryPoints = { "loadingscreen", "subtitles", "videos" };

  public static SideVerdict Classify(string modDir, GameSideFacts facts, string managedDir) {
    ModArtifactCensus census = ModArtifactCensus.Read(modDir);
    ModDllFacts dllFacts = ModDllFacts.Read(census, managedDir, facts);
    var verdict = new SideVerdict { ModName = census.ModName };

    var serverNeed = false;
    var clientNeed = false;

    var patchedEntryPoints = census.ConfigFiles
      .Select(f => Path.ChangeExtension(f, null))
      .Where(n => facts.EntryPoints.ContainsKey(n)).ToList();
    if (patchedEntryPoints.Count > 0) {
      serverNeed = true;
      verdict.Evidence.Add($"patches {patchedEntryPoints.Count} XML entry point(s): " +
                           string.Join(", ", patchedEntryPoints));
      verdict.Deciding = "Config XML (server-distributed)";
    }

    if (census.WorldFiles.Count > 0) {
      serverNeed = true;
      verdict.Evidence.Add($"{census.WorldFiles.Count} world/prefab file(s) — worldgen and POI placement are " +
                           "server-side");
      verdict.Caveats.Add("world/prefab content: whether a client needs local prefab files (distant imposters, " +
                          "dynamic mesh) is not decidable from the mod's artifacts");
    }

    if (census.Dlls.Count > 0) {
      verdict.Evidence.Add("ships " + string.Join(", ", census.Dlls));
      if (dllFacts.HasModApi) {
        // IModApi.InitMod runs on whichever unit loads the mod, and the dedicated server loads every mod DLL
        // it is given (#77, clause 2a). So an entry point is server-need on its own; only
        // client-marked targets can additionally pull the mod client-side.
        serverNeed = true;
        verdict.Evidence.Add("implements IModApi — a runtime entry point the server executes");
      }
    }

    foreach (string target in dllFacts.HarmonyTargets) {
      string typeName = target.Split(' ')[0];
      if (facts.ClientOnlyTypes.Contains(typeName)) {
        clientNeed = true;
        verdict.Evidence.Add($"Harmony target is a client-only type: {target}");
        verdict.Deciding = "client-only Harmony target";
      } else {
        serverNeed = true;
      }
    }

    if (census.Dlls.Count > 0 && dllFacts.HarmonyTargets.Count == 0 && !dllFacts.HasModApi &&
        dllFacts.XmlInstantiatedClasses.Count > 0) {
      serverNeed = true;
      verdict.Evidence.Add("DLL supplies XML-instantiated game class(es): " +
                           string.Join(", ", dllFacts.XmlInstantiatedClasses));
    }

    var menuConfig = census.ConfigFiles.Where(f => f.StartsWith("XUi_Menu/", StringComparison.OrdinalIgnoreCase))
      .ToList();
    if (menuConfig.Count > 0) {
      clientNeed = true;
      verdict.Evidence.Add($"Config/XUi_Menu/** ({menuConfig.Count} file(s)) — menu XUi is not an xmlsToLoad " +
                           "entry point and is never sent to clients");
      verdict.Deciding = "Config/XUi_Menu (never sent)";
      verdict.Confidence = "certain";
    }

    var unsent = patchedEntryPoints.Where(n => ClientConsumedUnsentEntryPoints.Contains(n)).ToList();
    if (unsent.Count > 0) {
      clientNeed = true;
      verdict.Evidence.Add("patches client-consumed entry point(s) with SendToClients=false: " +
                           string.Join(", ", unsent));
      verdict.Deciding = "SendToClients=false, client-consumed";
    }

    if (census.AssetFiles.Count > 0) {
      clientNeed = true;
      verdict.Evidence.Add($"{census.AssetFiles.Count} asset file(s) in a game-read directory — assets are " +
                           "never transferred: " + string.Join(", ", census.AssetFiles.Take(4)));
      verdict.Deciding = "unsynced asset content";
    }

    if (dllFacts.ClientMarkerHits.Count > 0) {
      verdict.Evidence.Add("calls client-only API: " + string.Join("; ", dllFacts.ClientMarkerHits.Take(4)));
      verdict.Caveats.Add("calls a client-only API but that alone does not prove client-need — a null-tolerant " +
                          "listen-server path looks identical in IL (" + dllFacts.ClientMarkerHits[0] + ")");
    }

    if (census.ServerOnlyClassDeclared) {
      serverNeed = true;
      verdict.Evidence.Add("blocks.xml declares ServerOnlyClass — the author's own statement that the block " +
                           "class is server-side and clients ignore it (StrongMods extension)");
      verdict.Deciding = "ServerOnlyClass declaration";
      verdict.Confidence = "certain";
    }

    if (dllFacts.ProgrammaticTargets.Count > 0) {
      verdict.Caveats.Add("programmatic Harmony targets are invisible statically: " +
                          string.Join(", ", dllFacts.ProgrammaticTargets.Take(3)));
    }

    if (dllFacts.XmlInstantiatedClasses.Count > 0 && !census.ServerOnlyClassDeclared) {
      verdict.Caveats.Add("DLL supplies a class the game instantiates from XML (" +
                          string.Join(", ", dllFacts.XmlInstantiatedClasses.Take(2)) +
                          ") and the XML does not declare ServerOnlyClass — the client parses the same XML");
    }

    if (dllFacts.ReadFailed) {
      verdict.Caveats.Add("at least one DLL could not be read with the current resolver");
    }

    var xuiInGame = patchedEntryPoints.Where(n => n.StartsWith("XUi", StringComparison.OrdinalIgnoreCase)).ToList();
    if (xuiInGame.Count > 0) {
      verdict.Caveats.Add("patches in-game XUi (" + string.Join(", ", xuiInGame) + ") which IS SendToClients; " +
                          "whether the client rebuilds its UI from server-sent XUi XML is a runtime question");
    }

    verdict.Side = (serverNeed, clientNeed) switch {
      (true, true) => "both",
      (true, false) => "server",
      (false, true) => "client",
      _ => "unknown"
    };
    if (verdict.Side == "unknown") {
      verdict.Deciding = "no side-bearing content found";
      verdict.Confidence = "none";
    } else if (verdict.Deciding.Length == 0) {
      verdict.Deciding = "DLL patch targets";
    }

    if (verdict.Caveats.Count > 0 && verdict.Confidence != "certain") {
      verdict.Confidence = "low";
    }

    // The #77 rule, applied: EAC-off is required exactly when a .dll lands in the CLIENT's Mods folder. Note
    // what that makes this column — a statement about the mod INSTALLED AS ITS SIDE SAYS. Deploy a server-side
    // DLL mod into a client's Mods folder anyway and the client still needs EAC off; the mod did not stop
    // being server-side, the operator put a DLL somewhere the mod does not require it.
    bool dllOnClient = census.Dlls.Count > 0 && verdict.Side is "client" or "both";
    verdict.EacOff = census.Dlls.Count == 0 ? "no - no .dll at all"
      : !dllOnClient ? "no - no client-side .dll"
      : census.SkipWithAntiCheat ? "no - SkipWithAntiCheat drops it on EAC clients"
      : "YES - client-side .dll";
    return verdict;
  }
}

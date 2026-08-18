// apidrift — measures the game-API drift surface of every compiled mod DLL (#50 gap S2).
//
// Two passes, both pure BCL System.Reflection.Metadata (no assembly loading, no game deps):
//
//   1. CENSUS — every MemberRef/TypeRef a mod DLL carries into a game assembly. These are emitted by
//      compiling against the game's own assemblies (ADR-0002), and CI recompiles every mod against BOTH
//      units on every push (#21) — so this whole slice is already drift-guarded by compilation itself.
//      The census exists to size that covered surface honestly, not to re-guard it.
//
//   2. STRING-LOOKUP SCAN — the residue compilation cannot see: members resolved BY NAME at runtime.
//      The scanner decodes each method body's IL (operand sizes from System.Reflection.Emit.OpCodes,
//      the Tests/Fixtures/IlReader.cs precedent) and records every call to a known lookup API
//      (AccessTools.Method/Field/Property/TypeByName/Declared*, Type.GetMethod/GetField/GetProperty/
//      GetType, Traverse.Create/Method/Field/Property, Assembly.GetType) together with the nearest
//      preceding ldstr (the name) and ldtoken (the subject type). Each site whose subject is a game
//      type is then resolved against every restored tree under packages/ (both units x both declared
//      versions); a site whose subject type is dynamic is checked as "declared by N types in
//      Assembly-CSharp". IL cannot distinguish nameof(X.Y) from a literal "Y" — nameof sites appear
//      here too and are ADDITIONALLY compile-guarded; the report notes which ones those are.
//
// Known limitations (prototype-honest):
//   * Name-only resolution — a signature change with the name kept (e.g. ProcessPackage gaining a
//     parameter) passes the check. Overload counts are reported so a reviewer can spot risk.
//   * The ldstr/ldtoken pairing is a linear window heuristic, not dataflow. Names built at runtime
//     (concatenation, config data) surface as <dynamic-name> and cannot be resolved statically —
//     StrongMods' <function> XML dispatch is the known case.
//
// Usage, from the repo root:
//   dotnet run StrongDev/.ai/tools/apidrift.cs                 scan every mod DLL in */bin/Debug
//   dotnet run StrongDev/.ai/tools/apidrift.cs -- --selftest   run the built-in cases
//   dotnet run StrongDev/.ai/tools/apidrift.cs -- <ModName>    scan one mod
// Exit code: 1 when any game-subject site fails to resolve in any tree (or on selftest failure).
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

var arguments = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.EndsWith(".cs")).ToArray();
return arguments.Contains("--selftest")
  ? SelfTest.Run()
  : ApiDrift.Run(Environment.CurrentDirectory, arguments.FirstOrDefault(a => !a.StartsWith("--")));

internal sealed record LookupSite(string Mod, string Caller, string Api, string SubjectType, string SubjectAssembly,
                                  string MemberName);

internal sealed record CensusRow(string Mod, int GameMethodRefs, int GameFieldRefs, int GameTypeRefs,
                                 int HarmonyMemberRefs, int BclMemberRefs, int OtherMemberRefs);

// One decoded IL instruction, reduced to what the site extractor needs.
internal sealed record Instr(string Kind, string A = "", string B = "", string C = "");
// Kinds: "ldstr" (A=value) · "ldtoken-type" (A=fullname, B=assembly) · "call" (A=declaring type fullname,
// B=method name, C=declaring assembly) · "other".

internal static class ApiDrift {
  public static int Run(string root, string onlyMod) {
    var trees = Trees.Discover(root);
    if (trees.Count == 0) {
      Console.Error.WriteLine("apidrift: no game trees under packages/ — run the restore documented in AGENTS.md.");
      return 2;
    }

    var gameAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var tree in trees) {
      foreach (var dll in Directory.GetFiles(tree.ManagedDir, "*.dll")) {
        gameAssemblyNames.Add(Path.GetFileNameWithoutExtension(dll));
      }
    }

    var mods = Directory.GetDirectories(root)
      .Select(d => new { Dir = d, Name = Path.GetFileName(d) })
      .Where(m => File.Exists(Path.Combine(m.Dir, "bin", "Debug", m.Name + ".dll")))
      .Where(m => onlyMod == null || string.Equals(m.Name, onlyMod, StringComparison.OrdinalIgnoreCase))
      .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (mods.Count == 0) {
      Console.Error.WriteLine($"apidrift: no mod DLLs found{(onlyMod == null ? "" : $" for '{onlyMod}'")}.");
      return 2;
    }

    var census = new List<CensusRow>();
    var sites = new List<LookupSite>();
    foreach (var mod in mods) {
      var dll = Path.Combine(mod.Dir, "bin", "Debug", mod.Name + ".dll");
      using var stream = File.OpenRead(dll);
      using var pe = new PEReader(stream);
      var md = pe.GetMetadataReader();
      census.Add(Census.Count(mod.Name, md, gameAssemblyNames));
      sites.AddRange(Scanner.Scan(mod.Name, pe, md));
    }

    Console.WriteLine("== Census: memberrefs into game assemblies (compile-guarded by CI, #21) ==");
    Console.WriteLine($"{"Mod",-24} {"game m",7} {"game f",7} {"game T",7} {"Harmony",8} {"BCL",6} {"other",6}");
    foreach (var row in census) {
      Console.WriteLine($"{row.Mod,-24} {row.GameMethodRefs,7} {row.GameFieldRefs,7} {row.GameTypeRefs,7} " +
                        $"{row.HarmonyMemberRefs,8} {row.BclMemberRefs,6} {row.OtherMemberRefs,6}");
    }

    Console.WriteLine($"{"TOTAL",-24} {census.Sum(r => r.GameMethodRefs),7} {census.Sum(r => r.GameFieldRefs),7} " +
                      $"{census.Sum(r => r.GameTypeRefs),7} {census.Sum(r => r.HarmonyMemberRefs),8} " +
                      $"{census.Sum(r => r.BclMemberRefs),6} {census.Sum(r => r.OtherMemberRefs),6}");

    Console.WriteLine();
    Console.WriteLine("== Runtime string-lookup sites (the residue compilation cannot guard) ==");
    var failures = 0;
    if (sites.Count == 0) {
      Console.WriteLine("none found");
    }

    foreach (var site in sites) {
      var subject = site.SubjectType == "" ? "<dynamic-subject>" : Display(site.SubjectType);
      var name = site.MemberName == "" ? "<dynamic-name>" : $"\"{site.MemberName}\"";
      Console.WriteLine($"[{site.Mod}] {site.Caller}");
      Console.WriteLine($"    {site.Api}({subject}, {name})  subject-assembly: {site.SubjectAssembly}");
      var isGameSubject = gameAssemblyNames.Contains(site.SubjectAssembly);
      if (site.SubjectAssembly != "" && !isGameSubject) {
        Console.WriteLine("    -> mod/BCL-internal subject: not game-drift surface, skipped");
        continue;
      }

      if (site.MemberName == "") {
        Console.WriteLine("    -> name is runtime data: statically unresolvable (data-driven dispatch)");
        continue;
      }

      foreach (var tree in trees) {
        string verdict;
        if (site.Api == "AccessTools.TypeByName" || site.Api == "Type.GetType" || site.Api == "Assembly.GetType") {
          var found = tree.TypeExists(site.MemberName);
          verdict = found ? "type resolves" : "TYPE NOT FOUND";
          if (!found) failures++;
        } else if (site.SubjectType == "") {
          var declaring = tree.CountDeclaringTypes(site.MemberName);
          verdict = declaring > 0
            ? $"declared by {declaring} type(s) in Assembly-CSharp (dynamic subject: heuristic)"
            : "NO TYPE in Assembly-CSharp declares this member";
          if (declaring == 0) failures++;
        } else {
          var result = tree.ResolveMember(site.SubjectType, site.SubjectAssembly, site.MemberName,
            site.Api.Contains("Declared"));
          verdict = result;
          if (result.StartsWith("MISSING") || result.StartsWith("TYPE NOT FOUND")) failures++;
        }

        Console.WriteLine($"    {tree.Label,-28} {verdict}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? "RESULT: every statically-resolvable string lookup resolves in every tree."
      : $"RESULT: {failures} tree-resolution FAILURE(S) — see above.");
    return failures == 0 ? 0 : 1;
  }

  internal static string Display(string metadataFullName) => metadataFullName.Replace('/', '+');
}

// ---------------------------------------------------------------------------------------------------------
// Census — classify every MemberRef / TypeRef by the assembly it points into.
// ---------------------------------------------------------------------------------------------------------
internal static class Census {
  // NOTE: mscorlib/System.*/netstandard physically live in the game's Managed dir (FrameworkPathOverride,
  // ADR-0002) — they must classify as BCL FIRST or every string.Format call counts as "game API".
  // UnityEngine.* deliberately counts as game: a game update can bump Unity and move those APIs.
  public static CensusRow Count(string mod, MetadataReader md, HashSet<string> gameAssemblies) {
    int gameM = 0, gameF = 0, harmony = 0, bcl = 0, other = 0, unknown = 0;
    foreach (var handle in md.MemberReferences) {
      var member = md.GetMemberReference(handle);
      var assembly = ParentAssembly(md, member.Parent);
      var isField = md.GetBlobReader(member.Signature).ReadByte() == 0x6; // FIELD signature header
      if (assembly == "") {
        unknown++; // parent TypeSpec this tool's minimal decoder cannot name (arrays, nested generics)
      } else if (IsBcl(assembly)) {
        bcl++;
      } else if (gameAssemblies.Contains(assembly)) {
        if (isField) gameF++; else gameM++;
      } else if (assembly.Equals("0Harmony", StringComparison.OrdinalIgnoreCase)) {
        harmony++;
      } else {
        other++;
      }
    }

    var gameT = 0;
    foreach (var handle in md.TypeReferences) {
      var scope = ScopeAssembly(md, md.GetTypeReference(handle).ResolutionScope);
      if (!IsBcl(scope) && gameAssemblies.Contains(scope)) {
        gameT++;
      }
    }

    return new CensusRow(mod, gameM, gameF, gameT, harmony, bcl, other + unknown);
  }

  public static bool IsBcl(string assembly) =>
    assembly is "mscorlib" or "netstandard" or "System" || assembly.StartsWith("System.");

  private static string ParentAssembly(MetadataReader md, EntityHandle parent) {
    switch (parent.Kind) {
      case HandleKind.TypeReference:
        return ScopeAssembly(md, md.GetTypeReference((TypeReferenceHandle)parent).ResolutionScope);
      case HandleKind.TypeDefinition:
      case HandleKind.MethodDefinition:
        return md.GetString(md.GetAssemblyDefinition().Name);
      case HandleKind.TypeSpecification: {
        var (_, assembly) = Tokens.DecodeTypeSpec(md, (TypeSpecificationHandle)parent);
        return assembly;
      }
      default:
        return "";
    }
  }

  internal static string ScopeAssembly(MetadataReader md, EntityHandle scope) {
    switch (scope.Kind) {
      case HandleKind.AssemblyReference:
        return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
      case HandleKind.TypeReference: // nested type: walk to the outermost scope
        return ScopeAssembly(md, md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope);
      case HandleKind.ModuleDefinition:
        return md.GetString(md.GetAssemblyDefinition().Name);
      default:
        return "";
    }
  }
}

// ---------------------------------------------------------------------------------------------------------
// Scanner — decode IL, extract lookup sites.
// ---------------------------------------------------------------------------------------------------------
internal static class Scanner {
  // (declaring type fullname, method name) -> true when the FIRST ldstr is a type name, not a member name.
  private static readonly HashSet<string> LookupApis = new() {
    "HarmonyLib.AccessTools|Method", "HarmonyLib.AccessTools|DeclaredMethod",
    "HarmonyLib.AccessTools|Field", "HarmonyLib.AccessTools|DeclaredField",
    "HarmonyLib.AccessTools|Property", "HarmonyLib.AccessTools|DeclaredProperty",
    "HarmonyLib.AccessTools|PropertyGetter", "HarmonyLib.AccessTools|PropertySetter",
    "HarmonyLib.AccessTools|TypeByName",
    "HarmonyLib.Traverse|Method", "HarmonyLib.Traverse|Field", "HarmonyLib.Traverse|Property",
    "System.Type|GetMethod", "System.Type|GetField", "System.Type|GetProperty", "System.Type|GetEvent",
    "System.Type|GetMember", "System.Type|GetNestedType", "System.Type|GetType",
    "System.Reflection.Assembly|GetType",
  };

  public static List<LookupSite> Scan(string mod, PEReader pe, MetadataReader md) {
    var sites = new List<LookupSite>();
    foreach (var handle in md.MethodDefinitions) {
      var method = md.GetMethodDefinition(handle);
      if (method.RelativeVirtualAddress == 0) {
        continue;
      }

      var declaring = md.GetTypeDefinition(method.GetDeclaringType());
      var caller = $"{Tokens.TypeDefFullName(md, method.GetDeclaringType())}.{md.GetString(method.Name)}";
      var body = pe.GetMethodBody(method.RelativeVirtualAddress);
      var instructions = Decode(md, body.GetILBytes()!);
      sites.AddRange(ExtractSites(mod, ApiDrift.Display(caller), instructions));
      _ = declaring;
    }

    return sites;
  }

  /// <summary>Linear decode of one method body into the reduced Instr stream. Pure BCL.</summary>
  public static List<Instr> Decode(MetadataReader md, byte[] il) {
    var result = new List<Instr>();
    var position = 0;
    while (position < il.Length) {
      var opcode = Il.ReadOpCode(il, ref position);
      var operandStart = position;
      Il.SkipOperand(opcode, il, ref position);
      if (opcode == OpCodes.Ldstr) {
        var token = BitConverter.ToInt32(il, operandStart);
        result.Add(new Instr("ldstr", md.GetUserString(MetadataTokens.UserStringHandle(token))));
      } else if (opcode == OpCodes.Ldtoken) {
        var token = BitConverter.ToInt32(il, operandStart);
        var (name, assembly) = Tokens.TypeFromToken(md, token);
        result.Add(name == "" ? new Instr("other") : new Instr("ldtoken-type", name, assembly));
      } else if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj) {
        var token = BitConverter.ToInt32(il, operandStart);
        var (type, name, assembly) = Tokens.MethodFromToken(md, token);
        result.Add(new Instr("call", type, name, assembly));
      } else if (opcode.Name!.StartsWith("stelem")) {
        result.Add(new Instr("stelem"));
      } else {
        result.Add(new Instr("other"));
      }
    }

    return result;
  }

  /// <summary>
  ///   The pairing heuristic: at each call to a lookup API, take the nearest preceding ldstr within the
  ///   window as the name, and the most recent NOT-YET-CONSUMED ldtoken-type as the subject. Types are
  ///   kept on a small stack and popped by stelem — a typeof(X) stored into an argument array (the
  ///   AccessTools.Method(type, name, new[]{typeof(A), typeof(B)}) shape) is an argument type, never the
  ///   subject. GetTypeFromHandle calls do not break the window (typeof(X) compiles to ldtoken + that
  ///   call); any other call does, because its arguments consumed whatever was loaded before it.
  /// </summary>
  public static List<LookupSite> ExtractSites(string mod, string caller, List<Instr> instructions) {
    var sites = new List<LookupSite>();
    string lastString = null;
    var typeStack = new List<(string Name, string Assembly)>();
    string lastTypeName = null, lastTypeAssembly = null;

    void SyncTop() {
      lastTypeName = typeStack.Count > 0 ? typeStack[^1].Name : null;
      lastTypeAssembly = typeStack.Count > 0 ? typeStack[^1].Assembly : null;
    }

    foreach (var instr in instructions) {
      switch (instr.Kind) {
        case "ldstr":
          lastString = instr.A;
          break;
        case "ldtoken-type":
          typeStack.Add((instr.A, instr.B));
          SyncTop();
          break;
        case "stelem":
          if (typeStack.Count > 0) {
            typeStack.RemoveAt(typeStack.Count - 1);
          }

          SyncTop();
          break;
        case "call": {
          if (LookupApis.Contains($"{instr.A}|{instr.B}")) {
            var api = $"{instr.A.Substring(instr.A.LastIndexOf('.') + 1)}.{instr.B}";
            var isStaticTypeLookup = instr.B == "GetType" || instr.B == "TypeByName";
            // Colon form AccessTools.Method("Type:Member") — split it into subject + member.
            var subjectType = lastTypeName ?? "";
            var subjectAssembly = lastTypeAssembly ?? "";
            var memberName = lastString ?? "";
            if (!isStaticTypeLookup && subjectType == "" && memberName.Contains(':')) {
              var parts = memberName.Split(':', 2);
              subjectType = parts[0];
              memberName = parts[1];
            }

            sites.Add(new LookupSite(mod, caller, api, isStaticTypeLookup ? "" : subjectType,
              isStaticTypeLookup ? "" : subjectAssembly, memberName));
          }

          // typeof(X) is ldtoken + GetTypeFromHandle: keep the window alive through it.
          if (!(instr.A == "System.Type" && instr.B == "GetTypeFromHandle")) {
            lastString = null;
            typeStack.Clear();
            SyncTop();
          }

          break;
        }
      }
    }

    return sites;
  }
}

// ---------------------------------------------------------------------------------------------------------
// Il — opcode table + operand skipping, byte-level. The IlReader.cs approach without runtime loading.
// ---------------------------------------------------------------------------------------------------------
internal static class Il {
  private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.FieldType == typeof(OpCode))
    .Select(f => (OpCode)f.GetValue(null)!)
    .ToDictionary(o => o.Value);

  public static OpCode ReadOpCode(byte[] il, ref int position) {
    var first = il[position++];
    return first != 0xFE ? Opcodes[first] : Opcodes[(short)(0xFE00 | il[position++])];
  }

  public static void SkipOperand(OpCode opcode, byte[] il, ref int position) {
    switch (opcode.OperandType) {
      case OperandType.InlineNone:
        return;
      case OperandType.ShortInlineBrTarget:
      case OperandType.ShortInlineI:
      case OperandType.ShortInlineVar:
        position += 1;
        return;
      case OperandType.InlineVar:
        position += 2;
        return;
      case OperandType.InlineI8:
      case OperandType.InlineR:
        position += 8;
        return;
      case OperandType.InlineSwitch: {
        var count = BitConverter.ToInt32(il, position);
        position += 4 + count * 4;
        return;
      }
      default: // InlineBrTarget, InlineI, ShortInlineR, InlineString, InlineField, InlineMethod, InlineType, InlineTok, InlineSig
        position += 4;
        return;
    }
  }
}

// ---------------------------------------------------------------------------------------------------------
// Tokens — resolve metadata tokens to names using SRM only.
// ---------------------------------------------------------------------------------------------------------
internal static class Tokens {
  /// <summary>Type fullname (metadata form, '/' for nesting) + assembly, from an inline token.</summary>
  public static (string Name, string Assembly) TypeFromToken(MetadataReader md, int token) {
    var handle = MetadataTokens.EntityHandle(token);
    switch (handle.Kind) {
      case HandleKind.TypeDefinition:
        return (TypeDefFullName(md, (TypeDefinitionHandle)handle), md.GetString(md.GetAssemblyDefinition().Name));
      case HandleKind.TypeReference:
        return (TypeRefFullName(md, (TypeReferenceHandle)handle),
          Census.ScopeAssembly(md, md.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope));
      case HandleKind.TypeSpecification:
        return DecodeTypeSpec(md, (TypeSpecificationHandle)handle);
      default:
        return ("", ""); // ldtoken of a field/method — not a type window entry
    }
  }

  /// <summary>Declaring type fullname + method name + assembly for a call target.</summary>
  public static (string Type, string Name, string Assembly) MethodFromToken(MetadataReader md, int token) {
    var handle = MetadataTokens.EntityHandle(token);
    switch (handle.Kind) {
      case HandleKind.MemberReference: {
        var member = md.GetMemberReference((MemberReferenceHandle)handle);
        var name = md.GetString(member.Name);
        switch (member.Parent.Kind) {
          case HandleKind.TypeReference:
            return (TypeRefFullName(md, (TypeReferenceHandle)member.Parent), name,
              Census.ScopeAssembly(md, md.GetTypeReference((TypeReferenceHandle)member.Parent).ResolutionScope));
          case HandleKind.TypeDefinition:
            return (TypeDefFullName(md, (TypeDefinitionHandle)member.Parent), name,
              md.GetString(md.GetAssemblyDefinition().Name));
          case HandleKind.TypeSpecification: {
            var (type, assembly) = DecodeTypeSpec(md, (TypeSpecificationHandle)member.Parent);
            return (type, name, assembly);
          }
          default:
            return ("", name, "");
        }
      }
      case HandleKind.MethodDefinition: {
        var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
        return (TypeDefFullName(md, method.GetDeclaringType()), md.GetString(method.Name),
          md.GetString(md.GetAssemblyDefinition().Name));
      }
      case HandleKind.MethodSpecification: {
        var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
        return MethodFromToken(md, MetadataTokens.GetToken(spec.Method));
      }
      default:
        return ("", "", "");
    }
  }

  public static string TypeDefFullName(MetadataReader md, TypeDefinitionHandle handle) {
    var type = md.GetTypeDefinition(handle);
    var name = md.GetString(type.Name);
    if (type.IsNested) {
      return $"{TypeDefFullName(md, type.GetDeclaringType())}/{name}";
    }

    var ns = md.GetString(type.Namespace);
    return ns == "" ? name : $"{ns}.{name}";
  }

  public static string TypeRefFullName(MetadataReader md, TypeReferenceHandle handle) {
    var type = md.GetTypeReference(handle);
    var name = md.GetString(type.Name);
    if (type.ResolutionScope.Kind == HandleKind.TypeReference) {
      return $"{TypeRefFullName(md, (TypeReferenceHandle)type.ResolutionScope)}/{name}";
    }

    var ns = md.GetString(type.Namespace);
    return ns == "" ? name : $"{ns}.{name}";
  }

  /// <summary>
  ///   Hand-decode just enough of a TypeSpec blob to name a GENERICINST's generic type (the census only
  ///   needs the assembly; full fidelity is not the goal). Anything else returns empty.
  /// </summary>
  public static (string Name, string Assembly) DecodeTypeSpec(MetadataReader md, TypeSpecificationHandle handle) {
    var blob = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
    var element = blob.ReadByte();
    if (element != 0x15) { // ELEMENT_TYPE_GENERICINST
      return ("", "");
    }

    blob.ReadByte(); // CLASS or VALUETYPE
    var coded = blob.ReadCompressedInteger(); // TypeDefOrRefOrSpec coded index
    var tag = coded & 0x3;
    var row = coded >> 2;
    switch (tag) {
      case 0:
        return (TypeDefFullName(md, MetadataTokens.TypeDefinitionHandle(row)),
          md.GetString(md.GetAssemblyDefinition().Name));
      case 1: {
        var typeRef = MetadataTokens.TypeReferenceHandle(row);
        return (TypeRefFullName(md, typeRef), Census.ScopeAssembly(md, md.GetTypeReference(typeRef).ResolutionScope));
      }
      default:
        return ("", "");
    }
  }
}

// ---------------------------------------------------------------------------------------------------------
// Trees — the restored game trees under packages/, with lazy per-assembly member indexes.
// ---------------------------------------------------------------------------------------------------------
internal sealed class Trees {
  public string Label = "";
  public string ManagedDir = "";
  private readonly Dictionary<string, Dictionary<string, TypeEntry>> indexByAssembly = new(StringComparer.OrdinalIgnoreCase);
  private Dictionary<string, int> declaringTypeCounts; // member name -> types in Assembly-CSharp declaring it

  internal sealed record TypeEntry(HashSet<string> Members, string BaseType, string BaseAssembly);

  public static List<Trees> Discover(string root) {
    var trees = new List<Trees>();
    foreach (var unit in new[] { "7dtd.assemblies.game", "7dtd.assemblies.dedicatedserver" }) {
      var unitDir = Path.Combine(root, "packages", unit);
      if (!Directory.Exists(unitDir)) {
        continue;
      }

      foreach (var versionDir in Directory.GetDirectories(unitDir)) {
        var managed = Directory.GetDirectories(versionDir, "*_Data")
          .Select(d => Path.Combine(d, "Managed"))
          .FirstOrDefault(Directory.Exists);
        if (managed != null) {
          var shortUnit = unit == "7dtd.assemblies.game" ? "game" : "server";
          trees.Add(new Trees { Label = $"{shortUnit}/{Path.GetFileName(versionDir)}", ManagedDir = managed });
        }
      }
    }

    return trees.OrderBy(t => t.Label, StringComparer.Ordinal).ToList();
  }

  public bool TypeExists(string fullName) {
    var metadataName = fullName.Replace('+', '/');
    // AccessTools.TypeByName searches all assemblies; check Assembly-CSharp first, then every DLL.
    if (Index("Assembly-CSharp")?.ContainsKey(metadataName) == true) {
      return true;
    }

    return Directory.GetFiles(ManagedDir, "*.dll")
      .Select(Path.GetFileNameWithoutExtension)
      .Any(a => Index(a)?.ContainsKey(metadataName) == true);
  }

  public int CountDeclaringTypes(string memberName) {
    if (declaringTypeCounts == null) {
      declaringTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
      var index = Index("Assembly-CSharp");
      if (index != null) {
        foreach (var entry in index.Values) {
          foreach (var member in entry.Members) {
            declaringTypeCounts[member] = declaringTypeCounts.GetValueOrDefault(member) + 1;
          }
        }
      }
    }

    return declaringTypeCounts.GetValueOrDefault(memberName);
  }

  /// <summary>Name-only member resolution, walking the base chain unless declaredOnly.</summary>
  public string ResolveMember(string typeFullName, string assembly, string memberName, bool declaredOnly) {
    var metadataName = typeFullName.Replace('+', '/');
    var chain = 0;
    var currentType = metadataName;
    var currentAssembly = assembly;
    while (currentType != "" && chain++ < 32) {
      var index = Index(currentAssembly);
      if (index == null) {
        return chain == 1 ? $"TYPE NOT FOUND (assembly {currentAssembly} absent from tree)" : "MISSING (base chain left the tree)";
      }

      if (!index.TryGetValue(currentType, out var entry)) {
        return chain == 1 ? $"TYPE NOT FOUND ({typeFullName})" : "MISSING (base type vanished mid-chain)";
      }

      if (entry.Members.Contains(memberName)) {
        return chain == 1 ? "resolves (declared)" : $"resolves (inherited, depth {chain - 1})";
      }

      if (declaredOnly && chain == 1) {
        return $"MISSING: {memberName} not declared on {ApiDrift.Display(typeFullName)}";
      }

      currentType = entry.BaseType;
      currentAssembly = entry.BaseAssembly;
    }

    return $"MISSING: {memberName} not found on {ApiDrift.Display(typeFullName)} or its base chain";
  }

  private Dictionary<string, TypeEntry> Index(string assemblyName) {
    if (indexByAssembly.TryGetValue(assemblyName, out var cached)) {
      return cached;
    }

    var path = Path.Combine(ManagedDir, assemblyName + ".dll");
    Dictionary<string, TypeEntry> index = null;
    if (File.Exists(path)) {
      index = BuildIndex(path);
    }

    indexByAssembly[assemblyName] = index;
    return index;
  }

  private static Dictionary<string, TypeEntry> BuildIndex(string path) {
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    var result = new Dictionary<string, TypeEntry>(StringComparer.Ordinal);
    foreach (var handle in md.TypeDefinitions) {
      var type = md.GetTypeDefinition(handle);
      var members = new HashSet<string>(StringComparer.Ordinal);
      foreach (var m in type.GetMethods()) members.Add(md.GetString(md.GetMethodDefinition(m).Name));
      foreach (var f in type.GetFields()) members.Add(md.GetString(md.GetFieldDefinition(f).Name));
      foreach (var p in type.GetProperties()) members.Add(md.GetString(md.GetPropertyDefinition(p).Name));
      foreach (var e in type.GetEvents()) members.Add(md.GetString(md.GetEventDefinition(e).Name));
      var (baseType, baseAssembly) = BaseOf(md, type);
      result[Tokens.TypeDefFullName(md, handle)] = new TypeEntry(members, baseType, baseAssembly);
    }

    return result;
  }

  private static (string, string) BaseOf(MetadataReader md, TypeDefinition type) {
    var baseHandle = type.BaseType;
    if (baseHandle.IsNil) {
      return ("", "");
    }

    return baseHandle.Kind switch {
      HandleKind.TypeDefinition => (Tokens.TypeDefFullName(md, (TypeDefinitionHandle)baseHandle),
        md.GetString(md.GetAssemblyDefinition().Name)),
      HandleKind.TypeReference => (Tokens.TypeRefFullName(md, (TypeReferenceHandle)baseHandle),
        Census.ScopeAssembly(md, md.GetTypeReference((TypeReferenceHandle)baseHandle).ResolutionScope)),
      _ => ("", ""),
    };
  }
}

// ---------------------------------------------------------------------------------------------------------
// Selftest — pure-function cases plus one real end-to-end IL scan of this tool's own compiled assembly.
// ---------------------------------------------------------------------------------------------------------
internal static class SelfTest {
  // Never invoked; exists so the selftest can find a REAL lookup site in this tool's own IL.
  internal static class Marker {
    internal static void Lookup() {
      typeof(SelfTest).GetMethod("MarkerVictim");
      HarmonyStandIn.TypeByNameStandIn("Some.Game.Type");
    }
  }

  internal static class HarmonyStandIn {
    internal static void TypeByNameStandIn(string name) => _ = name;
  }

  public static int Run() {
    var failures = new List<string>();

    void Check(bool condition, string what) {
      if (!condition) failures.Add(what);
    }

    // 1. Colon-form split inside ExtractSites.
    var colon = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldstr", "Ns.Foo:Bar"),
      new("call", "HarmonyLib.AccessTools", "Method", "0Harmony"),
    });
    Check(colon.Count == 1 && colon[0].SubjectType == "Ns.Foo" && colon[0].MemberName == "Bar",
      "colon-form AccessTools.Method(\"Type:Member\") splits into subject + member");

    // 2. typeof(X) + literal pairing, with GetTypeFromHandle NOT breaking the window.
    var paired = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldtoken-type", "GameNs.Thing", "Assembly-CSharp"),
      new("call", "System.Type", "GetTypeFromHandle", "mscorlib"),
      new("ldstr", "DoIt"),
      new("call", "HarmonyLib.AccessTools", "DeclaredMethod", "0Harmony"),
    });
    Check(paired.Count == 1 && paired[0].SubjectType == "GameNs.Thing" && paired[0].MemberName == "DoIt"
          && paired[0].SubjectAssembly == "Assembly-CSharp",
      "ldtoken/ldstr window pairs subject and name across GetTypeFromHandle");

    // 3. An intervening call breaks the window: the stale string must not leak into the next site.
    var broken = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldstr", "stale"),
      new("call", "Some.Helper", "Frob", "Elsewhere"),
      new("call", "HarmonyLib.AccessTools", "TypeByName", "0Harmony"),
    });
    Check(broken.Count == 1 && broken[0].MemberName == "",
      "an unrelated call clears the window (stale ldstr not attributed)");

    // 4. Dynamic subject: no ldtoken before the lookup -> empty subject.
    var dynamicSubject = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldstr", "ProcessPackage"),
      new("call", "HarmonyLib.AccessTools", "DeclaredMethod", "0Harmony"),
    });
    Check(dynamicSubject.Count == 1 && dynamicSubject[0].SubjectType == ""
          && dynamicSubject[0].MemberName == "ProcessPackage",
      "lookup with no ldtoken reports a dynamic subject and keeps the name");

    // 5. End-to-end: decode this tool's own compiled IL and find Marker.Lookup's Type.GetMethod site.
    var self = typeof(SelfTest).Assembly.Location;
    if (self == "" || !File.Exists(self)) {
      failures.Add($"cannot locate own assembly for the end-to-end case (Location='{self}')");
    } else {
      using var stream = File.OpenRead(self);
      using var pe = new PEReader(stream);
      var sites = Scanner.Scan("self", pe, pe.GetMetadataReader());
      Check(sites.Any(s => s.Api == "Type.GetMethod" && s.MemberName == "MarkerVictim"
                           && s.SubjectType.EndsWith("SelfTest") && s.Caller.Contains("Marker")),
        "end-to-end IL scan of own assembly finds Marker.Lookup's Type.GetMethod(\"MarkerVictim\") site");
    }

    // 5b. Argument-array types are popped by stelem: the AuthZ FindProcessPackage IL shape.
    //     AccessTools.DeclaredMethod(<dynamic>, "ProcessPackage", new[]{typeof(World), typeof(GameManager)})
    var argArray = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldstr", "ProcessPackage"),
      new("ldtoken-type", "World", "Assembly-CSharp"),
      new("call", "System.Type", "GetTypeFromHandle", "mscorlib"),
      new("stelem"),
      new("ldtoken-type", "GameManager", "Assembly-CSharp"),
      new("call", "System.Type", "GetTypeFromHandle", "mscorlib"),
      new("stelem"),
      new("call", "HarmonyLib.AccessTools", "DeclaredMethod", "0Harmony"),
    });
    Check(argArray.Count == 1 && argArray[0].SubjectType == "" && argArray[0].MemberName == "ProcessPackage",
      "typeof()s stored into an argument array are not mistaken for the subject");

    // 5c. A subject typeof survives a later argument array.
    var subjectPlusArray = Scanner.ExtractSites("m", "c", new List<Instr> {
      new("ldtoken-type", "GameNs.Subject", "Assembly-CSharp"),
      new("call", "System.Type", "GetTypeFromHandle", "mscorlib"),
      new("ldstr", "DoIt"),
      new("ldtoken-type", "System.Int32", "mscorlib"),
      new("call", "System.Type", "GetTypeFromHandle", "mscorlib"),
      new("stelem"),
      new("call", "HarmonyLib.AccessTools", "Method", "0Harmony"),
    });
    Check(subjectPlusArray.Count == 1 && subjectPlusArray[0].SubjectType == "GameNs.Subject",
      "the subject typeof survives argument-array pops");

    // 6. BCL classification.
    Check(Census.IsBcl("mscorlib") && Census.IsBcl("System.Core") && !Census.IsBcl("Assembly-CSharp"),
      "BCL classification");

    Console.WriteLine(failures.Count == 0
      ? "selftest: all 8 cases pass"
      : $"selftest: {failures.Count} FAILURE(S):\n  " + string.Join("\n  ", failures));
    return failures.Count == 0 ? 0 : 1;
  }
}

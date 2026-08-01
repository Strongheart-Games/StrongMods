#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Prove an MSBuild project-file change is a no-op, without building anything.
//
// C# port of compare-eval.py (retired with it; #36 language decision) — comparison semantics, output shape, and
// the 0/1 exit contract are 1:1. NOT imported by MSBuild. This is a developer tool that happens to live next to
// the shared build files because the build system is what it inspects; nothing in build\*.props or
// build\*.targets references it.
//
// MSBuild's -getProperty/-getItem flags *evaluate* a project and print the result as JSON without running any
// target — no compile, no file copy, nothing written to the game install. Diffing the evaluation of a project
// before and after a csproj edit is therefore a free, side-effect-free regression check.
//
// Typical use, comparing the working tree against a pristine checkout:
//
//     PROPS=OutputPath,OutDir,TargetDir,TargetPath,LangVersion,DefineConstants,AssemblyName,RootNamespace,\
//     TargetFrameworkVersion,OutputType,DebugType,Optimize,DebugSymbols,PlatformTarget,WarningLevel,ErrorReport,\
//     FileAlignment,AppDesignerFolder
//     ITEMS=Reference,Compile,Content,None,ProjectReference
//
//     git worktree add --detach .scratch/baseline HEAD
//     dotnet msbuild .scratch/baseline/Foo/Foo.csproj -nologo -p:Configuration=Debug "-getProperty:$PROPS" "-getItem:$ITEMS" > b.json
//     dotnet msbuild Foo/Foo.csproj                   -nologo -p:Configuration=Debug "-getProperty:$PROPS" "-getItem:$ITEMS" > a.json
//     dotnet run build/tools/compare-eval.cs -- b.json a.json Foo
//
// Two things worth knowing, both learned the hard way:
//
//   * Always include OutDir/TargetDir, not just OutputPath. Microsoft.Common.CurrentVersion.targets derives them
//     from OutputPath *during evaluation*, so an OutputPath set too late reads back correct while OutDir stays
//     latched at the bin\$(Configuration)\ fallback.
//   * Evaluation does not run Roslyn. A clean diff means the compiler's inputs are unchanged; it does not prove
//     the project still compiles. Follow up with one real build.
//
// Exit codes: 0 = evaluations match, 1 = they differ (so it can gate a script), 2 = usage or unreadable input.

using System.Text.Json;

if (args.Length < 2) {
  Console.Error.WriteLine("usage: compare-eval.cs <before.json> <after.json> [label]"
    + "  (msbuild -getProperty/-getItem JSON; see the header comment for the full recipe)");
  return 2;
}

try {
  return CompareEval.Run(args[0], args[1], args.Length > 2 ? args[2] : "");
} catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal static class CompareEval {
  // Metadata that actually affects the build. Everything else MSBuild synthesises is location-derived
  // (FullPath, Directory, RootDir, RelativeDir, DefiningProject*, timestamps) and necessarily differs
  // between the repo and a git worktree holding the baseline, so comparing it is pure noise.
  private static readonly string[] Meaningful = {
    "HintPath", "Private", "CopyToOutputDirectory", "Link", "Project", "Name",
    "CopyLocalSatelliteAssemblies", "SpecificVersion", "Aliases", "SubType", "DependentUpon"
  };

  public static int Run(string beforePath, string afterPath, string label) {
    using var before = JsonDocument.Parse(File.ReadAllText(beforePath));
    using var after = JsonDocument.Parse(File.ReadAllText(afterPath));
    var diffs = new List<string>();

    var bp = Properties(before);
    var ap = Properties(after);
    foreach (var name in bp.Keys.Union(ap.Keys).OrderBy(n => n, StringComparer.Ordinal)) {
      if (bp.GetValueOrDefault(name) != ap.GetValueOrDefault(name)) {
        diffs.Add($"    PROP {name}: {Show(bp.GetValueOrDefault(name))} -> {Show(ap.GetValueOrDefault(name))}");
      }
    }

    var bi = Items(before);
    var ai = Items(after);
    var kinds = bi.Keys.Union(ai.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
    foreach (var kind in kinds) {
      var b = Keyed(bi.GetValueOrDefault(kind));
      var a = Keyed(ai.GetValueOrDefault(kind));
      diffs.AddRange(b.Where(kv => !a.ContainsKey(kv.Key)).Select(kv => $"    {kind} BEFORE-ONLY {Describe(kv.Value)}"));
      diffs.AddRange(a.Where(kv => !b.ContainsKey(kv.Key)).Select(kv => $"    {kind} AFTER-ONLY  {Describe(kv.Value)}"));
    }

    var summary = string.Join(", ", kinds.Select(k =>
      $"{k} {bi.GetValueOrDefault(k)?.Count ?? 0}->{ai.GetValueOrDefault(k)?.Count ?? 0}"));
    if (diffs.Count > 0) {
      Console.WriteLine($"{label,-22} {diffs.Count} diff(s)   [{summary}]");
      Console.WriteLine(string.Join("\n", diffs));
      return 1;
    }

    Console.WriteLine($"{label,-22} IDENTICAL   [{summary}]");
    return 0;
  }

  private static string Show(string? value) {
    return value is null ? "null" : $"'{value}'";
  }

  /// Identity plus build-affecting metadata: catches a changed HintPath/Private/CopyToOutputDirectory,
  /// and cannot cancel out if a HintPath moves between two items.
  private static string ItemKey(Dictionary<string, string> item) {
    const char fieldSep = (char)0; // control chars cannot appear in MSBuild metadata,
    const char partSep = (char)1;  // so the key cannot collide or cancel out
    var parts = new List<string> { item.GetValueOrDefault("Identity", "") };
    parts.AddRange(Meaningful.Where(item.ContainsKey).Select(k => $"{k}{fieldSep}{item[k]}"));
    return string.Join(partSep, parts);
  }

  private static Dictionary<string, Dictionary<string, string>> Keyed(List<Dictionary<string, string>>? items) {
    var result = new Dictionary<string, Dictionary<string, string>>();
    foreach (var item in items ?? new List<Dictionary<string, string>>()) {
      result[ItemKey(item)] = item;
    }

    return result;
  }

  private static string Describe(Dictionary<string, string> item) {
    var extras = string.Join(" ", Meaningful
      .Where(k => item.TryGetValue(k, out var v) && v.Length > 0)
      .Select(k => $"{k}={item[k]}"));
    return item.GetValueOrDefault("Identity", "") + (extras.Length > 0 ? $"  [{extras}]" : "");
  }

  private static string Text(JsonElement value) {
    return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
  }

  private static Dictionary<string, string> Properties(JsonDocument doc) {
    var result = new Dictionary<string, string>();
    if (doc.RootElement.TryGetProperty("Properties", out var props)) {
      foreach (var p in props.EnumerateObject()) {
        result[p.Name] = Text(p.Value);
      }
    }

    return result;
  }

  private static Dictionary<string, List<Dictionary<string, string>>> Items(JsonDocument doc) {
    var result = new Dictionary<string, List<Dictionary<string, string>>>();
    if (!doc.RootElement.TryGetProperty("Items", out var kinds)) {
      return result;
    }

    foreach (var kind in kinds.EnumerateObject()) {
      var list = new List<Dictionary<string, string>>();
      foreach (var item in kind.Value.EnumerateArray()) {
        list.Add(item.EnumerateObject().ToDictionary(f => f.Name, f => Text(f.Value)));
      }

      result[kind.Name] = list;
    }

    return result;
  }
}

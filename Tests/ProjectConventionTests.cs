using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests;

/// <summary>
///   Conventions the repo's project files must hold to, checked by reading the .csproj files themselves. No
///   game assemblies, no load contexts — these are cheap file scans.
/// </summary>
public class ProjectConventionTests {
  [Fact]
  public void A_HintPath_into_another_projects_output_is_ordered_by_a_ProjectReference() {
    // A <Reference><HintPath> pointing at a sibling project's bin\ compiles only if that project happened to
    // be built first, and nothing in the build graph says it must be. It passes on any machine where the
    // sibling was ever built, then fails on a clean checkout — exactly what #51 found in Tests\FunctionMod.
    // A ProjectReference (even ReferenceOutputAssembly=false, which orders without referencing) is the fix.
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    var configuration = AssemblyMetadata.Get("Configuration");
    List<string> projects = SourceProjects(repoRoot).ToList();
    // Longest first: Tests\FunctionMod must win over Tests for a path inside it.
    List<string> projectDirs = projects.Select(p => Path.GetDirectoryName(p)!)
      .OrderByDescending(d => d.Length).ToList();

    var offenders = new List<string>();
    foreach (var project in projects) {
      var projectDir = Path.GetDirectoryName(project)!;
      XDocument document = XDocument.Load(project);
      HashSet<string> ordered = Elements(document, "ProjectReference")
        .Select(e => Resolve(projectDir, e.Attribute("Include")?.Value))
        .Where(p => p != null).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

      foreach (XElement hint in Elements(document, "HintPath")) {
        var raw = hint.Value.Replace("$(Configuration)", configuration);
        if (raw.Contains("$(")) {
          continue; // a property this scan cannot resolve — the game's assemblies, not a repo project
        }

        var target = Resolve(projectDir, raw);
        var owner = projectDirs.FirstOrDefault(d =>
          !string.Equals(d, projectDir, StringComparison.OrdinalIgnoreCase) &&
          target!.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (owner == null) {
          continue; // points outside every repo project — nothing to order against
        }

        var ownerProject = projects.First(p => Path.GetDirectoryName(p)!
          .Equals(owner, StringComparison.OrdinalIgnoreCase));
        if (!ordered.Contains(ownerProject)) {
          offenders.Add(
            $"{Relative(repoRoot, project)} references {Path.GetFileName(hint.Value)} by HintPath into " +
            $"{Relative(repoRoot, owner)}'s build output, but has no ProjectReference to " +
            $"{Relative(repoRoot, ownerProject)} — so nothing orders that project ahead of this one and a " +
            "clean checkout can fail to compile (#51). Add:\n" +
            $"    <ProjectReference Include=\"{Relative(projectDir, ownerProject)}\"\n" +
            "                      ReferenceOutputAssembly=\"false\"\n" +
            "                      SkipGetTargetFrameworkProperties=\"true\" />\n" +
            "  ReferenceOutputAssembly=false orders the build without changing what this project compiles " +
            "against (the HintPath still supplies the reference); SkipGetTargetFrameworkProperties skips the " +
            "TFM-compatibility check, which is needed when the two projects target different frameworks.");
        }
      }
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }

  // Which shape each build\ entry point belongs to. A project imports exactly one shape's set; the three are
  // mutually exclusive because each defines its own Deploy target.
  private static readonly Dictionary<string, string> EntryPointShapes = new(StringComparer.OrdinalIgnoreCase) {
    ["Mod.props"] = "code mod", ["Mod.targets"] = "code mod",
    ["Modlet.targets"] = "modlet",
    ["Overlay.props"] = "overlay", ["Overlay.targets"] = "overlay"
  };

  // What each shape must open and close with. A modlet has no props half, so it constrains only its last
  // element. See AGENTS.md's "Shared build files" table for the sandwich each shape forms.
  private static readonly Dictionary<string, (string First, string Last)> ShapeBookends = new() {
    ["code mod"] = ("Mod.props", "Mod.targets"),
    ["modlet"] = (null, "Modlet.targets"),
    ["overlay"] = ("Overlay.props", "Overlay.targets")
  };

  // Projects that are not mods at all and so import no entry point. Asserted, not skipped — see the test.
  private static readonly string[] KnownNonMods = {
    @"Tests\Tests.csproj", @"Tests\Stubs\UnityStub.csproj", @"Tests\FunctionMod\FunctionMod.csproj",
    @"build\GameAssemblies.csproj"
  };

  [Fact]
  public void B_Every_project_imports_its_shapes_entry_points_in_the_load_bearing_order() {
    // Import POSITION is this build system's central invariant. There is deliberately no
    // Directory.Build.props/.targets (the build\Mod.targets header records the OutDir-latch incident that
    // decided it), so each project's own imports are the whole mechanism — and MSBuild property expansion is
    // immediate, which makes "before" and "after" mean something. Two incidents, both shipped:
    //   * A <DeployRoot>$(ModsDir)\Hades</DeployRoot> written ABOVE the Overlay.props import froze the
    //     reference empty; $(ModsDir)\Hades became \Hades and a deploy landed in C:\Hades (2026-07-30).
    //   * An OutputPath set after Sdk.targets evaluates leaves OutDir latched at the bin\ fallback while
    //     OutputPath itself reads back correct.
    // Prose alone measurably rots here (#52's comment asserted the opposite of the truth for its whole life),
    // so the rule is executable. Comments and the XML declaration are not elements, so the templates'
    // <!--#if (IsTemplate)--> blocks are invisible to these position checks and need no exemption.
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    var offenders = new List<string>();

    foreach (var project in SourceProjects(repoRoot)) {
      XDocument document = XDocument.Load(project);
      var name = Relative(repoRoot, project);
      HashSet<string> shapes = Elements(document, "Import").Select(SharedBuildFile)
        .Where(file => file != null && EntryPointShapes.ContainsKey(file))
        .Select(file => EntryPointShapes[file]).ToHashSet();

      if (shapes.Count == 0) {
        continue; // no entry point at all — the roster test below owns that case
      }
      if (shapes.Count > 1) {
        offenders.Add($"{name} imports entry points of {shapes.Count} different shapes " +
          $"({string.Join(", ", shapes.OrderBy(s => s))}). The shapes are mutually exclusive: each defines its " +
          "own Deploy target and two in one project collide. Pick one — code mod (Mod.props + Mod.targets), " +
          "modlet (Modlet.targets), or overlay (Overlay.props + Overlay.targets).");
        continue;
      }

      var shape = shapes.Single();
      (var first, var last) = ShapeBookends[shape];
      List<XElement> body = document.Root.Elements().ToList();
      // The one thing allowed AHEAD of a props import: a single literal-only declaration block (#23's
      // two-slot rule — declarations above, deviations between). Rule D owns the block's content; here it
      // only shifts where the import must sit.
      var lead = first != null && IsDeclarationBlock(body[0]) ? 1 : 0;

      if (first != null && SharedBuildFile(body[lead]) != first) {
        offenders.Add($"{name} is {Article(shape)}, so its FIRST element must be " +
          $"<Import Project=\"..\\build\\{first}\" /> — optionally preceded by ONE PropertyGroup holding " +
          $"only literal declaration properties ({string.Join(", ", DeclarationProperties)}) — but it is " +
          $"{Describe(body[lead])}.\n" +
          "  The props half must be imported before anything that references a shared property, because " +
          "expansion is immediate: a body that reads $(ModsDir) or $(SdtdSavesDir) too early freezes it empty. " +
          "That shipped — $(ModsDir)\\Hades evaluated to \\Hades and the deploy landed in C:\\Hades " +
          "(2026-07-30; the build\\Overlay.props header has the incident).");
      }

      if (SharedBuildFile(body[^1]) != last) {
        offenders.Add($"{name} is {Article(shape)}, so its LAST element must be " +
          $"<Import Project=\"..\\build\\{last}\" /> — it is {Describe(body[^1])}.\n" +
          "  The targets half must come after the whole body so the body's deviations win, and so OutputPath is " +
          "set before Microsoft.Common.CurrentVersion.targets derives OutDir/TargetDir from it DURING " +
          "EVALUATION. Set too late, OutputPath reads back correct while OutDir stays latched at the bin\\ " +
          "fallback and the assembly lands somewhere else entirely (build\\Mod.targets header — and the reason " +
          "compare-eval insists on querying OutDir, not just OutputPath).");
      }

      if (shape == "overlay" && !DefinesDeployRootBelow(document, body, "Overlay.props")) {
        offenders.Add($"{name} is an overlay but does not define <DeployRoot> BELOW its Overlay.props import.\n" +
          "  DeployRoot is built from $(ModsDir) or $(SdtdSavesDir) — defined above the import, those " +
          "references expand empty and the deploy root becomes a driveless path. Overlay.targets carries a " +
          "runtime guard for it precisely because it happened (C:\\Hades, 2026-07-30); this catches it a build " +
          "earlier.");
      }
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }

  [Fact]
  public void C_Only_known_non_mod_projects_import_no_build_entry_point() {
    // The exemptions are asserted rather than skipped. A project matching no shape is invisible to the order
    // test above, so if the roster were implicit a new one would go silently unchecked — the failure mode this
    // whole pair exists to prevent.
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    List<string> unclassified = SourceProjects(repoRoot)
      .Where(project => !Elements(XDocument.Load(project), "Import").Select(SharedBuildFile)
        .Any(file => file != null && EntryPointShapes.ContainsKey(file)))
      .Select(project => Relative(repoRoot, project)).OrderBy(name => name).ToList();

    IEnumerable<string> expected = KnownNonMods.Select(NormalizeSeparators).OrderBy(name => name);
    Assert.True(unclassified.SequenceEqual(expected),
      "The set of projects importing none of the three build\\ entry points changed.\n" +
      $"  expected: {string.Join(", ", expected)}\n" +
      $"  actual:   {string.Join(", ", unclassified)}\n" +
      "  A new mod must import its shape's entry points (see AGENTS.md, \"Adding a new mod\"); a new non-mod " +
      "project must be added to KnownNonMods with a reason. Silently falling outside every shape means no " +
      "convention test covers it.");
  }

  // The declaration properties (#23): what a mod may pin in its leading block, and nothing else may go there.
  private static readonly string[] DeclarationProperties = { "SdtdDevVersion", "SdtdTestVersions" };

  /// <summary>One PropertyGroup of declaration properties only, with no $() anywhere in it.</summary>
  private static bool IsDeclarationBlock(XElement element) =>
    element.Name.LocalName == "PropertyGroup" && element.HasElements &&
    element.Elements().All(p => DeclarationProperties.Contains(p.Name.LocalName)) &&
    !element.ToString().Contains("$(");

  [Fact]
  public void D_Declaration_properties_are_literal_and_sit_above_the_entry_point_props_import() {
    // Both halves are load-bearing, measured (.ai/declared-game-versions.md §4):
    //   * Position: BETWEEN the imports, a declaration assigns after build\GamePaths.props has already
    //     resolved the game tree — the property reads back changed while every derived path silently keeps
    //     the old value (the OutDir-latch shape). The runtime guard (_ValidateGameTreeDeclaration) catches
    //     that at build time; this scan catches it a build earlier, and catches placements no build runs.
    //   * Literalness: a $() reference in a leading block would read shared properties BEFORE the props
    //     import defines them — the class that once evaluated $(ModsDir)\Hades to \Hades and deployed into
    //     C:\Hades. A literal cannot do that.
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    var offenders = new List<string>();

    foreach (var project in SourceProjects(repoRoot)) {
      XDocument document = XDocument.Load(project);
      var name = Relative(repoRoot, project);
      List<XElement> body = document.Root.Elements().ToList();
      var propsFirst = Elements(document, "Import").Select(SharedBuildFile)
        .Any(file => file is "Mod.props" or "Overlay.props");

      foreach (XElement declaration in document.Descendants()
                 .Where(e => DeclarationProperties.Contains(e.Name.LocalName))) {
        if (declaration.ToString().Contains("$(")) {
          offenders.Add($"{name} declares {declaration.Name.LocalName} with a $() reference: " +
            $"{declaration.ToString().Trim()}\n  Declaration values are LITERAL labels. A reference in a " +
            "leading block reads shared properties before the props import defines them — the class that " +
            "once evaluated $(ModsDir)\\Hades to \\Hades and deployed into C:\\Hades.");
        }

        if (propsFirst && !(declaration.Parent == body[0] && IsDeclarationBlock(body[0]))) {
          offenders.Add($"{name} declares {declaration.Name.LocalName} outside the leading declaration " +
            "block. Declarations live in ONE PropertyGroup ABOVE the entry-point props import; set any " +
            "lower, the property reads back changed while build\\GamePaths.props has already resolved the " +
            "game tree from the old value — the OutDir-latch shape. (_ValidateGameTreeDeclaration is the " +
            "runtime backstop; this scan fires a build earlier.) Two-slot rule: declarations above, " +
            "deviations between.");
        }
      }
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }

  [Fact]
  public void E_Declared_versions_and_the_registry_close_over_each_other() {
    // build\GameVersions.props' registry and the declarations (repo defaults + per-mod leading blocks) must
    // agree in BOTH directions, each mod must test what it compiles against, and every registry row must
    // match an independent recomputation of pack.cs's FourPart label mapping — the same
    // two-independent-computations pattern pack.cs itself uses against vendor.cs's nuspec stub.
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    XDocument versions = XDocument.Load(Path.Combine(repoRoot, "build", "GameVersions.props"));
    var offenders = new List<string>();

    var map = new Dictionary<string, string>();
    foreach (var pair in GameVersionDeclarations.SplitList(Declared(versions, "SdtdGameVersionMap"))) {
      string[] parts = pair.Split('=');
      if (parts.Length != 2 || FourPart(parts[0]) == null) {
        offenders.Add($"SdtdGameVersionMap entry '{pair}' is not a 'V<major>.<minor>[.<patch>]-b<build>=" +
          "<four-part>' pair. Rows are branch-head labels mapped to package versions, semicolon-separated.");
        continue;
      }
      map[parts[0]] = parts[1];
      if (FourPart(parts[0]) != parts[1]) {
        offenders.Add($"SdtdGameVersionMap maps {parts[0]} to {parts[1]}, but the FourPart rule maps it to " +
          $"{FourPart(parts[0])} (V3.1.0-b13 -> 3.1.0.13; V2.5-b8 -> 2.5.0.8 — patch defaults to 0, never " +
          "dropped). pack.cs computes the same rule from the label when packing; a row that disagrees would " +
          "resolve a tree the feed cannot contain.");
      }
    }

    // Effective declarations come from the same reader ModInventory consumes (GameVersionDeclarations), so
    // what this test validates is exactly what the suite runs — the two cannot drift.
    GameVersionDeclarations declarations = GameVersionDeclarations.Load(repoRoot);
    var declared = new Dictionary<string, string> {
      [declarations.DefaultDevVersion] = "the repo default (build\\GameVersions.props)"
    };
    foreach (var label in declarations.DefaultTestVersions) {
      declared.TryAdd(label, "the repo default (build\\GameVersions.props)");
    }

    foreach (var project in SourceProjects(repoRoot)) {
      var name = Relative(repoRoot, project);
      (var dev, IReadOnlyList<string> test) = declarations.For(project);
      declared.TryAdd(dev, name);
      foreach (var label in test) {
        declared.TryAdd(label, name);
      }

      if (!test.Contains(dev)) {
        offenders.Add($"{name} develops against {dev} but tests against [{string.Join(", ", test)}] — the " +
          "dev version must be among the test versions. Compiling against a version you never test is " +
          "exactly the silent gap the declaration exists to close.");
      }
    }

    foreach ((var label, var declaredBy) in declared.Where(d => !map.ContainsKey(d.Key))) {
      var hint = label.Contains(',')
        ? " Labels are SEMICOLON-separated — a comma joins two labels into one unknown token."
        : "";
      offenders.Add($"'{label}' (declared by {declaredBy}) has no row in SdtdGameVersionMap " +
        $"(build\\GameVersions.props).{hint} Rows are branch heads; add 'label=fourpart' or fix the label.");
    }

    foreach (var label in map.Keys.Where(k => !declared.ContainsKey(k))) {
      offenders.Add($"SdtdGameVersionMap row '{label}' is declared by nothing — a dead pin. The registry " +
        "lists exactly what is declared (repo defaults or a mod's leading block); drop the row or declare it.");
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }

  /// <summary>The one element of this name in GameVersions.props (the _...AtResolve capture is named apart).</summary>
  private static string Declared(XDocument versions, string name) =>
    versions.Descendants().First(e => e.Name.LocalName == name).Value;

  /// <summary>pack.cs's label mapping, recomputed independently: null when the label is not label-shaped.</summary>
  private static string FourPart(string label) {
    Match match = Regex.Match(label, @"^V(\d+)\.(\d+)(?:\.(\d+))?-b(\d+)$");
    return !match.Success ? null :
      $"{match.Groups[1].Value}.{match.Groups[2].Value}." +
      $"{(match.Groups[3].Success ? match.Groups[3].Value : "0")}.{match.Groups[4].Value}";
  }

  private static IEnumerable<string> SourceProjects(string repoRoot) =>
    Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
      .Where(path => IsSourceProject(Path.GetRelativePath(repoRoot, path))).Select(Path.GetFullPath);

  /// <summary>The file name if this element imports something out of build\, else null.</summary>
  private static string SharedBuildFile(XElement element) {
    if (element.Name.LocalName != "Import") {
      return null;
    }

    var raw = element.Attribute("Project")?.Value;
    string[] parts = raw?.Split('\\', '/') ?? Array.Empty<string>();
    return parts.Length >= 2 && parts[^2].Equals("build", StringComparison.OrdinalIgnoreCase) ? parts[^1] : null;
  }

  private static bool DefinesDeployRootBelow(XDocument document, List<XElement> body, string import) {
    XElement holder = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "DeployRoot")
      ?.AncestorsAndSelf().FirstOrDefault(e => e.Parent == document.Root);
    return holder != null && body.IndexOf(holder) > body.FindIndex(e => SharedBuildFile(e) == import);
  }

  private static string Article(string shape) => $"{("aeiou".Contains(shape[0]) ? "an" : "a")} {shape}";

  private static string Describe(XElement element) =>
    SharedBuildFile(element) is { } file ? $"an import of build\\{file}" : $"<{element.Name.LocalName}>";

  private static string NormalizeSeparators(string path) =>
    path.Replace('\\', Path.DirectorySeparatorChar);

  private static IEnumerable<XElement> Elements(XDocument document, string name) =>
    document.Descendants().Where(e => e.Name.LocalName == name);

  private static string Resolve(string baseDir, string path) =>
    path == null ? null : Path.GetFullPath(Path.Combine(baseDir, path.Replace('\\', Path.DirectorySeparatorChar)));

  private static string Relative(string from, string to) => Path.GetRelativePath(from, to);

  /// <summary>
  ///   A project the repo's shape conventions govern. Build output, the scratch area and the vendored game
  ///   trees are obviously not; <c>.ai\</c> is the less obvious one. Effort-scoped prototypes live there
  ///   (AGENTS.md, "Docs have homes"), they are excluded from every mod project's compile glob, and they are
  ///   deliberately outside StrongMods.sln — so a mod-shape entry point is exactly what they must NOT import.
  ///   ModInventory already skips <c>.ai\</c> for the same reason. Added when #82's Tier-2 client and its
  ///   isolation mod landed under StrongDev\.ai\.
  /// </summary>
  private static bool IsSourceProject(string relativePath) {
    var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return !parts.Any(p => p is "bin" or "obj" or ".scratch" or "vendor" or ".git" or ".ai");
  }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    var repoRoot = Path.GetFullPath(GameTree.Metadata("RepoRoot"));
    var configuration = GameTree.Metadata("Configuration");
    List<string> projects = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
      .Where(IsSourceProject).Select(Path.GetFullPath).ToList();
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
  // element. See CLAUDE.md's "Shared build files" table for the sandwich each shape forms.
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
    var repoRoot = Path.GetFullPath(GameTree.Metadata("RepoRoot"));
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

      if (first != null && SharedBuildFile(body[0]) != first) {
        offenders.Add($"{name} is {Article(shape)}, so its FIRST element must be " +
          $"<Import Project=\"..\\build\\{first}\" /> — it is {Describe(body[0])}.\n" +
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
    var repoRoot = Path.GetFullPath(GameTree.Metadata("RepoRoot"));
    List<string> unclassified = SourceProjects(repoRoot)
      .Where(project => !Elements(XDocument.Load(project), "Import").Select(SharedBuildFile)
        .Any(file => file != null && EntryPointShapes.ContainsKey(file)))
      .Select(project => Relative(repoRoot, project)).OrderBy(name => name).ToList();

    IEnumerable<string> expected = KnownNonMods.Select(NormalizeSeparators).OrderBy(name => name);
    Assert.True(unclassified.SequenceEqual(expected),
      "The set of projects importing none of the three build\\ entry points changed.\n" +
      $"  expected: {string.Join(", ", expected)}\n" +
      $"  actual:   {string.Join(", ", unclassified)}\n" +
      "  A new mod must import its shape's entry points (see CLAUDE.md, \"Adding a new mod\"); a new non-mod " +
      "project must be added to KnownNonMods with a reason. Silently falling outside every shape means no " +
      "convention test covers it.");
  }

  private static IEnumerable<string> SourceProjects(string repoRoot) =>
    Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
      .Where(IsSourceProject).Select(Path.GetFullPath);

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

  private static bool IsSourceProject(string path) {
    var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return !parts.Any(p => p is "bin" or "obj" or ".scratch" or "vendor" or ".git");
  }
}

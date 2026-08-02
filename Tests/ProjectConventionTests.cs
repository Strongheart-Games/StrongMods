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

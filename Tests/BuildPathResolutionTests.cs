using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Tests;

/// <summary>
///   Pins what a RELATIVE path override means: -p:SdtdDir, -p:ModsDir and -p:SdtdSavesDir all resolve against
///   the directory the command was run from, never against each project's own directory. Two bugs shipped from
///   getting this wrong, at the two different points in the build lifecycle where it can be gotten wrong:
///
///     #46  EVALUATION time — build\GamePaths.props resolved -p:SdtdDir=vendor/... against each project, so a
///          dedicated-server tree was silently misdetected as a game tree, and FrameworkPathOverride came out
///          unrooted (NETSDK1052) once a mod project entered the Tests build graph.
///     #52  TARGET-EXECUTION time — build\Deploy.targets computed the deploy destination inside the Deploy
///          target, where MSBuild has already pointed the process working directory at the PROJECT. So
///          -p:ModsDir=.scratch/deploy — the redirect CLAUDE.md documents for safe verification — wrote 29
///          per-project folders, nothing at the repo root, and reported 0 Error(s).
///
///   Both were defended by prose alone, and the Deploy.targets prose asserted the opposite of the truth for its
///   whole life. These tests shell MSBuild because nothing cheaper can observe the rule: it is a property of
///   MSBuild's own evaluation and execution phases, invisible to a file scan.
///
///   No game install is needed — layout detection is Exists() on directories, so a fixture is a few empty
///   folders. Each test builds a throwaway tree with the invocation directory and the probe PROJECT as SIBLINGS,
///   so "resolved against the invocation" and "resolved against the project" cannot be mistaken for one another,
///   and neither can accidentally coincide with the repo root.
/// </summary>
public class BuildPathResolutionTests {
  [Fact]
  public void Relative_SdtdDir_resolves_against_the_invocation_directory() {
    using var space = new Workspace();
    space.Fixture("tree", "7DaysToDie_Data");
    IReadOnlyDictionary<string, string> resolved = space.Evaluate(space.CodeModProject(), "tree",
      "SdtdManagedDir", "SdtdHarmonyDir", "FrameworkPathOverride");

    foreach ((var name, var value) in resolved) {
      Assert.True(Path.IsPathRooted(value),
        $"{name} resolved to '{value}', which is not an absolute path. build\\GamePaths.props must normalize a " +
        "relative -p:SdtdDir into $(_SdtdRoot) before deriving anything from it; an unrooted value reaches the " +
        "compiler as NETSDK1052 (#46).");
      Assert.True(value.StartsWith(space.InvocationDir, StringComparison.OrdinalIgnoreCase),
        $"{name} resolved to\n    {value}\nbut -p:SdtdDir=tree was passed from\n    {space.InvocationDir}\n" +
        $"The probe project lives at\n    {space.ProjectDir}\nand the repo root is\n    {Workspace.RepoRoot}\n" +
        "A relative override means relative to WHERE THE COMMAND WAS RUN. MSBuild resolves relative paths " +
        "against the project by default, so build\\GamePaths.props names $(MSBuildStartupDirectory) explicitly " +
        "(#46). If this fails, that anchor was dropped or something derives from raw $(SdtdDir) instead of " +
        "$(_SdtdRoot).");
    }
  }

  [Fact]
  public void Relative_SdtdDir_detects_the_dedicated_server_layout() {
    using var space = new Workspace();
    space.Fixture("tree", "7DaysToDieServer_Data");
    var managed = space.Evaluate(space.CodeModProject(), "tree", "SdtdManagedDir")["SdtdManagedDir"];

    Assert.True(managed.Contains("7DaysToDieServer_Data", StringComparison.OrdinalIgnoreCase),
      $"A dedicated-server tree was passed as a relative -p:SdtdDir, but SdtdManagedDir resolved to\n    {managed}\n" +
      "which is the GAME layout. This is #46's quiet failure: layout detection is an Exists() test, and an " +
      "unanchored relative path makes it probe under the project directory, where neither data folder exists — " +
      "so it silently falls through to the game-layout default and the whole build compiles against the wrong " +
      "unit. Detection must test the normalized $(_SdtdRoot), not $(SdtdDir).");
  }

  [Fact]
  public void Relative_ModsDir_deploys_to_the_invocation_directory_not_beside_the_project() {
    using var space = new Workspace();
    var project = space.ModletProject();

    // -t:Deploy, not -t:Build: the deploy destination is computed while the target RUNS, which is the whole
    // point. By then MSBuild has pointed the working directory at the project, so an unanchored relative
    // ModsDir lands beside the project instead of here.
    (var exitCode, var log) = space.Run("msbuild", project, "-nologo", "-t:Deploy", "-p:Configuration=Debug",
      "-p:ModsDir=deployed", "-nodeReuse:false");
    Assert.True(exitCode == 0, $"MSBuild -t:Deploy failed ({exitCode}):\n{log}");

    var beside = Path.Combine(space.ProjectDir, "deployed");
    Assert.False(Directory.Exists(beside),
      $"-p:ModsDir=deployed was passed from\n    {space.InvocationDir}\nbut the deploy created\n    {beside}\n" +
      "beside the project instead. That is #52 exactly: _DeployDir is computed inside the Deploy target, where " +
      "the working directory is the PROJECT's, so across the solution this scatters one folder beside every " +
      "deploying project — silently, reporting success. build\\Deploy.targets must pass " +
      "$(MSBuildStartupDirectory) as the first argument to NormalizeDirectory (an absolute $(ModsDir) then " +
      "discards it, so real deploys are unaffected).");

    var expected = Path.Combine(space.InvocationDir, "deployed", ProbeName);
    Assert.True(File.Exists(Path.Combine(expected, "ModInfo.xml")),
      $"Nothing was deployed to\n    {expected}\nwhich is where -p:ModsDir=deployed points from " +
      $"{space.InvocationDir}. Deploy log:\n{log}");
    Assert.Single(Directory.GetFileSystemEntries(Path.Combine(space.InvocationDir, "deployed")));
  }

  [Fact]
  public void The_game_tree_follows_the_declaration_and_the_install_side_does_not() {
    // The two-root split (#23): SdtdDir resolves from the DECLARED version under the repo's packages layout,
    // never from the machine's install; ModsDir and SdtdServerDir derive from the INSTALL, never from the
    // game tree. One evaluation pins both directions — a revert of the flip puts SdtdDir under the install
    // fixture, and a re-merge of the roots puts ModsDir under packages\.
    using var space = new Workspace();
    space.Fixture("install", "7DaysToDie_Data");
    (var label, var version) = DefaultDeclaration();
    IReadOnlyDictionary<string, string> resolved = space.EvaluateWith(space.CodeModProject(),
      new[] { "-p:SdtdInstallDir=install" },
      new[] { "SdtdDir", "SdtdManagedDir", "ModsDir", "SdtdServerDir" });

    var declaredTree = Path.Combine(Workspace.RepoRoot, "packages", "7dtd.assemblies.game", version);
    Assert.True(string.Equals(resolved["SdtdDir"], declaredTree, StringComparison.OrdinalIgnoreCase),
      $"SdtdDir resolved to\n    {resolved["SdtdDir"]}\nexpected the declared default {label} under the " +
      $"repo packages layout:\n    {declaredTree}\nThe game tree resolves from build\\GameVersions.props' " +
      "declaration — never from the machine's install (the #23 flip). If this points at the install " +
      "fixture, the flip was reverted; if elsewhere, resolution or the registry map changed shape.");
    foreach (var name in new[] { "ModsDir", "SdtdServerDir" }) {
      Assert.True(resolved[name].StartsWith(Path.Combine(space.InvocationDir, "install"),
          StringComparison.OrdinalIgnoreCase),
        $"{name} resolved to\n    {resolved[name]}\nexpected it under the install fixture\n    " +
        $"{Path.Combine(space.InvocationDir, "install")}\nInstall-side paths derive from " +
        "$(SdtdInstallDir), deliberately NEVER from the game tree — a redirected or declared tree must not " +
        "become a deploy destination (#52's lesson made structural). If this sits under packages\\ or " +
        "vendor\\, the roots were re-merged.");
    }
  }

  [Fact]
  public void The_tree_source_is_an_input_never_a_discovery() {
    // Decision 7 (.ai/declared-game-versions.md §1): resolution never probes both roots and picks one. The
    // repo REALLY holds this version in packages\ AND vendor\ — so each source must resolve strictly within
    // itself, and an empty packages override must NOT fall back to the vendored copy.
    using var space = new Workspace();
    (var label, var version) = DefaultDeclaration();
    var empty = Path.Combine(space.InvocationDir, "empty-packages");
    Directory.CreateDirectory(empty);

    var packages = space.EvaluateWith(space.CodeModProject(),
      new[] { "-p:SdtdPackagesDir=" + empty }, new[] { "SdtdDir" })["SdtdDir"];
    Assert.True(string.Equals(packages, Path.Combine(empty, "7dtd.assemblies.game", version),
        StringComparison.OrdinalIgnoreCase),
      $"With -p:SdtdPackagesDir pointing at an EMPTY folder, SdtdDir resolved to\n    {packages}\n" +
      "Packages mode must resolve under the given packages root even when the tree is absent there — the " +
      "repo's vendor\\ holds this very version, and probing into it would mask a broken packages path " +
      "(decision 7: the source is an input, never a discovery). The missing tree is the instructional " +
      "Mod.targets error's job, not a fallback's.");

    var vendor = space.EvaluateWith(space.CodeModProject(),
      new[] { "-p:SdtdTreeSource=vendor", "-p:SdtdUnit=dedicated-server" }, new[] { "SdtdDir" })["SdtdDir"];
    Assert.True(string.Equals(vendor, Path.Combine(Workspace.RepoRoot, "vendor", "dedicated-server", label),
        StringComparison.OrdinalIgnoreCase),
      $"With -p:SdtdTreeSource=vendor -p:SdtdUnit=dedicated-server, SdtdDir resolved to\n    {vendor}\n" +
      $"expected vendor\\dedicated-server\\{label} under the repo root: vendor mode resolves by LABEL under " +
      "the unit's own directory name (dedicated-server keeps its hyphen; only the package id drops it), " +
      "regardless of what packages\\ holds.");
  }

  [Fact]
  public void Deploy_refuses_a_live_install_on_an_undeclared_version() {
    // #37's failure mode: a live install whose Assembly-CSharp.dll matches NONE of the mod's declared
    // versions must not receive the deploy. The check compares the destination's assembly hash against the
    // declared trees under the selected source — all fabricated here, so the test needs no restore.
    using var space = new Workspace();
    (_, var version) = DefaultDeclaration();
    var install = space.FakeInstall("install", "assemblies of some OTHER game version");
    var packs = space.FakeDeclaredTree("packs", version, "assemblies of the declared version");

    (var exitCode, var log) = space.Run("msbuild", space.ModletProject(), "-nologo", "-t:Deploy",
      "-p:Configuration=Debug", $"-p:ModsDir={install}\\Mods", $"-p:SdtdPackagesDir={packs}",
      "-nodeReuse:false");

    Assert.True(exitCode != 0 && log.Contains("matches none of them"),
      $"Deploy into an install on an UNDECLARED version must refuse with the teaching error (#37) — got exit " +
      $"{exitCode}:\n{log}");
    Assert.False(Directory.Exists(Path.Combine(install, "Mods", ProbeName)),
      "the refusal must fire BEFORE the copy — a rejected deploy may not leave a partial mod folder behind.");
  }

  [Fact]
  public void Deploy_accepts_a_declared_version_install_and_the_escape_hatch_forces_a_mismatch() {
    using var space = new Workspace();
    (_, var version) = DefaultDeclaration();
    var install = space.FakeInstall("install", "assemblies of the declared version");
    var packs = space.FakeDeclaredTree("packs", version, "assemblies of the declared version");
    var project = space.ModletProject();

    (var exitCode, var log) = space.Run("msbuild", project, "-nologo", "-t:Deploy", "-p:Configuration=Debug",
      $"-p:ModsDir={install}\\Mods", $"-p:SdtdPackagesDir={packs}", "-nodeReuse:false");
    Assert.True(exitCode == 0 && File.Exists(Path.Combine(install, "Mods", ProbeName, "ModInfo.xml")),
      $"An install hash-matching a declared version's tree must deploy normally — got exit {exitCode}:\n{log}");

    File.WriteAllText(Path.Combine(install, "7DaysToDie_Data", "Managed", "Assembly-CSharp.dll"),
      "assemblies of some OTHER game version");
    (exitCode, log) = space.Run("msbuild", project, "-nologo", "-t:Deploy", "-p:Configuration=Debug",
      $"-p:ModsDir={install}\\Mods", $"-p:SdtdPackagesDir={packs}",
      "-p:SdtdSkipInstallVersionCheck=true", "-nodeReuse:false");
    Assert.True(exitCode == 0,
      $"-p:SdtdSkipInstallVersionCheck=true is the explicit escape hatch and must deploy through a mismatch " +
      $"— got exit {exitCode}:\n{log}");
  }

  [Fact]
  public void Deploy_errors_when_no_declared_tree_exists_to_verify_a_live_install_against() {
    // A live install with NOTHING to compare against is an error, not a shrug: silently skipping would turn
    // "unverifiable" into "unverified", and the message teaches the restore command.
    using var space = new Workspace();
    var install = space.FakeInstall("install", "assemblies of some game version");
    var packs = Path.Combine(space.InvocationDir, "empty-packs");
    Directory.CreateDirectory(packs);

    (var exitCode, var log) = space.Run("msbuild", space.ModletProject(), "-nologo", "-t:Deploy",
      "-p:Configuration=Debug", $"-p:ModsDir={install}\\Mods", $"-p:SdtdPackagesDir={packs}",
      "-nodeReuse:false");

    Assert.True(exitCode != 0 && log.Contains("to verify it against"),
      $"A live install with no declared trees restored must refuse as UNVERIFIABLE, teaching the restore " +
      $"command — got exit {exitCode}:\n{log}");
  }

  /// <summary>The repo default declaration, read from build\GameVersions.props so a version bump never
  ///   rots these tests: the label plus its registry package version.</summary>
  private static (string Label, string Version) DefaultDeclaration() {
    XDocument versions = XDocument.Load(Path.Combine(Workspace.RepoRoot, "build", "GameVersions.props"));
    var label = versions.Descendants().First(e => e.Name.LocalName == "SdtdDevVersion").Value;
    var version = versions.Descendants().First(e => e.Name.LocalName == "SdtdGameVersionMap").Value
      .Split(';').Select(pair => pair.Split('=')).First(pair => pair[0].Trim() == label)[1].Trim();
    return (label, version);
  }

  private const string ProbeName = "PathProbe";

  /// <summary>
  ///   A disposable temp tree holding an invocation directory and a sibling probe project, plus the plumbing to
  ///   run MSBuild against them. Siblings on purpose: a test that put the project inside the invocation
  ///   directory could not tell the two resolution bases apart.
  /// </summary>
  private sealed class Workspace : IDisposable {
    internal static readonly string RepoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    private static readonly string BuildDir = Path.Combine(RepoRoot, "build");

    private readonly string root = Directory.CreateTempSubdirectory("StrongMods-paths-").FullName;

    internal Workspace() {
      Directory.CreateDirectory(InvocationDir);
      Directory.CreateDirectory(ProjectDir);
    }

    internal string InvocationDir => Path.Combine(root, "invoked-from");
    internal string ProjectDir => Path.Combine(root, "elsewhere");

    /// <summary>
    ///   A stand-in game tree: the layout detection in build\GamePaths.props only calls Exists(), so empty
    ///   directories suffice. The zero-byte mscorlib.dll is not decoration — Microsoft.Common.CurrentVersion
    ///   .targets blanks FrameworkPathOverride to $(MSBuildFrameworkToolsPath) (empty under `dotnet msbuild`
    ///   on CoreCLR) unless mscorlib.dll is found there, which would make this test assert on "".
    /// </summary>
    internal void Fixture(string name, string dataFolder) {
      var managed = Path.Combine(InvocationDir, name, dataFolder, "Managed");
      Directory.CreateDirectory(managed);
      File.WriteAllBytes(Path.Combine(managed, "mscorlib.dll"), Array.Empty<byte>());
    }

    /// <summary>A fake live install (game layout) whose Assembly-CSharp.dll holds the given content — the
    ///   deploy version check identifies an install by that file's hash, so content IS version.</summary>
    internal string FakeInstall(string name, string assemblyContent) {
      var root = Path.Combine(InvocationDir, name);
      var managed = Path.Combine(root, "7DaysToDie_Data", "Managed");
      Directory.CreateDirectory(managed);
      File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), assemblyContent);
      return root;
    }

    /// <summary>A fake restored-packages root holding one declared game tree at the given version.</summary>
    internal string FakeDeclaredTree(string name, string packageVersion, string assemblyContent) {
      var root = Path.Combine(InvocationDir, name);
      var managed = Path.Combine(root, "7dtd.assemblies.game", packageVersion, "7DaysToDie_Data", "Managed");
      Directory.CreateDirectory(managed);
      File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), assemblyContent);
      return root;
    }

    /// <summary>
    ///   A synthetic code mod importing the real shared build files by absolute path. Synthetic rather than a
    ///   repo mod so the test pins build\Mod.props + build\GamePaths.props themselves, needs no game install,
    ///   and writes nothing into the working tree.
    /// </summary>
    internal string CodeModProject() => WriteProject($"""
      <Project Sdk="Microsoft.NET.Sdk">
        <Import Project="{BuildDir}\Mod.props" />
        <Import Project="{BuildDir}\Mod.targets" />
      </Project>
      """);

    internal string ModletProject() {
      // A modlet is the deploy subject deliberately: nothing to compile, so the test costs a file copy, and no
      // game install is involved. ModInfo.xml is both the payload and what build\XmlLint.targets checks.
      File.WriteAllText(Path.Combine(ProjectDir, "ModInfo.xml"),
        $"<xml><Name value=\"{ProbeName}\" /><Version value=\"1.0.0\" /></xml>");
      return WriteProject($"""
        <Project DefaultTargets="Build">
          <Import Project="{BuildDir}\Modlet.targets" />
        </Project>
        """);
    }

    private string WriteProject(string content) {
      var path = Path.Combine(ProjectDir, ProbeName + ".csproj");
      File.WriteAllText(path, content);
      return path;
    }

    /// <summary>Evaluates the named properties with a relative -p:SdtdDir. No restore, no target, no build.</summary>
    internal IReadOnlyDictionary<string, string> Evaluate(string project, string sdtdDir, params string[] names) =>
      EvaluateWith(project, new[] { "-p:SdtdDir=" + sdtdDir }, names);

    /// <summary>Same evaluation, arbitrary -p: overrides.</summary>
    internal IReadOnlyDictionary<string, string> EvaluateWith(string project, string[] overrides, string[] names) {
      var arguments = new List<string> { "msbuild", project, "-nologo", "-p:Configuration=Debug",
        "-getProperty:" + string.Join(',', names) };
      arguments.AddRange(overrides);
      (var exitCode, var log) = Run(arguments.ToArray());
      Assert.True(exitCode == 0, $"MSBuild evaluation failed ({exitCode}):\n{log}");

      // -getProperty prints a bare value for one name and a {"Properties":{...}} object for several.
      if (names.Length == 1) {
        return new Dictionary<string, string> { [names[0]] = log.Trim() };
      }

      var properties = new Dictionary<string, string>();
      foreach (JsonProperty property in JsonDocument.Parse(log).RootElement.GetProperty("Properties")
                 .EnumerateObject()) {
        properties[property.Name] = property.Value.GetString();
      }
      return properties;
    }

    /// <summary>Runs the dotnet CLI with InvocationDir as the working directory — the base under test.</summary>
    internal (int ExitCode, string Log) Run(params string[] arguments) {
      var info = new ProcessStartInfo("dotnet") {
        WorkingDirectory = InvocationDir, RedirectStandardOutput = true, RedirectStandardError = true
      };
      foreach (var argument in arguments) {
        info.ArgumentList.Add(argument);
      }

      var output = new StringBuilder();
      using var process = Process.Start(info);
      process.OutputDataReceived += (_, e) => output.AppendLine(e.Data);
      process.ErrorDataReceived += (_, e) => output.AppendLine(e.Data);
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
      Assert.True(process.WaitForExit(180_000), "MSBuild did not exit within 180s.");
      process.WaitForExit(); // flush the async readers
      return (process.ExitCode, output.ToString().Trim());
    }

    public void Dispose() {
      try {
        Directory.Delete(root, recursive: true);
      } catch (IOException) {
        // A build server holding a handle is not a test failure; the OS temp dir is self-cleaning.
      }
    }
  }
}

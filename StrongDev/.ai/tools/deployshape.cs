// Deploy-shape verification harness (#42, tool gap D1): runs REAL `-t:Deploy` builds of disposable probe
// projects into .scratch/deploy-shape/ roots and asserts what lands.
//
// Mechanism — how disposable sources drive the real build logic:
//   The tracked build entry points import their siblings via $(MSBuildThisFileDirectory), so they work from
//   any importing project location. A probe project synthesized under .scratch/deploy-shape/src/ therefore
//   imports the repo's build\Modlet.targets (or Overlay.props + Overlay.targets) by ABSOLUTE path, computed
//   at run time from the repo root. No tracked project source is copied or modified; probe content (ModInfo,
//   Config XML, overlay files) is generated, edited, and deleted freely between deploys. Every dotnet build
//   runs with WorkingDirectory = the repo root, so relative -p:ModsDir values anchor exactly as documented
//   in build\Deploy.targets (#46/#52).
//
// Scenarios (each is a fresh root under .scratch/deploy-shape/roots/):
//   S1  modlet mirror: content arrives; ModLoadTier prefix applied to the deploy folder name.
//   S2  modlet mirror stale deletion: file removed from source disappears from the destination —
//       observed both with a plain incremental redeploy (staging staleness!) and with Clean;Deploy.
//   S3  overlay protective-additive: a NEWER live edit at the destination survives a redeploy;
//       a newer STAGED file does overwrite an older destination file.
//   S4  overlay MirrorOnDeploy scoped mirroring: stale deletion happens ONLY inside declared scopes;
//       an unmanaged file outside the scope survives.
//   S5  empty/undeclared MirrorOnDeploy vector: nothing anywhere in the deploy root is deleted
//       (the 2026-07-30 empty-vector guard, Overlay.targets lines ~148-166).
//   S6  #37 destination version check is SKIPPED for a redirected destination with no game assembly
//       above it (every scenario above also proves this implicitly; asserted explicitly on S1 output).
//   S7  #37 refusal path: a FAKE install planted in .scratch (garbage Assembly-CSharp.dll above the
//       ModsDir) makes the deploy REFUSE with the declares-support-for error. No real install involved.
//
// Usage, from the repo root:
//   dotnet run StrongDev/.ai/tools/deployshape.cs                 run all scenarios (exit 1 on any FAIL)
//   dotnet run StrongDev/.ai/tools/deployshape.cs -- --selftest   internal checks, no dotnet build spawned
//   dotnet run StrongDev/.ai/tools/deployshape.cs -- S4 S5        run selected scenarios only
// Build logs land in .scratch/deploy-shape/logs/ for inspection.
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

var args2 = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.EndsWith(".cs")).ToArray();
return args2.Contains("--selftest")
  ? SelfTest.Run()
  : Harness.Run(args2.Where(a => !a.StartsWith("--")).ToArray());

internal sealed record Check(string Scenario, string Assertion, bool Pass, bool Observation, string Evidence);

internal static class Harness {
  public static string RepoRoot = "";
  public static string Area = "";       // .scratch/deploy-shape (absolute)
  public static readonly List<Check> Checks = new();

  public static int Run(string[] only) {
    RepoRoot = FindRepoRoot();
    if (RepoRoot == "") {
      Console.Error.WriteLine("!! run from inside the StrongMods repo (StrongMods.sln not found upward).");
      return 2;
    }
    Area = Path.Combine(RepoRoot, ".scratch", "deploy-shape");
    // Own area only: never touches sibling .scratch dirs from other efforts.
    foreach (var sub in new[] { "src", "roots", "logs" }) {
      var p = Path.Combine(Area, sub);
      if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
      Directory.CreateDirectory(p);
    }

    bool Want(string s) => only.Length == 0 || only.Contains(s, StringComparer.OrdinalIgnoreCase);
    if (Want("S1") || Want("S2")) Scenario1And2_MirrorModlet();
    if (Want("S3")) Scenario3_OverlayProtective();
    if (Want("S4")) Scenario4_ScopedMirror();
    if (Want("S5")) Scenario5_EmptyVector();
    if (Want("S7")) Scenario7_FakeInstallRefusal();
    // S6 is asserted inside Scenario1 (explicit no-refusal check on a redirected root).

    Console.WriteLine();
    Console.WriteLine("== results ==");
    var fails = 0;
    foreach (var c in Checks) {
      var tag = c.Observation ? "OBS " : c.Pass ? "PASS" : "FAIL";
      if (!c.Observation && !c.Pass) fails++;
      Console.WriteLine($"  [{tag}] {c.Scenario} — {c.Assertion}");
      Console.WriteLine($"         {c.Evidence}");
    }
    Console.WriteLine($"== {Checks.Count(c => !c.Observation && c.Pass)} pass, {fails} fail, " +
                      $"{Checks.Count(c => c.Observation)} observations ==");
    return fails == 0 ? 0 : 1;
  }

  // ---------- scenarios ----------

  static void Scenario1And2_MirrorModlet() {
    var proj = Synth.MirrorModlet(Path.Combine(Area, "src", "MirrorProbeModlet"), RepoRoot);
    var mods = "roots/mirror/Mods";
    var deployed = Path.Combine(Area, "roots", "mirror", "Mods", "ZZ_MirrorProbeModlet");

    var (code, log) = Build("s1-deploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S1", "deploy of a fresh modlet succeeds", code == 0, $"exit {code}; log logs/s1-deploy.log");
    Assert("S1", "ModLoadTier AfterDependencies maps to ZZ_ deploy-folder prefix",
      Directory.Exists(deployed), deployed);
    Assert("S1", "ModInfo.xml arrives", File.Exists(Path.Combine(deployed, "ModInfo.xml")),
      Path.Combine(deployed, "ModInfo.xml"));
    Assert("S1", "Config content arrives (keep.xml, stale.xml)",
      File.Exists(Path.Combine(deployed, "Config", "keep.xml")) &&
      File.Exists(Path.Combine(deployed, "Config", "stale.xml")), Path.Combine(deployed, "Config"));
    Assert("S6", "#37 check silently skipped: redirected root has no game assembly above it, no refusal in log",
      code == 0 && !log.Contains("declares support for") && !log.Contains("#37"),
      "roots/mirror/ contains no 7DaysToDie*_Data; deploy proceeded");

    // S2a — remove a file from SOURCE, plain incremental redeploy. Staging (bin\Debug) may retain it.
    File.Delete(Path.Combine(Area, "src", "MirrorProbeModlet", "Config", "stale.xml"));
    var (code2a, _) = Build("s2a-redeploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    var lingersAtDest = File.Exists(Path.Combine(deployed, "Config", "stale.xml"));
    var lingersInStaging = File.Exists(Path.Combine(Area, "src", "MirrorProbeModlet", "bin", "Debug", "Config", "stale.xml"));
    Observe("S2", "plain incremental -t:Deploy after source deletion: does the file survive?",
      $"exit {code2a}; at destination: {(lingersAtDest ? "STILL PRESENT" : "deleted")}; " +
      $"in bin\\Debug staging: {(lingersInStaging ? "STILL PRESENT" : "gone")}");

    // S2b — Clean;Deploy: staging rebuilt from source, mirror must delete the stale file at the destination.
    var (code2b, log2b) = Build("s2b-clean-deploy", proj, "Clean;Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S2", "after Clean;Deploy, file removed from source is deleted from the destination (mirror)",
      code2b == 0 && !File.Exists(Path.Combine(deployed, "Config", "stale.xml")),
      $"exit {code2b}; {Path.Combine(deployed, "Config", "stale.xml")} exists=" +
      File.Exists(Path.Combine(deployed, "Config", "stale.xml")));
    Assert("S2", "the deletion is announced in the build log ('removed stale')",
      log2b.Contains("removed stale"), "log logs/s2b-clean-deploy.log");
    Assert("S2", "kept file still deployed after the mirror pass",
      File.Exists(Path.Combine(deployed, "Config", "keep.xml")), Path.Combine(deployed, "Config", "keep.xml"));
  }

  static void Scenario3_OverlayProtective() {
    var proj = Synth.Overlay(Path.Combine(Area, "src", "OverlayProbePlain"), RepoRoot, mirrorConfig: false);
    var mods = "roots/plain/Mods";
    var root = Path.Combine(Area, "roots", "plain", "Mods", "OverlayProbePlain");

    var (code, _) = Build("s3-deploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S3", "initial overlay deploy succeeds and content arrives",
      code == 0 && File.Exists(Path.Combine(root, "Config", "managed_a.xml")) &&
      File.Exists(Path.Combine(root, "topfile.txt")), root);

    // Live edit, NEWER than the staged copy: must survive the redeploy.
    var live = Path.Combine(root, "topfile.txt");
    File.WriteAllText(live, "LIVE EDIT — must survive\n");
    File.SetLastWriteTimeUtc(live, DateTime.UtcNow);
    var (code3b, _) = Build("s3b-redeploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S3", "a newer live edit at the destination survives a redeploy (protective-additive)",
      code3b == 0 && File.ReadAllText(live).Contains("LIVE EDIT"),
      $"exit {code3b}; {live} content preserved={File.ReadAllText(live).Contains("LIVE EDIT")}");

    // The flip side: a newer STAGED file must overwrite an older destination copy.
    var srcFile = Path.Combine(Area, "src", "OverlayProbePlain", "Config", "managed_a.xml");
    File.WriteAllText(srcFile, "<probe updated=\"true\" />\n");
    File.SetLastWriteTimeUtc(srcFile, DateTime.UtcNow);
    var destFile = Path.Combine(root, "Config", "managed_a.xml");
    File.SetLastWriteTimeUtc(destFile, DateTime.UtcNow.AddHours(-2));
    var (code3c, _) = Build("s3c-redeploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S3", "a newer staged file overwrites an older destination copy (absent-or-newer)",
      code3c == 0 && File.ReadAllText(destFile).Contains("updated"),
      $"exit {code3c}; {destFile} updated={File.ReadAllText(destFile).Contains("updated")}");
  }

  static void Scenario4_ScopedMirror() {
    var proj = Synth.Overlay(Path.Combine(Area, "src", "OverlayProbeScoped"), RepoRoot, mirrorConfig: true);
    var mods = "roots/scoped/Mods";
    var root = Path.Combine(Area, "roots", "scoped", "Mods", "OverlayProbeScoped");

    var (code, _) = Build("s4-deploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S4", "initial scoped-overlay deploy succeeds", code == 0, $"exit {code}");

    // Plant: a stale file INSIDE the mirrored scope, and an unmanaged file OUTSIDE it.
    var staleInScope = Path.Combine(root, "Config", "planted_stale.xml");
    var unmanagedOutside = Path.Combine(root, "unmanaged_live_file.txt");
    File.WriteAllText(staleInScope, "<planted />\n");
    File.WriteAllText(unmanagedOutside, "live server file the repo does not manage\n");
    var (code4b, log4b) = Build("s4b-redeploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S4", "stale file INSIDE the declared MirrorOnDeploy scope is deleted on redeploy",
      code4b == 0 && !File.Exists(staleInScope), $"exit {code4b}; {staleInScope} exists={File.Exists(staleInScope)}");
    Assert("S4", "unmanaged file OUTSIDE the scope survives the same redeploy",
      File.Exists(unmanagedOutside), unmanagedOutside);
    Assert("S4", "scoped deletion is announced ('removed stale (mirrored scope)')",
      log4b.Contains("removed stale (mirrored scope)"), "log logs/s4b-redeploy.log");
  }

  static void Scenario5_EmptyVector() {
    // No MirrorOnDeploy declared at all — the 2026-07-30 incident shape. The guards in Overlay.targets must
    // make this purely protective-additive: nothing anywhere in the deploy root may be deleted.
    var proj = Synth.Overlay(Path.Combine(Area, "src", "OverlayProbeEmpty"), RepoRoot, mirrorConfig: false,
      name: "OverlayProbeEmpty");
    var mods = "roots/empty/Mods";
    var root = Path.Combine(Area, "roots", "empty", "Mods", "OverlayProbeEmpty");

    var (code, _) = Build("s5-deploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    Assert("S5", "initial deploy with UNDECLARED MirrorOnDeploy vector succeeds", code == 0, $"exit {code}");

    var planted = new[] {
      Path.Combine(root, "unmanaged_root.txt"),
      Path.Combine(root, "Config", "unmanaged_in_config.xml"),
      Path.Combine(root, "unmanaged_dir", "nested.dat"),
    };
    foreach (var p in planted) {
      Directory.CreateDirectory(Path.GetDirectoryName(p)!);
      File.WriteAllText(p, "unmanaged\n");
    }
    var (code5b, log5b) = Build("s5b-redeploy", proj, "Deploy", $"-p:ModsDir=.scratch/deploy-shape/{mods}");
    var survivors = planted.Count(File.Exists);
    Assert("S5", "empty-vector guard: NO planted file anywhere in the deploy root is deleted on redeploy",
      code5b == 0 && survivors == planted.Length,
      $"exit {code5b}; {survivors}/{planted.Length} planted files survive (root, inside Config, nested dir)");
    Assert("S5", "no stale-removal is announced with an empty vector",
      !log5b.Contains("removed stale"), "log logs/s5b-redeploy.log");
  }

  static void Scenario7_FakeInstallRefusal() {
    // Prove the #37 refusal path with a FAKE install entirely inside .scratch: a garbage Assembly-CSharp.dll
    // planted where a game layout would have it, one level above the redirected ModsDir. Its hash can match
    // no declared tree, so the deploy must refuse. No real install is involved at any point.
    var proj = Synth.MirrorModlet(Path.Combine(Area, "src", "RefusalProbeModlet"), RepoRoot, name: "RefusalProbeModlet");
    var fake = Path.Combine(Area, "roots", "fakeinstall");
    Directory.CreateDirectory(Path.Combine(fake, "7DaysToDie_Data", "Managed"));
    File.WriteAllBytes(Path.Combine(fake, "7DaysToDie_Data", "Managed", "Assembly-CSharp.dll"),
      Encoding.ASCII.GetBytes("not a real game assembly — deployshape.cs probe"));
    Directory.CreateDirectory(Path.Combine(fake, "Mods"));

    var (code, log) = Build("s7-refusal", proj, "Deploy", "-p:ModsDir=.scratch/deploy-shape/roots/fakeinstall/Mods");
    Assert("S7", "#37: deploy onto a version-mismatched install REFUSES (non-zero exit)",
      code != 0, $"exit {code}; log logs/s7-refusal.log");
    Assert("S7", "the refusal names the declared versions ('declares support for')",
      log.Contains("declares support for"), Excerpt(log, "declares support for"));
    Assert("S7", "nothing was deployed into the refused destination",
      !Directory.Exists(Path.Combine(fake, "Mods", "ZZ_RefusalProbeModlet")),
      Path.Combine(fake, "Mods", "ZZ_RefusalProbeModlet") + " absent");
  }

  // ---------- plumbing ----------

  public static (int code, string log) Build(string logName, string proj, string targets, params string[] props) {
    var psi = new ProcessStartInfo {
      FileName = "dotnet",
      WorkingDirectory = RepoRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    psi.ArgumentList.Add("build");
    psi.ArgumentList.Add(proj);
    psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("Debug");
    psi.ArgumentList.Add($"-t:{targets}");
    psi.ArgumentList.Add("-nologo");
    psi.ArgumentList.Add("-v:m");
    foreach (var p in props) psi.ArgumentList.Add(p);

    using var proc = Process.Start(psi)!;
    var stdout = proc.StandardOutput.ReadToEnd();
    var stderr = proc.StandardError.ReadToEnd();
    proc.WaitForExit();
    var log = stdout + (stderr.Length > 0 ? "\n-- stderr --\n" + stderr : "");
    File.WriteAllText(Path.Combine(Area, "logs", logName + ".log"),
      $"$ dotnet build {proj} -c Debug -t:{targets} {string.Join(' ', props)}\n\n{log}");
    return (proc.ExitCode, log);
  }

  public static void Assert(string scenario, string what, bool pass, string evidence) =>
    Checks.Add(new Check(scenario, what, pass, Observation: false, evidence));

  public static void Observe(string scenario, string what, string evidence) =>
    Checks.Add(new Check(scenario, what, Pass: true, Observation: true, evidence));

  static string Excerpt(string log, string needle) {
    var i = log.IndexOf(needle, StringComparison.Ordinal);
    if (i < 0) return "(needle not found in log)";
    var end = Math.Min(log.Length, i + 160);
    return log[i..end].Replace('\n', ' ').Replace('\r', ' ');
  }

  public static string FindRepoRoot() {
    var d = new DirectoryInfo(Environment.CurrentDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "StrongMods.sln"))) d = d.Parent;
    return d?.FullName ?? "";
  }
}

/// <summary>Synthesizes the disposable probe projects. Imports point at the repo's REAL build files by
/// absolute path, so the logic under test is exactly what tracked projects run.</summary>
internal static class Synth {
  public static string MirrorModlet(string dir, string repoRoot, string name = "MirrorProbeModlet") {
    Directory.CreateDirectory(Path.Combine(dir, "Config"));
    var targets = Path.Combine(repoRoot, "build", "Modlet.targets");
    var csproj = Path.Combine(dir, name + ".csproj");
    File.WriteAllText(csproj,
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
      "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
      "  <PropertyGroup>\n" +
      "    <ModLoadTier>AfterDependencies</ModLoadTier>\n" +
      "  </PropertyGroup>\n" +
      $"  <Import Project=\"{targets}\" />\n" +
      "</Project>\n");
    WriteModInfo(dir, name);
    File.WriteAllText(Path.Combine(dir, "Config", "keep.xml"), "<configs><append xpath=\"/items\" /></configs>\n");
    File.WriteAllText(Path.Combine(dir, "Config", "stale.xml"), "<configs><append xpath=\"/blocks\" /></configs>\n");
    return csproj;
  }

  public static string Overlay(string dir, string repoRoot, bool mirrorConfig, string? name = null) {
    name ??= mirrorConfig ? "OverlayProbeScoped" : "OverlayProbePlain";
    Directory.CreateDirectory(Path.Combine(dir, "Config"));
    var props = Path.Combine(repoRoot, "build", "Overlay.props");
    var targets = Path.Combine(repoRoot, "build", "Overlay.targets");
    var mirror = mirrorConfig ? "  <ItemGroup>\n    <MirrorOnDeploy Include=\"Config\" />\n  </ItemGroup>\n" : "";
    var csproj = Path.Combine(dir, name + ".csproj");
    File.WriteAllText(csproj,
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
      "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
      $"  <Import Project=\"{props}\" />\n" +
      "  <PropertyGroup>\n" +
      $"    <DeployRoot>$(ModsDir)\\{name}</DeployRoot>\n" +
      "  </PropertyGroup>\n" +
      mirror +
      $"  <Import Project=\"{targets}\" />\n" +
      "</Project>\n");
    WriteModInfo(dir, name);
    File.WriteAllText(Path.Combine(dir, "Config", "managed_a.xml"), "<probe a=\"1\" />\n");
    File.WriteAllText(Path.Combine(dir, "Config", "managed_b.xml"), "<probe b=\"2\" />\n");
    File.WriteAllText(Path.Combine(dir, "topfile.txt"), "staged top-level file\n");
    return csproj;
  }

  static void WriteModInfo(string dir, string name) {
    // UTF-8 with a byte order mark, matching the repo convention for ModInfo.xml.
    File.WriteAllText(Path.Combine(dir, "ModInfo.xml"),
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
      "<ModInfo>\n" +
      $"  <Name value=\"{name}\" />\n" +
      "  <Version value=\"0.0.1\" />\n" +
      $"  <DisplayName value=\"{name}\" />\n" +
      "  <Description value=\"Disposable deploy-shape probe (deployshape.cs). Never ship.\" />\n" +
      "  <Author value=\"str0ngh34rt\" />\n" +
      "</ModInfo>\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
  }
}

internal static class SelfTest {
  public static int Run() {
    var repoRoot = Harness.FindRepoRoot();
    if (repoRoot == "") { Console.Error.WriteLine("!! selftest must run inside the repo."); return 2; }
    var area = Path.Combine(repoRoot, ".scratch", "deploy-shape", "selftest");
    if (Directory.Exists(area)) Directory.Delete(area, recursive: true);
    Directory.CreateDirectory(area);

    var failures = 0;
    void Case(string what, bool ok) {
      Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
      if (!ok) failures++;
    }

    // Synthesized projects are well-formed XML and carry the intended shape.
    var modletProj = Synth.MirrorModlet(Path.Combine(area, "M"), repoRoot);
    var modletDoc = XDocument.Load(modletProj);
    Case("modlet csproj parses as XML", true);
    Case("modlet csproj imports the repo's real Modlet.targets by absolute path",
      modletDoc.Descendants().Any(e => e.Name.LocalName == "Import" &&
        (string?)e.Attribute("Project") == Path.Combine(repoRoot, "build", "Modlet.targets")));
    Case("modlet declares ModLoadTier before the import (extension-point position)",
      modletDoc.Root!.Elements().Select(e => e.Name.LocalName).ToList() is var kids &&
      kids.IndexOf("PropertyGroup") < kids.IndexOf("Import"));
    Case("modlet ModInfo.xml parses and names the probe",
      (string?)XDocument.Load(Path.Combine(area, "M", "ModInfo.xml"))
        .Root!.Element("Name")!.Attribute("value") == "MirrorProbeModlet");
    Case("ModInfo.xml starts with a byte order mark (U+FEFF)",
      File.ReadAllBytes(Path.Combine(area, "M", "ModInfo.xml")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));

    var scoped = XDocument.Load(Synth.Overlay(Path.Combine(area, "OS"), repoRoot, mirrorConfig: true));
    var plain = XDocument.Load(Synth.Overlay(Path.Combine(area, "OP"), repoRoot, mirrorConfig: false));
    Case("scoped overlay declares exactly one MirrorOnDeploy (Config)",
      scoped.Descendants().Count(e => e.Name.LocalName == "MirrorOnDeploy") == 1);
    Case("plain overlay declares NO MirrorOnDeploy (the empty-vector shape)",
      !plain.Descendants().Any(e => e.Name.LocalName == "MirrorOnDeploy"));
    Case("overlay sandwich order: Overlay.props import precedes DeployRoot, Overlay.targets follows it",
      scoped.Root!.Elements().Select(e => e.Name.LocalName).ToList() is var order &&
      order.First() == "Import" && order.Last() == "Import" && order.Contains("PropertyGroup"));
    Case("overlay DeployRoot references $(ModsDir), not a hardcoded path",
      scoped.Descendants().First(e => e.Name.LocalName == "DeployRoot").Value.StartsWith("$(ModsDir)"));

    Directory.Delete(area, recursive: true);
    Console.WriteLine(failures == 0 ? "selftest: all cases pass" : $"selftest: {failures} FAILURES");
    return failures == 0 ? 0 : 1;
  }
}

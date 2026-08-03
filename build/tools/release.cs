#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// The one-command publish routine (.ai/ci-feed-and-workflow.md §6): decide whether Steam has something new,
// vendor + pack + push it, apply feed retention, and update the committed pins. Human-triggered by design; the
// guardrails do the checking so the human doesn't have to do the noticing.
//
//     dotnet run build/tools/release.cs -- [--dry-run] [--steamcmd] [--steam-user <name>] [--commit]
//     dotnet run build/tools/release.cs -- --selftest
//
// Steps (§6): 1) steam_check.cs subprocess (--json contract) answers "anything to publish?" — up to date exits 0
// with no prompt, an error or a rollback aborts loudly; 2) the local install must match Steam's buildid
// (--steamcmd updates it in place; game needs --steam-user with a cached SteamCMD login, server is anonymous;
// close the Steam client first); 3) ONE prompt — the new label, validated (grammar; must move forward);
// 4) vendor.cs + pack.cs per stale unit; 5+6) [--dry-run stops here] push.cs over vendor/packages — the single
// push path: idempotent per-file pushes, retention (latest build per major.minor.patch), and GitHub
// latest-tag reconciliation all live there (it prompts for the write PAT itself, or reads
// PACKAGES_WRITE_TOKEN); 7) update build/ci/game-versions.json + build/GameAssemblies.csproj — with --commit, also
// commit them to the current branch and push (the owner's flow is direct-to-main; the green Build run on main is
// the round-trip proof).
//
// Exit codes: 0 = success (including "nothing to publish"), 2 = error/abort. Never 1 (steam_check owns that).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var dryRun = false;
var steamcmd = false;
var commit = false;
var selftest = false;
string? steamUser = null;
for (var i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--dry-run": dryRun = true; break;
    case "--steamcmd": steamcmd = true; break;
    case "--steam-user" when i + 1 < args.Length: steamUser = args[++i]; break;
    case "--commit": commit = true; break;
    case "--selftest": selftest = true; break;
    default:
      Console.Error.WriteLine(
        "usage: release.cs [--dry-run] [--steamcmd] [--steam-user <name>] [--commit] | --selftest");
      return 2;
  }
}

try {
  return selftest ? Release.Selftest() : Release.Run(dryRun, steamcmd, steamUser, commit);
} catch (Exception e) when (e is ReleaseError or IOException or JsonException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal static class Release {
  private static readonly Regex LabelRe = new(@"^V(\d+)\.(\d+)(?:\.(\d+))?-b(\d+)$");

  private sealed record UnitInfo(string AppId, string InstallName, string PackageId);

  private static readonly Dictionary<string, UnitInfo> Units = new() {
    ["game"] = new UnitInfo("251570", "7 Days To Die", "7DtD.Assemblies.Game"),
    ["dedicated-server"] = new UnitInfo("294420", "7 Days to Die Dedicated Server", "7DtD.Assemblies.DedicatedServer")
  };

  private static string RepoRoot([CallerFilePath] string src = "") {
    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, "..", ".."));
  }

  private static string ToolPath(string name) {
    return Path.Combine(RepoRoot(), "build", "tools", name);
  }

  public static int Run(bool dryRun, bool steamcmd, string? steamUser, bool commit) {
    var versionsPath = Path.Combine(RepoRoot(), "build", "ci", "game-versions.json");

    // 1. Anything to publish? steam_check owns the decision (§6b); we consume its --json + exit-code contract.
    (var checkExit, var checkOut) = RunCapture("dotnet",
      new[] { "run", ToolPath("steam_check.cs"), "--", "--live", "--published", versionsPath, "--json" });
    if (checkExit == 2 || checkExit != 0 && checkExit != 1) {
      throw new ReleaseError($"steam_check failed (exit {checkExit}) - fix that before releasing:\n{checkOut}");
    }

    var decisions = JsonDocument.Parse(checkOut).RootElement.EnumerateArray().ToList();
    if (checkExit == 0) {
      Console.WriteLine("Nothing to publish: every unit's public branch matches the published state.");
      return 0;
    }

    var stale = new List<(string Unit, long SteamBuildid, string OldLabel, string? Hint)>();
    foreach (JsonElement d in decisions) {
      var unit = d.GetProperty("unit").GetString()!;
      if (d.GetProperty("kind").GetString() == "rollback") {
        throw new ReleaseError($"{unit}: Steam's public branch moved BACKWARD (rollback) - refusing to proceed;"
                               + " investigate manually before publishing anything");
      }

      if (d.GetProperty("notify").GetBoolean()) {
        stale.Add((unit, d.GetProperty("steam_buildid").GetInt64(),
          d.GetProperty("published_label").GetString()!, d.GetProperty("version_hint").GetString()));
      }
    }

    Console.WriteLine($"To publish: {string.Join(", ", stale.Select(s => $"{s.Unit} (build {s.SteamBuildid})"))}");

    // 2. Local installs must BE the build we are about to vendor.
    foreach ((var unit, var steamBuildid, _, _) in stale) {
      var install = FindInstall(unit);
      var installBuildid = AppmanifestBuildid(unit, install);
      if (installBuildid == steamBuildid) {
        continue;
      }

      if (!steamcmd) {
        throw new ReleaseError($"{unit}: install at {install} is build {installBuildid?.ToString() ?? "unknown"},"
                               + $" Steam has {steamBuildid}. Let Steam update it, or re-run with --steamcmd"
                               + " (game also needs --steam-user; close the Steam client first).");
      }

      UpdateViaSteamCmd(unit, install, steamUser);
      installBuildid = AppmanifestBuildid(unit, install);
      if (installBuildid != steamBuildid) {
        throw new ReleaseError($"{unit}: still build {installBuildid?.ToString() ?? "unknown"} after steamcmd"
                               + $" (wanted {steamBuildid}) - aborting");
      }
    }

    // 3. The one prompt: the new label. Machine-underivable (the b# exists only in-game; §6 step 3).
    var hint = stale.Select(s => s.Hint).FirstOrDefault(h => h is not null);
    Console.WriteLine($"Steam's version-branch hint: {(hint is null ? "(none)" : $"player version {hint}")}."
                      + " The b# is on the game's main-menu screen / in the client log.");
    Console.Write($"New label (current: {string.Join(", ", stale.Select(s => $"{s.Unit}={s.OldLabel}"))}): ");
    var label = Console.ReadLine()?.Trim() ?? "";
    foreach ((var unit, _, var oldLabel, _) in stale) {
      var problem = ValidateLabel(label, oldLabel);
      if (problem is not null) {
        throw new ReleaseError($"{unit}: {problem}");
      }
    }

    // 4. Vendor + pack (each tool re-validates everything it owns).
    foreach ((var unit, _, _, _) in stale) {
      RunInherit("dotnet", new[] { "run", ToolPath("vendor.cs"), "--", "--unit", unit, "--label", label, "--force" });
      RunInherit("dotnet", new[] { "run", ToolPath("pack.cs"), "--", "--unit", unit, "--label", label });
    }

    var version = FourPart(label);
    if (dryRun) {
      Console.WriteLine($"\n--dry-run: stopping before push. Would push {version} for"
                        + $" [{string.Join(", ", stale.Select(s => s.Unit))}], apply retention, update pins.");
      return 0;
    }

    // 5+6. Push + retention + latest-tag reconciliation: push.cs is the single push path (it prompts for the
    // write PAT itself if PACKAGES_WRITE_TOKEN is unset).
    RunInherit("dotnet", new[] { "run", ToolPath("push.cs"), "--", "--dir",
      Path.Combine(RepoRoot(), "vendor", "packages") });

    // 7. Pins. game-versions.json is written whole (hand-formatted, one line per unit, matching the committed
    // style); the csproj gets a surgical version bump.
    var state = JsonDocument.Parse(File.ReadAllText(versionsPath)).RootElement.EnumerateObject()
      .ToDictionary(p => p.Name, p => p.Value.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!));
    foreach ((var unit, var steamBuildid, _, _) in stale) {
      state[unit] = new Dictionary<string, string> {
        ["label"] = label, ["buildid"] = steamBuildid.ToString(), ["package_version"] = version
      };
    }

    File.WriteAllText(versionsPath, "{\n" + string.Join(",\n", state.Select(u =>
        $"  \"{u.Key}\": {{ " + string.Join(", ", u.Value.Select(f => $"\"{f.Key}\": \"{f.Value}\"")) + " }"))
      + "\n}\n", new UTF8Encoding(false));

    var csprojPath = Path.Combine(RepoRoot(), "build", "GameAssemblies.csproj");
    var csproj = File.ReadAllText(csprojPath);
    foreach ((var unit, _, _, _) in stale) {
      csproj = BumpPin(csproj, Units[unit].PackageId, version);
    }

    File.WriteAllText(csprojPath, csproj, new UTF8Encoding(false));
    Console.WriteLine($"Pins updated to {version} for [{string.Join(", ", stale.Select(s => s.Unit))}].");

    if (commit) {
      RunInherit("git", new[] { "add", versionsPath, csprojPath });
      RunInherit("git", new[] { "commit", "-m", $"Publish {label} game-assembly packages" });
      RunInherit("git", new[] { "push" });
      Console.WriteLine("Committed and pushed - the Build run on main is the round-trip proof.");
    } else {
      Console.WriteLine("Now commit build/ci/game-versions.json and build/GameAssemblies.csproj"
                        + " (or re-run with --commit); the Build run is the round-trip proof.");
    }

    return 0;
  }

  /// Grammar plus forward movement. Returns a problem description, or null if fine.
  public static string? ValidateLabel(string newLabel, string oldLabel) {
    if (!LabelRe.IsMatch(newLabel)) {
      return $"label '{newLabel}' does not match V<major>.<minor>[.<patch>]-b<build>, e.g. V2.5-b8";
    }

    if (CompareFourPart(FourPart(newLabel), FourPart(oldLabel)) <= 0) {
      return $"label '{newLabel}' does not move forward from published '{oldLabel}' - Steam's buildid moved, so"
             + " the label must too; a re-release of an old version is a rollback and is handled manually";
    }

    return null;
  }

  public static string FourPart(string label) {
    Match m = LabelRe.Match(label);
    if (!m.Success) {
      throw new ReleaseError($"bad label '{label}'");
    }

    return $"{m.Groups[1].Value}.{m.Groups[2].Value}.{(m.Groups[3].Success ? m.Groups[3].Value : "0")}.{m.Groups[4].Value}";
  }

  private static int CompareFourPart(string a, string b) {
    var pa = a.Split('.').Select(int.Parse).ToArray();
    var pb = b.Split('.').Select(int.Parse).ToArray();
    for (var i = 0; i < 4; i++) {
      if (pa[i] != pb[i]) {
        return pa[i].CompareTo(pb[i]);
      }
    }

    return 0;
  }

  public static string BumpPin(string csproj, string packageId, string newVersion) {
    var re = new Regex($"(<PackageReference Include=\"{Regex.Escape(packageId)}\" Version=\")[^\"]+(\")");
    if (!re.IsMatch(csproj)) {
      throw new ReleaseError($"no <PackageReference> for {packageId} found in GameAssemblies.csproj");
    }

    return re.Replace(csproj, $"${{1}}{newVersion}$2");
  }

  private static void UpdateViaSteamCmd(string unit, string install, string? steamUser) {
    var login = unit == "game"
      ? new[] { "+login", steamUser ?? throw new ReleaseError("--steamcmd for the game needs --steam-user"
                                                              + " (SteamCMD's cached login; see §6 caveats)") }
      : new[] { "+login", "anonymous" };
    if (unit == "game") {
      Console.WriteLine("note: updating the client-managed install - close the Steam client first (§6 caveats).");
    }

    var argv = new List<string> { "+force_install_dir", install };
    argv.AddRange(login);
    argv.AddRange(new[] { "+app_update", Units[unit].AppId, "+quit" });
    RunInherit("steamcmd", argv.ToArray());
  }

  private static string FindInstall(string unit) {
    var info = Units[unit];
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var candidates = new List<string>();
    var env = Environment.GetEnvironmentVariable("SDTD_HOME");
    if (!string.IsNullOrEmpty(env)) {
      candidates.Add(unit == "game" ? env : env + " Dedicated Server");
    }

    candidates.Add(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common", info.InstallName));
    candidates.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", info.InstallName));
    candidates.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", info.InstallName));
    return candidates.FirstOrDefault(Directory.Exists)
           ?? throw new ReleaseError($"no {unit} install found (tried: {string.Join("; ", candidates)})");
  }

  private static long? AppmanifestBuildid(string unit, string install) {
    var acf = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(install))!,
      $"appmanifest_{Units[unit].AppId}.acf");
    if (!File.Exists(acf)) {
      return null;
    }

    Match m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
    return m.Success ? long.Parse(m.Groups[1].Value) : null;
  }

  private static (int ExitCode, string Stdout) RunCapture(string exe, string[] argv) {
    var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in argv) {
      psi.ArgumentList.Add(a);
    }

    using Process proc = Process.Start(psi) ?? throw new ReleaseError($"failed to start {exe}");
    Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
    Task<string> stderr = proc.StandardError.ReadToEndAsync();
    proc.WaitForExit();
    if (stderr.Result.Length > 0) {
      Console.Error.Write(stderr.Result);
    }

    return (proc.ExitCode, stdout.Result);
  }

  private static void RunInherit(string exe, string[] argv) {
    var psi = new ProcessStartInfo(exe);
    foreach (var a in argv) {
      psi.ArgumentList.Add(a);
    }

    using Process proc = Process.Start(psi) ?? throw new ReleaseError($"failed to start {exe}");
    proc.WaitForExit();
    if (proc.ExitCode != 0) {
      throw new ReleaseError($"{exe} {argv[0]} failed (exit {proc.ExitCode})");
    }
  }

  // --- selftest -----------------------------------------------------------------------------------------------

  public static int Selftest() {
    var checks = 0;

    void Ok(bool cond, string what) {
      if (!cond) {
        Console.Error.WriteLine($"selftest FAILED: {what}");
        Environment.Exit(1);
      }

      checks++;
    }

    // Retention checks moved to push.cs's selftest with the code (2026-07-31: push.cs is the single push path).
    Ok(ValidateLabel("V3.1.0-b15", "V3.1.0-b14") is null, "forward label accepted");
    Ok(ValidateLabel("V4.0-b1", "V3.1.0-b14") is null, "major jump accepted (missing patch = 0)");
    Ok(ValidateLabel("V3.1.0-b14", "V3.1.0-b14") is not null, "unchanged label refused when buildid moved");
    Ok(ValidateLabel("V3.1.0-b13", "V3.1.0-b14") is not null, "backward label refused");
    Ok(ValidateLabel("3.1.0-b15", "V3.1.0-b14") is not null, "bad grammar refused");

    const string fixture = "  <ItemGroup>\n"
                           + "    <PackageReference Include=\"7DtD.Assemblies.Game\" Version=\"3.1.0.14\" />\n"
                           + "    <PackageReference Include=\"7DtD.Assemblies.DedicatedServer\" Version=\"3.1.0.14\" />\n"
                           + "  </ItemGroup>\n";
    var bumped = BumpPin(fixture, "7DtD.Assemblies.Game", "3.1.0.15");
    Ok(bumped.Contains("\"7DtD.Assemblies.Game\" Version=\"3.1.0.15\"")
       && bumped.Contains("\"7DtD.Assemblies.DedicatedServer\" Version=\"3.1.0.14\""),
      "csproj bump touches only the requested package id");
    try {
      BumpPin(fixture, "7DtD.Assemblies.Nonexistent", "1.0.0.0");
      Ok(false, "bump of unknown package id must throw");
    } catch (ReleaseError) {
      Ok(true, "bump of unknown package id throws");
    }

    Console.WriteLine($"selftest: {checks} checks passed");
    return 0;
  }
}

internal class ReleaseError : Exception {
  public ReleaseError(string message) : base(message) { }
}

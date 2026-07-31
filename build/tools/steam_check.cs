#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Decide whether a new 7DtD release is available to package — the shared core for release and CI.
//
// C# file-based-app port of steam_check.py for the issue #36 side-by-side language evaluation; the Python
// original stays in place until the comparison is judged. CLI, selftest coverage, output, and exit codes are
// ported 1:1. NOT imported by MSBuild; a developer tool like vendor.py. Cross-platform.
//
//     dotnet run build/tools/steam_check.cs -- --selftest
//     dotnet run build/tools/steam_check.cs -- --live --published build/ci/game-versions.json [--json]
//     dotnet run build/tools/steam_check.cs -- --raw <captured app_info output> --published ... [--json]
//
// Noise model: Steam mints a buildid for every depot push (SteamDB shows that firehose), but players only
// receive a build once it is promoted to a *branch head*. We watch one branch per unit — `public`, the branch
// the publish routine vendors — so unpromoted builds are invisible by construction. Version-named branches
// (v3.1.0, ...) and latest_experimental are read as *context* (version hints, informational notes), never as
// notification triggers.
//
// Exit codes for --live/--raw: 0 = up to date, 1 = notification warranted, 2 = error (a broken query must fail
// the workflow red, never report "up to date").
//
// The query is anonymous (`+login anonymous`): branch metadata is public; no credentials are involved anywhere.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

bool selftest = false, live = false, asJson = false;
string? rawPath = null;
var publishedPath = "build/ci/game-versions.json";
for (var i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--selftest": selftest = true; break;
    case "--live": live = true; break;
    case "--raw" when i + 1 < args.Length: rawPath = args[++i]; break;
    case "--published" when i + 1 < args.Length: publishedPath = args[++i]; break;
    case "--json": asJson = true; break;
    default:
      Console.Error.WriteLine(
        "usage: steam_check.cs (--selftest | --live | --raw <capture>) [--published <json>] [--json]");
      return 2;
  }
}

if ((selftest ? 1 : 0) + (live ? 1 : 0) + (rawPath is null ? 0 : 1) != 1) {
  Console.Error.WriteLine("error: exactly one of --selftest, --live, --raw <capture> is required");
  return 2;
}

if (selftest) {
  return SteamCheck.Selftest();
}

try {
  Dictionary<string, Dictionary<string, string>> publishedState = SteamCheck.LoadPublished(publishedPath);
  Vdf appinfo = rawPath is not null ? SteamCheck.ParseVdf(File.ReadAllText(rawPath)) : SteamCheck.RunSteamCmd();
  return SteamCheck.Report(SteamCheck.DecideAll(appinfo, publishedState), asJson);
} catch (Exception e) when (e is SteamCheckError or IOException or JsonException
                            or FormatException or OverflowException or KeyNotFoundException) {
  // The last three cover malformed data (garbage buildid, branch without one): the exit-code contract must
  // hold even then — a crash with an arbitrary code is neither "up to date" (0) nor "notify" (1) nor error (2).
  Console.Error.WriteLine($"error: {e.Message}");
  return 2; // never let a broken query exit 0 ("up to date") or 1 ("notify")
}

internal static class SteamCheck {
  public const string WatchedBranch = "public";

  private const string VdfSample = """
                                   AppID : 251570, change number : 37658283/37658283, last change : Fri Jul 31 08:51:12 2026
                                   "251570"
                                   {
                                   	"depots"
                                   	{
                                   		"branches"
                                   		{
                                   			"public" { "buildid" "24436778" }
                                   			"v3.0.0" { "buildid" "23906531" "description" "Version  3.0.0 Stable" }
                                   		}
                                   	}
                                   }
                                   """;

  public static readonly (string Unit, string AppId)[] Units = { ("game", "251570"), ("dedicated-server", "294420") };
  private static readonly Regex VersionBranchRe = new(@"^v(\d+(?:\.\d+)*)$");

  private static readonly Regex VdfToken = new(@"""((?:\\.|[^""\\])*)""|([{}])");

  // Matches Python json.dumps(indent=1); snake_case keeps the --json key contract identical across both ports.
  private static readonly JsonSerializerOptions JsonOpts = new() {
    WriteIndented = true, IndentSize = 1, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

  /// Parse app_info_print output: quoted key/value pairs and braces. steamcmd interleaves unquoted chatter
  /// ("AppID : 251570, change number : ...") between blocks; anything outside quotes/braces is skipped.
  public static Vdf ParseVdf(string text) {
    var root = new Vdf();
    var stack = new Stack<Vdf>();
    stack.Push(root);
    string? key = null;
    foreach (Match m in VdfToken.Matches(text)) {
      if (m.Groups[2].Value == "{") {
        if (key is null) {
          throw new SteamCheckError("VDF parse: '{' with no preceding key");
        }

        var child = new Vdf();
        stack.Peek()[key] = child;
        stack.Push(child);
        key = null;
      } else if (m.Groups[2].Value == "}") {
        if (stack.Count == 1) {
          throw new SteamCheckError("VDF parse: unbalanced '}'");
        }

        stack.Pop();
      } else if (key is null) {
        key = m.Groups[1].Value;
      } else {
        stack.Peek()[key] = m.Groups[1].Value;
        key = null;
      }
    }

    if (stack.Count != 1 || key is not null) {
      throw new SteamCheckError("VDF parse: truncated input (unbalanced braces or dangling key)");
    }

    return root;
  }

  /// The typed boundary out of the VDF soup: branch name -> string fields (buildid, description, timeupdated).
  public static Branches BranchesFor(Vdf appinfo, string unit) {
    var appId = Units.First(u => u.Unit == unit).AppId;
    if (appinfo.GetValueOrDefault(appId) is not Vdf app) {
      throw new SteamCheckError($"no app_info block for {unit} (app {appId}) - steamcmd returned stale/partial data");
    }

    if ((app.GetValueOrDefault("depots") as Vdf)?.GetValueOrDefault("branches") is not Vdf branches ||
        branches.Count == 0) {
      throw new SteamCheckError($"{unit}: app_info has no depots/branches section");
    }

    return new Branches(branches.ToDictionary(
      kv => kv.Key,
      kv => ((Vdf)kv.Value).Where(f => f.Value is string).ToDictionary(f => f.Key, f => (string)f.Value)));
  }

  /// The decision. `published` is the unit's entry from game-versions.json ({'label','buildid',...}). A set
  /// Error means the caller must fail loudly — an unreadable state must never look like "up to date".
  public static Decision ShouldNotify(string unit, Dictionary<string, string>? published, Branches branches) {
    var d = new Decision { Unit = unit };
    if (branches.TryGetValue("latest_experimental", out Dictionary<string, string>? exp)) {
      d.Experimental = new Experimental
        { Buildid = long.Parse(exp["buildid"]), Description = exp.GetValueOrDefault("description") };
    }

    if (!branches.TryGetValue(WatchedBranch, out Dictionary<string, string>? watched)) {
      d.Error = $"{unit}: watched branch '{WatchedBranch}' missing from app_info";
      return d;
    }

    d.SteamBuildid = long.Parse(watched["buildid"]);
    if (published is null || !published.ContainsKey("buildid")) {
      d.Error = $"{unit}: no published state (missing unit entry or 'buildid' in game-versions.json)";
      return d;
    }

    d.PublishedBuildid = long.Parse(published["buildid"]);
    d.PublishedLabel = published.GetValueOrDefault("label");
    if (d.SteamBuildid == d.PublishedBuildid) {
      return d;
    }

    d.Notify = true;
    d.Kind = d.SteamBuildid > d.PublishedBuildid ? "release" : "rollback";
    // Version hint: a version-named branch pointing at the same build tells us the player-facing version —
    // including the hotfix case where an existing v-branch is re-pointed (observed live 2026-07-31: v3.1.0
    // re-pointed to a new buildid, description unchanged). The label still needs the human (b# is in-game only).
    foreach ((var name, Dictionary<string, string> b) in branches.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
      Match m = VersionBranchRe.Match(name);
      if (m.Success && long.TryParse(b.GetValueOrDefault("buildid", "-1"), out var bid) && bid == d.SteamBuildid) {
        d.VersionHint = m.Groups[1].Value;
        d.BranchDescription = b.GetValueOrDefault("description");
      }
    }

    return d;
  }

  public static Vdf RunSteamCmd() {
    var exe = Which("steamcmd") ?? throw new SteamCheckError("steamcmd not found on PATH");
    var argv = new List<string> { "+login", "anonymous", "+app_info_update", "1" };
    foreach (var (_, appId) in Units) {
      argv.AddRange(new[] { "+app_info_print", appId });
    }

    argv.Add("+quit");
    // steamcmd occasionally serves a stale/partial cache on the first query; one retry is the known cure.
    for (var attempt = 1;; attempt++) {
      var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
      foreach (var a in argv) {
        psi.ArgumentList.Add(a);
      }

      using Process proc = Process.Start(psi) ?? throw new SteamCheckError("failed to start steamcmd");
      Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
      Task<string> stderr = proc.StandardError.ReadToEndAsync();
      if (!proc.WaitForExit(600_000)) {
        proc.Kill(true);
        throw new SteamCheckError("steamcmd timed out after 600s");
      }

      try {
        Vdf appinfo = ParseVdf(stdout.Result);
        foreach (var (unit, _) in Units) {
          BranchesFor(appinfo, unit);
        }

        return appinfo;
      } catch (SteamCheckError) {
        if (attempt == 2) {
          throw;
        }
      }
    }
  }

  /// Minimal shutil.which: scan PATH, honoring PATHEXT on Windows (steamcmd -> steamcmd.exe).
  private static string? Which(string name) {
    var exts = OperatingSystem.IsWindows()
      ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';',
        StringSplitOptions.RemoveEmptyEntries)
      : new[] { "" };
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
               StringSplitOptions.RemoveEmptyEntries))
    foreach (var ext in exts) {
      var candidate = Path.Combine(dir, name + ext.ToLowerInvariant());
      if (File.Exists(candidate)) {
        return candidate;
      }
    }

    return null;
  }

  /// game-versions.json: unit -> {label, buildid, ...}. Field values normalize to strings (buildid may be
  /// written as a JSON number or string; both are accepted, like Python's int()).
  public static Dictionary<string, Dictionary<string, string>> LoadPublished(string path) {
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var result = new Dictionary<string, Dictionary<string, string>>();
    foreach (JsonProperty unitProp in doc.RootElement.EnumerateObject()) {
      var entry = new Dictionary<string, string>();
      foreach (JsonProperty f in unitProp.Value.EnumerateObject()) {
        entry[f.Name] = f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString()! : f.Value.GetRawText();
      }

      result[unitProp.Name] = entry;
    }

    return result;
  }

  public static List<Decision> DecideAll(Vdf appinfo, Dictionary<string, Dictionary<string, string>> publishedState) {
    return Units.Select(u =>
      ShouldNotify(u.Unit, publishedState.GetValueOrDefault(u.Unit), BranchesFor(appinfo, u.Unit))).ToList();
  }

  public static int Report(List<Decision> decisions, bool asJson) {
    if (asJson) {
      Console.WriteLine(JsonSerializer.Serialize(decisions, JsonOpts));
    } else {
      foreach (Decision d in decisions) {
        string line;
        if (d.Error is not null) {
          line = $"{d.Unit}: ERROR - {d.Error}";
        } else if (d.Notify) {
          var hint = d.VersionHint is not null ? $" (version {d.VersionHint})" : "";
          line = $"{d.Unit}: {d.Kind!.ToUpperInvariant()}{hint} - {WatchedBranch} branch at build " +
                 $"{d.SteamBuildid}, published {d.PublishedLabel} is build {d.PublishedBuildid}";
        } else {
          line = $"{d.Unit}: up to date ({d.PublishedLabel}, build {d.PublishedBuildid})";
        }

        if (d.Experimental is not null) {
          line += $"  [info: latest_experimental exists, build {d.Experimental.Buildid}]";
        }

        Console.WriteLine(line);
      }
    }

    if (decisions.Any(d => d.Error is not null)) {
      return 2;
    }

    return decisions.Any(d => d.Notify) ? 1 : 0;
  }

  // --- selftest -----------------------------------------------------------------------------------------------
  // Branch fixtures trimmed from a real capture (2026-07-31, .scratch/steam-capture/): the live state was itself
  // an edge case — a hotfix re-point of v3.1.0 with the description unchanged, public ahead of the published state.

  private static Branches RealGameBranches() {
    return new Branches {
      ["public"] = new Dictionary<string, string>
        { ["buildid"] = "24436778", ["timeupdated"] = "1785337415" },
      ["v3.1.0"] = new Dictionary<string, string>
        { ["buildid"] = "24436778", ["description"] = "Version 3.1.0 Stable", ["timeupdated"] = "1785337303" },
      ["v3.0.1"] = new Dictionary<string, string>
        { ["buildid"] = "24117861", ["description"] = "Version 3.0.1 Stable" },
      // Real quirk: double space in the description; older branches also omit timeupdated entirely.
      ["v3.0.0"] = new Dictionary<string, string>
        { ["buildid"] = "23906531", ["description"] = "Version  3.0.0 Stable" },
      ["alpha12.5"] = new Dictionary<string, string>
        { ["buildid"] = "745244", ["description"] = "Alpha 12.5 Stable" }
    };
  }

  public static int Selftest() {
    var checks = 0;

    void Ok(bool cond, string what) {
      if (!cond) {
        Console.Error.WriteLine($"selftest FAILED: {what}");
        Environment.Exit(1);
      }

      checks++;
    }

    Vdf b = ParseVdf(VdfSample).Sub("251570").Sub("depots").Sub("branches");
    Ok(b.Sub("public").Str("buildid") == "24436778", "parser: branch buildid, chatter line skipped");
    Ok(b.Sub("v3.0.0").Str("description") == "Version  3.0.0 Stable", "parser: double-space description preserved");
    try {
      ParseVdf("\"key\" { \"a\" \"b\"");
      Ok(false, "parser: truncated input must throw");
    } catch (SteamCheckError) {
      Ok(true, "parser: truncated input throws");
    }

    var publishedB13 = new Dictionary<string, string> { ["label"] = "V3.1.0-b13", ["buildid"] = "24392370" };

    Decision d = ShouldNotify("game", publishedB13, RealGameBranches());
    Ok(d.Notify && d.Kind == "release", "hotfix release detected (the real 2026-07-31 state)");
    Ok(d.VersionHint == "3.1.0", "hotfix re-point hints the same player version");

    d = ShouldNotify("game", new Dictionary<string, string> { ["label"] = "V3.1.0-b??", ["buildid"] = "24436778" },
      RealGameBranches());
    Ok(!d.Notify && d.Error is null, "up to date is quiet");

    d = ShouldNotify("game", new Dictionary<string, string> { ["label"] = "V9.9-b9", ["buildid"] = "99999999" },
      RealGameBranches());
    Ok(d.Notify && d.Kind == "rollback" && d.VersionHint == "3.1.0",
      "rollback flagged, hint names the version rolled back TO");

    Branches future = RealGameBranches();
    future["public"] = new Dictionary<string, string> { ["buildid"] = "25000000" };
    future["v3.2.0"] = new Dictionary<string, string>
      { ["buildid"] = "25000000", ["description"] = "Version 3.2.0 Stable" };
    d = ShouldNotify("game", publishedB13, future);
    Ok(d.Notify && d.VersionHint == "3.2.0", "new version branch hints the new version");

    d = ShouldNotify("game", publishedB13, new Branches { ["v3.1.0"] = RealGameBranches()["v3.1.0"] });
    Ok(d.Error is not null && !d.Notify, "missing watched branch is an error, not silence");

    d = ShouldNotify("game", null, RealGameBranches());
    Ok(d.Error is not null && !d.Notify, "missing published state is an error, not silence");

    Branches exp = RealGameBranches();
    exp["latest_experimental"] = new Dictionary<string, string>
      { ["buildid"] = "25100000", ["description"] = "V3.2 Experimental" };
    d = ShouldNotify("game", new Dictionary<string, string> { ["label"] = "V3.1.0-b??", ["buildid"] = "24436778" },
      exp);
    Ok(!d.Notify && d.Experimental!.Buildid == 25100000, "experimental is informational only, never a trigger");

    Console.WriteLine($"selftest: {checks} checks passed");
    return 0;
  }
}

internal class SteamCheckError : Exception {
  public SteamCheckError(string message) : base(message) { }
}

/// A parsed VDF node: values are string leaves or nested Vdf nodes. Sub/Str are the navigation casts.
internal class Vdf : Dictionary<string, object> {
  public Vdf Sub(string key) {
    return (Vdf)this[key];
  }

  public string Str(string key) {
    return (string)this[key];
  }
}

/// Branch name -> string fields, e.g. ["public"]["buildid"]. Named to keep signatures readable.
internal class Branches : Dictionary<string, Dictionary<string, string>> {
  public Branches() { }
  public Branches(IDictionary<string, Dictionary<string, string>> src) : base(src) { }
}

/// One unit's verdict; serialized with snake_case names to match the Python --json contract exactly.
internal class Decision {
  public string Unit { get; init; } = "";
  public bool Notify { get; set; }
  public string? Kind { get; set; }
  public string? Error { get; set; }
  public long? SteamBuildid { get; set; }
  public long? PublishedBuildid { get; set; }
  public string? PublishedLabel { get; set; }
  public string? VersionHint { get; set; }
  public string? BranchDescription { get; set; }
  public Experimental? Experimental { get; set; }
}

internal class Experimental {
  public long Buildid { get; init; }
  public string? Description { get; init; }
}

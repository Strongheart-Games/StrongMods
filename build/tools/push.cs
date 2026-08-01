#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Push every .nupkg in a directory to the private feed, idempotently, then make the feed tidy: apply the
// retention policy and fix GitHub's "latest" tag. The single push path — release.cs delegates here, and
// backfills (.ai/ci-feed-and-workflow.md §7c) run it directly. C# file-based app; NOT imported by MSBuild.
//
//     dotnet run build/tools/push.cs -- [--dir <dir>] [--dry-run]     (default dir: vendor/packages)
//     dotnet run build/tools/push.cs -- --selftest
//
// Behavior, in order (design: plan §6 + the 2026-07-31 latest-tag research/experiment):
//   1. Scan the directory; read each nupkg's id+version from its embedded nuspec (never parsed from filenames).
//   2. Push in ASCENDING version order with --skip-duplicate: re-runs are safe, and each file reports
//      "pushed" or "skipped (already on feed)". The feed's 409-on-same-version stays our idempotency backstop.
//   3. Retention (per package id on the feed): keep only the highest build within each major.minor.patch,
//      delete the rest via the Packages API (write PAT needs delete:packages). Old x.y.z keys survive — the
//      #37 lagging-mod guarantee.
//   4. Latest-tag reconciliation: GitHub marks the most recently *created* version as "latest" (not the highest
//      version - verified empirically 2026-07-31). If the highest version on the feed is not the most recent,
//      fix it by delete + re-push of the highest — proven to mint a fresh created_at and reclaim the tag.
//      Only done when that exact nupkg is in the directory (never delete without the replacement in hand);
//      the pinned version is briefly absent from the feed (~2 s) — a local, human-triggered moment, not CI's.
//
// Token: PACKAGES_WRITE_TOKEN environment variable, else a masked prompt. Never stored, never in argv.
// Exit codes: 0 = success, 2 = any failure.

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

var dir = (string?)null;
var dryRun = false;
var selftest = false;
for (var i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--dir" when i + 1 < args.Length: dir = args[++i]; break;
    case "--dry-run": dryRun = true; break;
    case "--selftest": selftest = true; break;
    default:
      Console.Error.WriteLine("usage: push.cs [--dir <dir>] [--dry-run] | --selftest");
      return 2;
  }
}

try {
  return selftest ? Push.Selftest() : Push.Run(dir, dryRun);
} catch (Exception e) when (e is PushError or IOException or JsonException or InvalidDataException
                            or HttpRequestException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal static class Push {
  private const string Org = "Strongheart-Games";
  private const string FeedUrl = $"https://nuget.pkg.github.com/{Org}/index.json";

  private static string RepoRoot([CallerFilePath] string src = "") {
    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, "..", ".."));
  }

  public static int Run(string? dirArg, bool dryRun) {
    var dir = Path.GetFullPath(dirArg ?? Path.Combine(RepoRoot(), "vendor", "packages"));
    List<(string File, string Id, string Version)> local = Directory.Exists(dir)
      ? Directory.GetFiles(dir, "*.nupkg").Select(f => {
        (var id, var version) = ReadNuspecIdentity(f);
        return (f, id, version);
      }).OrderBy(p => p.id, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.version, VersionComparer).ToList()
      : throw new PushError($"no such directory: {dir}");
    if (local.Count == 0) {
      throw new PushError($"no .nupkg files in {dir}");
    }

    Console.WriteLine($"{dir}: {local.Count} package file(s), pushing in ascending version order.");
    if (dryRun) {
      foreach ((var file, var id, var version) in local) {
        Console.WriteLine($"  would push {id} {version} ({Path.GetFileName(file)})");
      }

      Console.WriteLine("--dry-run: stopping before push/retention/reconciliation.");
      return 0;
    }

    var token = Environment.GetEnvironmentVariable("PACKAGES_WRITE_TOKEN") ?? PromptMasked("write PAT: ");
    foreach ((var file, var id, var version) in local) {
      var output = RunDotnetNugetPush(file, token);
      var skipped = output.Contains("already been pushed");
      Console.WriteLine($"  {id} {version}: {(skipped ? "skipped (already on feed)" : "pushed")}");
    }

    foreach (var id in local.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase)) {
      Tidy(id, local, token);
    }

    return 0;
  }

  /// Retention + latest reconciliation for one package id, from one fresh listing.
  private static void Tidy(string id, List<(string File, string Id, string Version)> local, string token) {
    Dictionary<string, long> byName = ListVersions(id, token);

    foreach (var name in RetentionDeletions(byName.Keys)) {
      DeleteVersion(id, byName[name], token);
      Console.WriteLine($"  {id}: retention - deleted {name} (superseded within its major.minor.patch)");
      byName.Remove(name);
    }

    // The feed lists versions newest-created first; the UI's "latest" is that first row (verified 2026-07-31).
    var newestCreated = ListVersionsOrdered(id, token).First();
    var highest = byName.Keys.OrderByDescending(v => v, VersionComparer).First();
    if (newestCreated == highest) {
      Console.WriteLine($"  {id}: latest tag already correct ({highest})");
      return;
    }

    var replacement = local.FirstOrDefault(p =>
      p.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && p.Version == highest).File;
    if (replacement is null) {
      Console.WriteLine($"  {id}: WARNING - GitHub shows {newestCreated} as latest but {highest} is higher;"
                        + $" cannot fix without {id}.{highest}.nupkg in the directory (vendor+pack it, re-run)");
      return;
    }

    DeleteVersion(id, byName[highest], token);
    var output = RunDotnetNugetPush(replacement, token);
    if (output.Contains("already been pushed")) {
      throw new PushError($"{id} {highest}: re-push after delete was refused as duplicate - the feed did not"
                          + " release the version number; RESTORE IT: POST"
                          + $" /orgs/{Org}/packages/nuget/{id}/versions/<id>/restore (30-day window)");
    }

    var top = ListVersionsOrdered(id, token).First();
    if (top != highest) {
      throw new PushError($"{id}: re-pushed {highest} but the feed still lists {top} first - investigate");
    }

    Console.WriteLine($"  {id}: latest tag fixed - {highest} re-pushed (was showing {newestCreated})");
  }

  /// id+version from the nuspec INSIDE the nupkg — filenames are ambiguous (ids contain dots).
  public static (string Id, string Version) ReadNuspecIdentity(string nupkgPath) {
    using ZipArchive zip = ZipFile.OpenRead(nupkgPath);
    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec"))
                            ?? throw new PushError($"{nupkgPath}: no nuspec at package root");
    using Stream s = entry.Open();
    var doc = XDocument.Load(s);
    XNamespace ns = doc.Root!.Name.Namespace;
    XElement meta = doc.Root.Element(ns + "metadata") ?? throw new PushError($"{nupkgPath}: nuspec has no metadata");
    return (meta.Element(ns + "id")!.Value, meta.Element(ns + "version")!.Value);
  }

  /// The §3 policy (moved here from release.cs — push.cs is the single push path): keep the highest build
  /// within each major.minor.patch; unparsable versions are never deleted.
  public static List<string> RetentionDeletions(IEnumerable<string> versions) {
    var parsed = versions.Select(v => (Version: v, Parts: v.Split('.'))).ToList();
    return parsed.Where(p => p.Parts.Length == 4 && p.Parts.All(x => int.TryParse(x, out _)))
      .GroupBy(p => string.Join(".", p.Parts.Take(3)))
      .SelectMany(g => g.OrderByDescending(p => int.Parse(p.Parts[3])).Skip(1))
      .Select(p => p.Version).ToList();
  }

  public static readonly IComparer<string> VersionComparer = Comparer<string>.Create((a, b) => {
    var pa = a.Split('.');
    var pb = b.Split('.');
    for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++) {
      var na = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
      var nb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
      if (na != nb) {
        return na.CompareTo(nb);
      }
    }

    return 0;
  });

  private static HttpClient Api(string token) {
    var http = new HttpClient();
    http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
    http.DefaultRequestHeaders.Add("User-Agent", "StrongMods-push");
    http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    return http;
  }

  private static string VersionsUrl(string id) {
    return $"https://api.github.com/orgs/{Org}/packages/nuget/{id}/versions?per_page=100";
  }

  private static Dictionary<string, long> ListVersions(string id, string token) {
    return ListRaw(id, token).ToDictionary(v => v.Name, v => v.Id);
  }

  /// Names in the API's own order: newest created_at first — the order the "latest" tag follows.
  private static List<string> ListVersionsOrdered(string id, string token) {
    return ListRaw(id, token).Select(v => v.Name).ToList();
  }

  private static List<(string Name, long Id)> ListRaw(string id, string token) {
    using HttpClient http = Api(token);
    HttpResponseMessage resp = http.GetAsync(VersionsUrl(id)).Result;
    if (!resp.IsSuccessStatusCode) {
      throw new PushError($"listing {id} versions failed: HTTP {(int)resp.StatusCode}"
                          + $" ({resp.Content.ReadAsStringAsync().Result})");
    }

    return JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result).RootElement.EnumerateArray()
      .Select(v => (v.GetProperty("name").GetString()!, v.GetProperty("id").GetInt64())).ToList();
  }

  private static void DeleteVersion(string id, long versionId, string token) {
    using HttpClient http = Api(token);
    HttpResponseMessage resp = http.DeleteAsync(
      $"https://api.github.com/orgs/{Org}/packages/nuget/{id}/versions/{versionId}").Result;
    if (!resp.IsSuccessStatusCode) {
      throw new PushError($"deleting {id} version id {versionId} failed: HTTP {(int)resp.StatusCode}"
                          + " (does the PAT have delete:packages?)");
    }
  }

  private static string RunDotnetNugetPush(string file, string token) {
    var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in new[]
               { "nuget", "push", file, "--source", FeedUrl, "--api-key", token, "--skip-duplicate" }) {
      psi.ArgumentList.Add(a);
    }

    using Process proc = Process.Start(psi) ?? throw new PushError("failed to start dotnet");
    Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
    Task<string> stderr = proc.StandardError.ReadToEndAsync();
    proc.WaitForExit();
    var combined = stdout.Result + stderr.Result;
    if (proc.ExitCode != 0) {
      throw new PushError($"dotnet nuget push {Path.GetFileName(file)} failed (exit {proc.ExitCode}):\n{combined}");
    }

    return combined;
  }

  private static string PromptMasked(string prompt) {
    Console.Write(prompt);
    var sb = new StringBuilder();
    while (true) {
      ConsoleKeyInfo k = Console.ReadKey(true);
      if (k.Key == ConsoleKey.Enter) {
        Console.WriteLine();
        return sb.ToString();
      }

      if (k.Key == ConsoleKey.Backspace) {
        if (sb.Length > 0) {
          sb.Length--;
        }
      } else if (!char.IsControl(k.KeyChar)) {
        sb.Append(k.KeyChar);
      }
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

    // Retention checks moved with the code from release.cs (plan V8 case verbatim).
    List<string> del = RetentionDeletions(new[] { "3.0.0.259", "3.0.1.4", "3.0.1.7" });
    Ok(del.Count == 1 && del[0] == "3.0.1.4", "retention deletes exactly the superseded build (plan V8 case)");
    Ok(RetentionDeletions(new[] { "3.1.0.14" }).Count == 0, "retention keeps a sole version");
    Ok(RetentionDeletions(new[] { "3.1.0.13", "3.1.0.14", "4.0.0.1" }).SequenceEqual(new[] { "3.1.0.13" }),
      "retention preserves other major.minor.patch keys (the lagging-mod guarantee, #37)");
    Ok(RetentionDeletions(new[] { "3.1.0.14", "weird" }).Count == 0, "unparsable versions are never deleted");

    var sorted = new[] { "3.1.0.14", "3.0.1.4", "10.0.0.1", "3.1.0.2" }.OrderBy(v => v, VersionComparer).ToList();
    Ok(sorted.SequenceEqual(new[] { "3.0.1.4", "3.1.0.2", "3.1.0.14", "10.0.0.1" }),
      "version sort is numeric per part (14 > 2, 10 > 3), not lexicographic");

    var highest = new[] { "3.0.1.4", "3.1.0.14" }.OrderByDescending(v => v, VersionComparer).First();
    Ok(highest == "3.1.0.14", "highest-version selection for latest reconciliation");

    var nupkg = Path.Combine(RepoRoot(), ".scratch", "pushselftest.nupkg.zip");
    Directory.CreateDirectory(Path.GetDirectoryName(nupkg)!);
    File.Delete(nupkg);
    using (ZipArchive zip = ZipFile.Open(nupkg, ZipArchiveMode.Create)) {
      using Stream s = zip.CreateEntry("Test.Pkg.nuspec").Open();
      var bytes = Encoding.UTF8.GetBytes(
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">"
        + "<metadata><id>Test.Pkg</id><version>1.2.3.4</version></metadata></package>");
      s.Write(bytes);
    }

    (var id, var version) = ReadNuspecIdentity(nupkg);
    Ok(id == "Test.Pkg" && version == "1.2.3.4", "id+version read from the embedded nuspec, not the filename");
    File.Delete(nupkg);

    Console.WriteLine($"selftest: {checks} checks passed");
    return 0;
  }
}

internal class PushError : Exception {
  public PushError(string message) : base(message) { }
}

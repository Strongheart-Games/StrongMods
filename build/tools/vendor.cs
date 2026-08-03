#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Vendor a releasable unit's assemblies into vendor/<unit>/<label>/ for game-free builds and tests.
//
// C# port of vendor.py (retired with it; #36 language decision) — CLI, tree layout, manifest schema, and nuspec
// stub are 1:1, verified by manifest-diff equivalence at port time. NOT imported by MSBuild. Cross-platform.
//
//     dotnet run build/tools/vendor.cs -- --unit game --label V2.5-b8
//     dotnet run build/tools/vendor.cs -- --unit dedicated-server --label V2.5-b8 [--force]
//
// Copies every DLL in the unit's Managed directory, plus the entire Mods/0_TFP_Harmony and Data/Config folders,
// into a tree that mirrors the source install exactly (the dedicated server keeps its 7DaysToDieServer_Data
// name; build/GamePaths.props detects either layout), so the build consumes it with nothing more than
// -p:SdtdDir=vendor/<unit>/<label> (one-off), or -p:SdtdTreeSource=vendor to resolve declared versions from
// vendor\ (#23's temporary pre-publish escape; packages is canonical). Whole folders, never a cherry-pick:
// the game's 0Harmony is a thin build whose MonoMod/Cecil siblings are required to execute Harmony code
// outside the game (#48), and Data/Config is the vanilla XML/CSV that patch-application and localization
// tests read through $(SdtdDir) (#59). Deploy destinations never follow the game tree (the two-root split),
// so building against a tree cannot deploy into it.
//
// The label is the human coordinate (the in-game "V 2.5 b8" as V2.5-b8); machine provenance — Steam buildid,
// betakey (informational), source paths, per-file SHA-256 — lands in manifest.json beside the tree. A nuspec
// stub is written for pack.cs. The data-dir name is how a unit is VERIFIED, not guessed: a game-layout install
// offered as the dedicated server (or vice versa) fails loudly, never silently vendors a mislabeled tree.
//
// Install discovery: --install-dir, else SDTD_HOME (game; "<SDTD_HOME> Dedicated Server" for the server,
// matching build/GamePaths.props), else the default Steam locations for the current platform.
//
// Exit codes: 0 = success, 2 = any failure.
//
// LEGAL: the output contains the game's licensed assemblies. It must never be committed (the repo is public) or
// published anywhere public. vendor/ is gitignored; keep it that way. See .ai/ci-feed-and-workflow.md.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string? unit = null, label = null, installDirArg = null, outputRoot = null;
var force = false;
for (var i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--unit" when i + 1 < args.Length: unit = args[++i]; break;
    case "--label" when i + 1 < args.Length: label = args[++i]; break;
    case "--install-dir" when i + 1 < args.Length: installDirArg = args[++i]; break;
    case "--output-root" when i + 1 < args.Length: outputRoot = args[++i]; break;
    case "--force": force = true; break;
    default:
      Console.Error.WriteLine(
        "usage: vendor.cs --unit (game|dedicated-server) --label V<maj>.<min>[.<patch>]-b<build>"
        + " [--install-dir <dir>] [--output-root <dir>] [--force]");
      return 2;
  }
}

if (unit is null || label is null) {
  Console.Error.WriteLine("error: --unit and --label are required");
  return 2;
}

try {
  Vendor.Run(unit, label, installDirArg, outputRoot, force);
  return 0;
} catch (Exception e) when (e is VendorError or IOException or UnauthorizedAccessException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal static class Vendor {
  private static readonly Regex LabelRe = new(@"^V(\d+)\.(\d+)(?:\.(\d+))?-b(\d+)$");

  private sealed record UnitInfo(string AppId, string InstallName, string DataDir, string PackageId);

  private static readonly Dictionary<string, UnitInfo> Units = new() {
    ["game"] = new UnitInfo("251570", "7 Days To Die", "7DaysToDie_Data", "7DtD.Assemblies.Game"),
    ["dedicated-server"] = new UnitInfo("294420", "7 Days to Die Dedicated Server", "7DaysToDieServer_Data",
      "7DtD.Assemblies.DedicatedServer")
  };

  private static string RepoRoot([CallerFilePath] string src = "") {
    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, "..", ".."));
  }

  public static void Run(string unit, string label, string? installDirArg, string? outputRootArg, bool force) {
    if (!Units.TryGetValue(unit, out UnitInfo? info)) {
      throw new VendorError($"unknown unit '{unit}' (expected: {string.Join(", ", Units.Keys)})");
    }

    Match m = LabelRe.Match(label);
    if (!m.Success) {
      throw new VendorError($"label '{label}' does not match V<major>.<minor>[.<patch>]-b<build>, e.g. V2.5-b8");
    }

    var install = FindInstall(unit, info, installDirArg);
    var managed = Path.Combine(install, info.DataDir, "Managed");
    if (!Directory.Exists(managed)) {
      throw new VendorError($"{install} does not look like a {unit} install (expected {info.DataDir}/Managed)");
    }

    var harmonyDir = Path.Combine(install, "Mods", "0_TFP_Harmony");
    if (!File.Exists(Path.Combine(harmonyDir, "0Harmony.dll"))) {
      throw new VendorError($"{Path.Combine(harmonyDir, "0Harmony.dll")} not found");
    }

    var configDir = Path.Combine(install, "Data", "Config");
    if (!Directory.Exists(configDir)) {
      throw new VendorError($"{configDir} not found");
    }

    var dest = Path.Combine(outputRootArg ?? Path.Combine(RepoRoot(), "vendor"), unit, label);
    if (Directory.Exists(dest)) {
      if (!force) {
        throw new VendorError($"{dest} already exists (use --force to regenerate)");
      }

      Directory.Delete(dest, true);
    }

    var files = new Dictionary<string, Dictionary<string, object>>();
    void VendorFile(string src, string rel) {
      var outPath = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
      File.Copy(src, outPath);
      files[rel] = new Dictionary<string, object> {
        ["size"] = new FileInfo(src).Length,
        ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(src)))
      };
    }

    foreach (var dll in Directory.GetFiles(managed, "*.dll").OrderBy(p => p, StringComparer.Ordinal)) {
      VendorFile(dll, $"{info.DataDir}/Managed/{Path.GetFileName(dll)}");
    }

    // The whole folder, not just 0Harmony.dll — see the header comment (#48).
    foreach (var file in Directory.GetFiles(harmonyDir, "*", SearchOption.AllDirectories)
               .OrderBy(p => p, StringComparer.Ordinal)) {
      VendorFile(file, $"Mods/0_TFP_Harmony/{Path.GetRelativePath(harmonyDir, file).Replace('\\', '/')}");
    }

    // Vanilla XML/CSV, wholesale for the same reason — see the header comment (#59).
    foreach (var file in Directory.GetFiles(configDir, "*", SearchOption.AllDirectories)
               .OrderBy(p => p, StringComparer.Ordinal)) {
      VendorFile(file, $"Data/Config/{Path.GetRelativePath(configDir, file).Replace('\\', '/')}");
    }

    (string? acf, string? buildid, string? betakey) = SteamProvenance(info, install);
    var manifest = new Dictionary<string, object?> {
      ["unit"] = unit,
      ["label"] = label,
      ["package_id"] = info.PackageId,
      ["generated_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz"),
      ["source_install"] = install,
      ["source_managed"] = managed,
      ["steam"] = new Dictionary<string, string?> { ["appmanifest"] = acf, ["buildid"] = buildid, ["betakey"] = betakey },
      ["data_dir"] = info.DataDir,
      ["files"] = files
    };
    var jsonOpts = new JsonSerializerOptions { WriteIndented = true, IndentSize = 1 };
    File.WriteAllText(Path.Combine(dest, "manifest.json"),
      JsonSerializer.Serialize(manifest, jsonOpts) + "\n", new UTF8Encoding(false));

    var patch = m.Groups[3].Success ? m.Groups[3].Value : "0";
    var fourPart = $"{m.Groups[1].Value}.{m.Groups[2].Value}.{patch}.{m.Groups[4].Value}";
    File.WriteAllText(Path.Combine(dest, $"{info.PackageId}.nuspec"), $"""
      <?xml version="1.0" encoding="utf-8"?>
      <!-- Stub for the CI packaging step (private feed ONLY — contents are licensed game files, never publish
           publicly). Version derived from label {label}; buildid in manifest.json is the exact-depot arbiter. -->
      <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
        <metadata>
          <id>{info.PackageId}</id>
          <version>{fourPart}</version>
          <authors>The Fun Pimps (assemblies); packaged by str0ngh34rt for private use</authors>
          <description>7 Days to Die {unit} assemblies, {label}, for compiling and testing StrongMods without a game
          install. Private use only; not redistributable.</description>
        </metadata>
      </package>

      """.Replace("\r\n", "\n"), new UTF8Encoding(false));

    var totalMb = files.Values.Sum(f => (long)f["size"]) / 1048576.0;
    Console.WriteLine($"{unit} {label}: {files.Count} files, {totalMb:F1} MB -> {dest}");
    Console.WriteLine($"  buildid={buildid ?? "null"}  betakey={betakey ?? "(default branch)"}");
  }

  private static string FindInstall(string unit, UnitInfo info, string? explicitDir) {
    if (explicitDir is not null) {
      if (!Directory.Exists(explicitDir)) {
        throw new VendorError($"--install-dir does not exist: {explicitDir}");
      }

      return explicitDir;
    }

    var candidates = new List<string>();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var env = Environment.GetEnvironmentVariable("SDTD_HOME");
    if (!string.IsNullOrEmpty(env)) {
      candidates.Add(unit == "game" ? env : env + " Dedicated Server");
    }

    candidates.Add(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common", info.InstallName));
    candidates.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", info.InstallName));
    candidates.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", info.InstallName));
    foreach (var c in candidates) {
      if (Directory.Exists(c)) {
        return c;
      }
    }

    throw new VendorError($"no {unit} install found. Tried:\n  " + string.Join("\n  ", candidates)
                          + "\nPass --install-dir or set SDTD_HOME.");
  }

  /// buildid/betakey from the appmanifest. Two layouts: a Steam-library install keeps it two levels up
  /// (<library>/steamapps/appmanifest_X.acf beside common/), while a SteamCMD +force_install_dir install keeps
  /// it INSIDE the install (<install>/steamapps/appmanifest_X.acf) — the backfill case. Absent -> nulls.
  private static (string? Acf, string? Buildid, string? Betakey) SteamProvenance(UnitInfo info, string install) {
    var acf = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(install))!, $"appmanifest_{info.AppId}.acf");
    if (!File.Exists(acf)) {
      acf = Path.Combine(install, "steamapps", $"appmanifest_{info.AppId}.acf");
    }

    if (!File.Exists(acf)) {
      return (null, null, null);
    }

    var text = File.ReadAllText(acf);
    string? Field(string name) {
      Match fm = Regex.Match(text, $"\"{name}\"\\s+\"([^\"]*)\"");
      return fm.Success ? fm.Groups[1].Value : null;
    }

    return (acf, Field("buildid"), Field("betakey"));
  }
}

internal class VendorError : Exception {
  public VendorError(string message) : base(message) { }
}

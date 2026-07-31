#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Turn a vendored tree (build/tools/vendor.py) into its private NuGet package, with manifest.json as the
// arbiter of what may be packed. C# file-based app; NOT imported by MSBuild. See .ai/ci-feed-and-workflow.md §2.
//
//     dotnet run build/tools/pack.cs -- --unit game --label V3.1.0-b13
//     dotnet run build/tools/pack.cs -- --verify-tree <tree-or-extracted-package-root>
//     dotnet run build/tools/pack.cs -- --selftest
//
// Packing validates before it packs: manifest coordinates must match the requested unit+label, the nuspec stub's
// version must equal the label's four-part mapping (V3.1.0-b13 -> 3.1.0.13, recomputed independently), every
// manifested file must hash-match (a drifted tree is never packed), unmanifested files fail loudly, and a
// nuspec carrying a <repository> element is refused — a GitHub package linked to the public repo would inherit
// its PUBLIC visibility, and that is the leak the whole design guards against (§1). The definitive nuspec is
// derived fresh from the manifest (stub metadata + explicit <files>, plus manifest.json itself so the package
// self-describes); the stub in the tree stays vendor.py's untouched output.
//
// --verify-tree runs the same hash verification standalone against a vendored tree OR a restored package root
// (CI runs it after every restore; §4). Extra files are tolerated there — NuGet extraction adds its own — but
// never at pack time.
//
// Exit codes: 0 = success, 2 = any failure (aligned with steam_check.cs: errors are never ambiguous).
//
// LEGAL: the output .nupkg contains the game's licensed assemblies. Private feed ONLY, never a public one,
// never committed, never an Actions artifact. vendor/ (including vendor/packages/) is gitignored; keep it that way.

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

string? unit = null, label = null, verifyTreePath = null, vendorRoot = null, outputDir = null;
var selftest = false;
for (var i = 0; i < args.Length; i++) {
  switch (args[i]) {
    case "--selftest": selftest = true; break;
    case "--unit" when i + 1 < args.Length: unit = args[++i]; break;
    case "--label" when i + 1 < args.Length: label = args[++i]; break;
    case "--verify-tree" when i + 1 < args.Length: verifyTreePath = args[++i]; break;
    case "--vendor-root" when i + 1 < args.Length: vendorRoot = args[++i]; break;
    case "--output" when i + 1 < args.Length: outputDir = args[++i]; break;
    default:
      Console.Error.WriteLine("usage: pack.cs (--selftest | --verify-tree <path> | --unit <unit> --label <label>)"
                              + " [--vendor-root <dir>] [--output <dir>]");
      return 2;
  }
}

var packMode = unit is not null || label is not null;
if ((selftest ? 1 : 0) + (verifyTreePath is null ? 0 : 1) + (packMode ? 1 : 0) != 1
    || (packMode && (unit is null || label is null))) {
  Console.Error.WriteLine("error: exactly one of --selftest, --verify-tree <path>, or --unit <unit> with"
                          + " --label <label> is required");
  return 2;
}

try {
  if (selftest) {
    return Pack.Selftest();
  }

  if (verifyTreePath is not null) {
    Pack.VerifyTree(verifyTreePath, false);
    return 0;
  }

  Pack.PackTree(unit!, label!, vendorRoot, outputDir);
  return 0;
} catch (Exception e) when (e is PackError or IOException or JsonException or XmlException
                              or InvalidDataException or FormatException or KeyNotFoundException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal static class Pack {
  private static readonly Regex LabelRe = new(@"^V(\d+)\.(\d+)(?:\.(\d+))?-b(\d+)$");

  public static readonly Dictionary<string, string> UnitPackageIds = new() {
    ["game"] = "7DtD.Assemblies.Game", ["dedicated-server"] = "7DtD.Assemblies.DedicatedServer"
  };

  private static string RepoRoot([CallerFilePath] string src = "") {
    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(src)!, "..", ".."));
  }

  /// V3.1.0-b13 -> 3.1.0.13; V2.5-b8 -> 2.5.0.8. Recomputed here independently of vendor.py's stub so the two
  /// implementations cross-check each other at validation time.
  public static string FourPart(string lbl) {
    Match m = LabelRe.Match(lbl);
    if (!m.Success) {
      throw new PackError($"label '{lbl}' does not match V<major>.<minor>[.<patch>]-b<build>, e.g. V2.5-b8");
    }

    var patch = m.Groups[3].Success ? m.Groups[3].Value : "0";
    return $"{m.Groups[1].Value}.{m.Groups[2].Value}.{patch}.{m.Groups[4].Value}";
  }

  /// The leak guard (§1): a
  /// <repository>
  ///   element (or RepositoryUrl metadata) links the published package to a
  ///   repository, and a package linked to the public repo inherits PUBLIC visibility.
  public static void EnsureNoRepository(XDocument nuspec, string what) {
    if (nuspec.Descendants().Any(e => e.Name.LocalName is "repository" or "RepositoryUrl")) {
      throw new PackError($"{what} contains a <repository>/RepositoryUrl element - refusing to pack:"
                          + " a package linked to the public repo would inherit PUBLIC visibility");
    }
  }

  public static Manifest LoadManifest(string treePath) {
    var path = Path.Combine(treePath, "manifest.json");
    if (!File.Exists(path)) {
      throw new PackError($"{path} not found - not a vendored tree / extracted package root?");
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = doc.RootElement;
    var files = new Dictionary<string, (long Size, string Sha256)>();
    foreach (JsonProperty f in root.GetProperty("files").EnumerateObject()) {
      files[f.Name] = (f.Value.GetProperty("size").GetInt64(), f.Value.GetProperty("sha256").GetString()!);
    }

    JsonElement steam = root.GetProperty("steam");
    return new Manifest(
      root.GetProperty("unit").GetString()!,
      root.GetProperty("label").GetString()!,
      root.GetProperty("package_id").GetString()!,
      steam.GetProperty("buildid").ValueKind == JsonValueKind.String ? steam.GetProperty("buildid").GetString() : null,
      files);
  }

  /// Every manifested file must exist with the manifested size and SHA-256; at pack time (failOnExtras) any
  /// file the manifest does not list — beyond manifest.json and the nuspec stub — also fails: both directions
  /// of drift (tampered bits, stray deploys into the tree) must be loud, never packed or silently ignored.
  public static void VerifyFiles(string treePath, Manifest manifest, bool failOnExtras) {
    foreach (var (rel, (size, sha)) in manifest.Files) {
      var path = Path.Combine(treePath, rel.Replace('/', Path.DirectorySeparatorChar));
      if (!File.Exists(path)) {
        throw new PackError($"manifested file missing from tree: {rel}");
      }

      using FileStream fs = File.OpenRead(path);
      if (fs.Length != size) {
        throw new PackError($"size mismatch for {rel}: manifest {size}, tree {fs.Length}");
      }

      var actual = Convert.ToHexStringLower(SHA256.HashData(fs));
      if (actual != sha) {
        throw new PackError($"SHA-256 mismatch for {rel}: tree has drifted from its manifest - regenerate with"
                            + $" vendor.py (manifest {sha[..12]}..., tree {actual[..12]}...)");
      }
    }

    if (!failOnExtras) {
      return;
    }

    foreach (var abs in Directory.EnumerateFiles(treePath, "*", SearchOption.AllDirectories)) {
      var rel = Path.GetRelativePath(treePath, abs).Replace(Path.DirectorySeparatorChar, '/');
      if (!manifest.Files.ContainsKey(rel) && rel != "manifest.json" &&
          !(rel == Path.GetFileName(abs) && rel.EndsWith(".nuspec"))) {
        throw new PackError($"unmanifested file in tree: {rel} (stray deploy or manual edit) - regenerate with"
                            + " vendor.py or remove it; nothing unmanifested is ever packed");
      }
    }
  }

  /// The definitive nuspec: stub metadata verbatim (id/version/authors/description, after validation) plus an
  /// explicit
  /// <files> list — exactly the manifested set, plus manifest.json so the package self-describes.
  public static XDocument BuildFinalNuspec(XDocument stub, Manifest manifest, string expectedVersion) {
    XNamespace ns = stub.Root!.Name.Namespace;
    XElement meta = stub.Root.Element(ns + "metadata")
                    ?? throw new PackError("nuspec stub has no <metadata> element");
    var stubId = meta.Element(ns + "id")?.Value;
    var stubVersion = meta.Element(ns + "version")?.Value;
    if (stubId != manifest.PackageId) {
      throw new PackError($"nuspec stub id '{stubId}' != manifest package_id '{manifest.PackageId}'");
    }

    if (stubVersion != expectedVersion) {
      throw new PackError($"nuspec stub version '{stubVersion}' != label-derived '{expectedVersion}' -"
                          + " stub and label disagree; regenerate the tree with vendor.py");
    }

    var files = new XElement(ns + "files");
    foreach (var rel in manifest.Files.Keys.Order(StringComparer.Ordinal).Append("manifest.json")) {
      files.Add(new XElement(ns + "file",
        new XAttribute("src", rel.Replace('/', Path.DirectorySeparatorChar)),
        new XAttribute("target", rel)));
    }

    return new XDocument(new XElement(ns + "package", new XElement(meta), files));
  }

  public static void PackTree(string unitName, string lbl, string? vendorRootArg, string? outputArg) {
    if (!UnitPackageIds.TryGetValue(unitName, out var packageId)) {
      throw new PackError($"unknown unit '{unitName}' (expected: {string.Join(", ", UnitPackageIds.Keys)})");
    }

    var version = FourPart(lbl);
    var vendor = vendorRootArg ?? Path.Combine(RepoRoot(), "vendor");
    var tree = Path.Combine(vendor, unitName, lbl);
    if (!Directory.Exists(tree)) {
      throw new PackError($"vendored tree not found: {tree} (generate it with vendor.py)");
    }

    Manifest manifest = LoadManifest(tree);
    if (manifest.Unit != unitName || manifest.Label != lbl) {
      throw new PackError($"manifest says unit '{manifest.Unit}' label '{manifest.Label}' but the tree sits at"
                          + $" {unitName}/{lbl} - coordinates disagree; regenerate with vendor.py");
    }

    if (manifest.PackageId != packageId) {
      throw new PackError($"manifest package_id '{manifest.PackageId}' != expected '{packageId}'");
    }

    if (manifest.Buildid is null) {
      Console.WriteLine("warning: manifest has no Steam buildid (non-Steam source install?) - provenance is"
                        + " weaker; the label alone identifies this tree");
    }

    var stubPath = Path.Combine(tree, $"{packageId}.nuspec");
    if (!File.Exists(stubPath)) {
      throw new PackError($"nuspec stub not found: {stubPath}");
    }

    var stub = XDocument.Load(stubPath);
    EnsureNoRepository(stub, "nuspec stub");
    VerifyFiles(tree, manifest, true);

    XDocument final = BuildFinalNuspec(stub, manifest, version);
    var scratch = Path.Combine(RepoRoot(), ".scratch", "pack", $"{unitName}-{lbl}");
    Directory.CreateDirectory(scratch);
    var nuspecPath = Path.Combine(scratch, $"{packageId}.nuspec");
    final.Save(nuspecPath);
    // An empty stub project: with NuspecFile set, pack takes everything from the nuspec; the project exists
    // only because `dotnet pack` needs one. netstandard2.0 compiles from SDK-bundled refs, no restore traffic.
    var stubProj = Path.Combine(scratch, "PackStub.csproj");
    File.WriteAllText(stubProj, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n"
                                + "    <TargetFramework>netstandard2.0</TargetFramework>\n"
                                + "  </PropertyGroup>\n</Project>\n");

    var output = outputArg ?? Path.Combine(vendor, "packages");
    Directory.CreateDirectory(output);
    RunDotnet("pack", stubProj, $"-p:NuspecFile={nuspecPath}", $"-p:NuspecBasePath={tree}",
      "-p:NoPackageAnalysis=true", "-o", output, "--nologo", "-v:minimal");

    var nupkg = Path.Combine(output, $"{packageId}.{version}.nupkg");
    if (!File.Exists(nupkg)) {
      throw new PackError($"dotnet pack succeeded but {nupkg} was not produced");
    }

    VerifyNupkg(nupkg, manifest);
    var mb = manifest.Files.Values.Sum(f => f.Size) / 1048576.0;
    Console.WriteLine($"{unitName} {lbl}: packed {manifest.Files.Count} files ({mb:F1} MB tree,"
                      + $" {new FileInfo(nupkg).Length / 1048576.0:F1} MB nupkg, buildid"
                      + $" {manifest.Buildid ?? "unknown"}) -> {nupkg}");
    Console.WriteLine($"  validated: coordinates, nuspec version {version}, no repository element,"
                      + $" {manifest.Files.Count}/{manifest.Files.Count} hashes, nupkg contents re-hashed");
  }

  /// Post-pack proof: the produced nupkg contains every manifested file byte-correct (hashes recomputed from
  /// the archive) plus manifest.json. What ships is what the manifest promised, not what pack happened to glob.
  public static void VerifyNupkg(string nupkgPath, Manifest manifest) {
    using ZipArchive zip = ZipFile.OpenRead(nupkgPath);
    var entries = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
    foreach (var (rel, (_, sha)) in manifest.Files) {
      if (!entries.TryGetValue(rel, out ZipArchiveEntry? entry)) {
        throw new PackError($"nupkg is missing manifested file {rel}");
      }

      using Stream s = entry.Open();
      var actual = Convert.ToHexStringLower(SHA256.HashData(s));
      if (actual != sha) {
        throw new PackError($"nupkg entry {rel} hash mismatch vs manifest");
      }
    }

    if (!entries.ContainsKey("manifest.json")) {
      throw new PackError("nupkg is missing manifest.json - restored trees would be unverifiable in CI");
    }
  }

  public static void VerifyTree(string treePath, bool failOnExtras) {
    if (!Directory.Exists(treePath)) {
      throw new PackError($"no such directory: {treePath}");
    }

    Manifest manifest = LoadManifest(treePath);
    VerifyFiles(treePath, manifest, failOnExtras);
    Console.WriteLine($"verified: {manifest.Unit} {manifest.Label} - {manifest.Files.Count}/"
                      + $"{manifest.Files.Count} files hash-match (buildid {manifest.Buildid ?? "unknown"})");
  }

  private static void RunDotnet(params string[] argv) {
    var psi = new ProcessStartInfo("dotnet")
      { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in argv) {
      psi.ArgumentList.Add(a);
    }

    using Process proc = Process.Start(psi)
                         ?? throw new PackError("failed to start dotnet");
    Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
    Task<string> stderr = proc.StandardError.ReadToEndAsync();
    if (!proc.WaitForExit(300_000)) {
      proc.Kill(true);
      throw new PackError($"dotnet {argv[0]} timed out after 300s");
    }

    if (proc.ExitCode != 0) {
      throw new PackError($"dotnet {argv[0]} failed (exit {proc.ExitCode}):\n{stdout.Result}\n{stderr.Result}");
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

    Ok(FourPart("V3.1.0-b13") == "3.1.0.13", "four-part mapping with patch");
    Ok(FourPart("V2.5-b8") == "2.5.0.8", "four-part mapping fills missing patch with 0");
    try {
      FourPart("3.1.0-b13");
      Ok(false, "bad label must throw");
    } catch (PackError) {
      Ok(true, "bad label throws");
    }

    XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    var goodStub = new XDocument(new XElement(ns + "package", new XElement(ns + "metadata",
      new XElement(ns + "id", "7DtD.Assemblies.Game"), new XElement(ns + "version", "3.1.0.13"),
      new XElement(ns + "authors", "TFP"), new XElement(ns + "description", "d"))));
    EnsureNoRepository(goodStub, "fixture");
    Ok(true, "repository-free nuspec accepted");
    var badStub = new XDocument(goodStub);
    badStub.Root!.Element(ns + "metadata")!.Add(new XElement(ns + "repository", new XAttribute("url", "x")));
    try {
      EnsureNoRepository(badStub, "fixture");
      Ok(false, "<repository> must be refused");
    } catch (PackError) {
      Ok(true, "<repository> refused - the §1 leak guard");
    }

    var manifest = new Manifest("game", "V3.1.0-b13", "7DtD.Assemblies.Game", "24392370",
      new Dictionary<string, (long, string)> {
        ["7DaysToDie_Data/Managed/A.dll"] = (3, Sha("abc")), ["Mods/0_TFP_Harmony/0Harmony.dll"] = (2, Sha("hh"))
      });
    XDocument final = BuildFinalNuspec(goodStub, manifest, "3.1.0.13");
    var targets = final.Root!.Element(ns + "files")!.Elements(ns + "file")
      .Select(f => f.Attribute("target")!.Value).ToList();
    Ok(targets.SequenceEqual(new[]
        { "7DaysToDie_Data/Managed/A.dll", "Mods/0_TFP_Harmony/0Harmony.dll", "manifest.json" }),
      "final nuspec lists exactly the manifested files plus manifest.json, in order");
    try {
      BuildFinalNuspec(goodStub, manifest, "9.9.9.9");
      Ok(false, "stub/label version disagreement must throw");
    } catch (PackError) {
      Ok(true, "stub/label version disagreement throws");
    }

    var dir = Path.Combine(RepoRoot(), ".scratch", "pack", "selftest");
    if (Directory.Exists(dir)) {
      Directory.Delete(dir, true);
    }

    Directory.CreateDirectory(Path.Combine(dir, "7DaysToDie_Data", "Managed"));
    Directory.CreateDirectory(Path.Combine(dir, "Mods", "0_TFP_Harmony"));
    File.WriteAllText(Path.Combine(dir, "7DaysToDie_Data", "Managed", "A.dll"), "abc");
    File.WriteAllText(Path.Combine(dir, "Mods", "0_TFP_Harmony", "0Harmony.dll"), "hh");
    VerifyFiles(dir, manifest, true);
    Ok(true, "pristine fixture tree verifies (strict)");
    File.WriteAllText(Path.Combine(dir, "stray.dll"), "x");
    try {
      VerifyFiles(dir, manifest, true);
      Ok(false, "unmanifested file must fail strict verification");
    } catch (PackError) {
      Ok(true, "unmanifested file fails strict verification");
    }

    VerifyFiles(dir, manifest, false);
    Ok(true, "lenient mode (restored package root) tolerates extras");
    File.WriteAllText(Path.Combine(dir, "7DaysToDie_Data", "Managed", "A.dll"), "abX");
    try {
      VerifyFiles(dir, manifest, false);
      Ok(false, "tampered byte must fail verification");
    } catch (PackError) {
      Ok(true, "tampered byte fails verification");
    }

    Directory.Delete(dir, true);
    Console.WriteLine($"selftest: {checks} checks passed");
    return 0;
  }

  private static string Sha(string content) {
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
  }
}

internal class PackError : Exception {
  public PackError(string message) : base(message) { }
}

/// The arbiter, parsed: what vendor.py recorded when it copied the tree from a licensed install.
internal record Manifest(
  string Unit,
  string Label,
  string PackageId,
  string? Buildid,
  Dictionary<string, (long Size, string Sha256)> Files);

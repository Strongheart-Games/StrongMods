using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Tests;

/// <summary>
///   The identity of one on-disk game tree — which version, which unit, where — carried by every failure
///   message. A record, not a host: it holds a label and paths and loads nothing (GameEngineHost and
///   PatcherHost are the hosts that load universes FROM the tree this record names). Label shape:
///   "V3.1.0-b14, game".
/// </summary>
public sealed record GameTree(string Label, string Root, string ManagedDir, string ConfigDir) {
  /// <summary>A registry tree: root baked by the build as SdtdTree:&lt;label&gt; for this build's unit and
  ///   source, layout name baked as SdtdUnitDataDir — MSBuild stays the only path-resolution authority; this
  ///   method only concatenates what it baked.</summary>
  public static GameTree ForLabel(string label) {
    var root = Path.GetFullPath(AssemblyMetadata.Get("SdtdTree:" + label));
    return new GameTree($"{label}, {AssemblyMetadata.Get("SdtdUnit")}", root,
      Path.Combine(root, AssemblyMetadata.Get("SdtdUnitDataDir"), "Managed"),
      Path.Combine(root, "Data", "Config"));
  }

  /// <summary>The build's default tree — what $(SdtdDir) resolved to. With an explicit -p:SdtdDir override
  ///   this is the ONE tree the suite runs against (see SmokeTestCtx.TreeIsDeclared); its label is recovered
  ///   from the tree's manifest, or from version info for a live install.</summary>
  public static GameTree Default() {
    var managedDir = AssemblyMetadata.Get("SdtdManagedDir");
    var root = Path.GetFullPath(Path.Combine(managedDir, "..", ".."));
    return new GameTree(DescribeVersion(managedDir), root, managedDir, Path.Combine(root, "Data", "Config"));
  }

  private static string DescribeVersion(string managedDir) {
    var dataDirName = new DirectoryInfo(Path.Combine(managedDir, "..")).Name;
    var unit = dataDirName.Contains("Server") ? "dedicated-server" : "game";
    var root = Path.GetFullPath(Path.Combine(managedDir, "..", ".."));

    // Vendored trees and CI package trees carry a manifest with the human version label; a live install
    // falls back to the assembly's file version.
    var manifestPath = Path.Combine(root, "manifest.json");
    if (File.Exists(manifestPath)) {
      using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (manifest.RootElement.TryGetProperty("label", out JsonElement label)) {
        return $"{label.GetString()}, {unit}";
      }
    }

    var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(managedDir, "Assembly-CSharp.dll"));
    var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
    return string.IsNullOrEmpty(version) || version.StartsWith("0.0.0")
      ? $"live install, {unit}"
      : $"{version}, {unit}";
  }
}

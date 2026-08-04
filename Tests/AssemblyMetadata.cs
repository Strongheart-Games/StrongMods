using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tests;

/// <summary>
///   Reads the key/value pairs the build baked into this test assembly — one pipe, end to end: the
///   &lt;AssemblyMetadata&gt; items in Tests.csproj write it, the BCL's AssemblyMetadataAttribute carries it,
///   this class reads it. MSBuild is the single authority for the values (resolved game-tree paths
///   especially); tests never recompute them.
/// </summary>
public static class AssemblyMetadata {
  private static readonly Dictionary<string, string> All = typeof(AssemblyMetadata).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

  public static string Get(string key) => All[key];

  /// <summary>Entries whose key starts with the prefix, keyed by the remainder — "SdtdTree:" yields
  ///   label → tree root for every registry row the build resolved for this build's unit and source.</summary>
  public static IReadOnlyDictionary<string, string> WithPrefix(string prefix) =>
    All.Where(entry => entry.Key.StartsWith(prefix))
      .ToDictionary(entry => entry.Key.Substring(prefix.Length), entry => entry.Value);
}

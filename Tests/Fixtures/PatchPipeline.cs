using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tests.Fixtures;

/// <summary>One mod's patch file applied to one entry-point document, and what the game logged doing it.</summary>
public sealed record PatchApplication(string Mod, string EntryPoint, IReadOnlyList<LogEntry> Logs) {
  public string Key => $"{Mod}/{EntryPoint}";

  public bool Clean => !Logs.Any(l => l.Level is LogLevel.Error or LogLevel.Warning or LogLevel.Exception);

  public string Summary => string.Join("; ", Logs
    .Where(l => l.Level is LogLevel.Error or LogLevel.Warning or LogLevel.Exception)
    .Select(l => $"{l.Level}: {l.Message.Split('\n')[0].Trim()}").Take(3));
}

/// <summary>A mod file under Config\ that the game will never open.</summary>
public sealed record DeadPatchFile(string Mod, string RelativePath) {
  public string Key => $"{Mod}/{RelativePath.Replace('\\', '/')}";
}

/// <summary>
///   Replays the real config-patching pipeline headlessly: every vanilla entry-point document loaded and
///   cached, then each mod's <c>Config\{entry-point}.xml</c> applied on top, in the order the patcher would.
///   Mirrors <c>BreadthFirstXmlPatcher</c>'s phases 1 and 2 — including that a patch is found only at the
///   entry point's own (possibly path-qualified) name, which is what makes a misplaced file dead (#62).
///   Not exhaustive of the real thing: mods run in directory order rather than deploy load order, and the
///   coroutine's frame yielding and ConfigDump are absent. Neither affects whether a patch applies.
/// </summary>
public sealed class PatchPipeline {
  private static readonly Regex IncludeFilename = new(@"<include\s+filename\s*=\s*""([^""]+)""");

  private PatchPipeline(IReadOnlyList<string> entryPoints, IReadOnlyList<PatchApplication> applications,
                        IReadOnlyList<DeadPatchFile> deadFiles, IReadOnlyList<string> absentFromUnit) {
    EntryPoints = entryPoints;
    Applications = applications;
    DeadFiles = deadFiles;
    AbsentFromUnit = absentFromUnit;
  }

  public IReadOnlyList<string> EntryPoints { get; }
  public IReadOnlyList<PatchApplication> Applications { get; }
  public IReadOnlyList<DeadPatchFile> DeadFiles { get; }

  /// <summary>Entry points this unit ships no document for — legal, XmlLoadInfo has an ignore-missing flag.</summary>
  public IReadOnlyList<string> AbsentFromUnit { get; }

  /// <summary>
  ///   Replays the pipeline against the host's tree. With a label, only the projects DECLARING that version
  ///   run — a mod pinned away from a game version is not installed on it, so its patches must not apply in
  ///   that version's replay (#23 phase 6b). A null label (the -p:SdtdDir escape hatch) runs every project,
  ///   as before the version axis existed.
  /// </summary>
  public static PatchPipeline Run(PatcherHost host, string label = null) {
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    IReadOnlyList<string> entryPoints = Fixtures.EntryPoints.Read(host.Tree.ManagedDir);
    GameVersionDeclarations declarations = label == null ? null : GameVersionDeclarations.Load(repoRoot);

    host.Cache.Clear();
    try {
      var vanilla = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
      var absent = new List<string>();
      foreach (var name in entryPoints) {
        var path = Path.Combine(host.VanillaConfigDir, Native(name) + ".xml");
        if (!File.Exists(path)) {
          absent.Add(name);
          continue;
        }

        object document = host.CreateXmlFile(File.ReadAllText(path), Path.GetFileName(path));
        vanilla[name] = document;
        host.Cache.Seed(name, document);
      }

      var applications = new List<PatchApplication>();
      var dead = new List<DeadPatchFile>();
      foreach (var modDir in Directory.GetDirectories(repoRoot).OrderBy(d => d, StringComparer.Ordinal)) {
        var mod = Path.GetFileName(modDir);
        var configDir = Path.Combine(modDir, "Config");
        if (!Directory.Exists(configDir) || mod == "Tests") {
          continue;
        }

        if (declarations != null) {
          var csproj = Path.Combine(modDir, mod + ".csproj");
          if (File.Exists(csproj) && !declarations.For(csproj).Test.Contains(label)) {
            continue; // pinned away from this version — not installed there, so its patches never run there
          }
        }

        var opened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in entryPoints) {
          var patchPath = Path.Combine(configDir, Native(name) + ".xml");
          if (!File.Exists(patchPath) || !vanilla.TryGetValue(name, out object target)) {
            continue;
          }

          opened.Add(Path.GetFullPath(patchPath));
          IReadOnlyList<LogEntry> logs;
          try {
            logs = host.ApplyPatchFile(target, File.ReadAllText(patchPath), Path.GetFileName(patchPath));
          } catch (Exception ex) {
            logs = new[] { new LogEntry(LogLevel.Exception, $"{ex.GetType().Name}: {ex.Message}") };
          }

          applications.Add(new PatchApplication(mod, name, logs));
        }

        dead.AddRange(DeadFilesIn(mod, configDir, opened));
      }

      return new PatchPipeline(entryPoints, applications, dead, absent);
    } finally {
      host.Cache.Clear();
    }
  }

  /// <summary>
  ///   Files under Config\ the patcher never opens: not an entry point, and not pulled in by
  ///   <c>&lt;include filename="…"&gt;</c> from one that is.
  /// </summary>
  private static IEnumerable<DeadPatchFile> DeadFilesIn(string mod, string configDir, ICollection<string> opened) {
    List<string> all = Directory.EnumerateFiles(configDir, "*.xml", SearchOption.AllDirectories)
      .Select(Path.GetFullPath).OrderBy(p => p, StringComparer.Ordinal).ToList();
    var included = new HashSet<string>(
      all.SelectMany(f => IncludeFilename.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
        .Select(v => Path.GetFullPath(Path.Combine(configDir, Native(v)))),
      StringComparer.OrdinalIgnoreCase);

    return all.Where(f => !opened.Contains(f) && !included.Contains(f))
      .Select(f => new DeadPatchFile(mod, Path.GetRelativePath(configDir, f)));
  }

  private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}

// Lints every mod for filename-casing hazards: references that resolve on a case-insensitive dev
// machine (Windows) but not on a case-sensitive production server (Linux). Prototype for #95 — the
// dev-time answer to the runtime CaseSensitiveFilesystem feature (#89/#90/#91).
//
// Grounded in the game's own loaders, read from Assembly-CSharp IL (V3.1.0 b14, game unit,
// packages/7dtd.assemblies.game/3.1.0.14). Each rule cites the load path that makes it real:
//
//   * Mod.LoadDefinitionFromFolder concatenates <folder> + "/ModInfo.xml" and calls SdFile.Exists.
//     Wrong casing on Linux: the folder is not a mod at all.
//   * XmlPatcher.LoadAndPatchConfig concatenates mod.Path + "/Config/" + <vanilla config name> and
//     calls SdFile.Exists; brfalse skips the mod for that file with NO log line. Wrong casing on
//     Linux: the patch file silently never applies. The vanilla names are the authority (all
//     lowercase, e.g. "blocks.xml").
//   * ModManager.LoadLocalizations gates on mod.Path + "/Config" via SdDirectory.Exists, then
//     Localization.LoadPatchDictionaries probes <dir> + "/Localization.csv" via SdFile.Exists.
//     Wrong casing of either segment on Linux: the mod's localization silently never loads.
//   * ModManager.LoadUiAtlases gates on mod.Path + "/UIAtlases" via SdDirectory.Exists.
//   * XmlPatchMethods.Include resolves its "filename" attribute with
//     Path.Combine(Path.GetDirectoryName(<current patch file>), filename) and calls SdFile.Exists;
//     failure at least logs "Given file not found".
//
// The disk is the authority for reference rules: a reference is flagged ONLY when a target exists
// under the mod with a DIFFERENT spelling (ordinal mismatch after a case-insensitive resolve). A
// reference that matches nothing at all is a broken-reference problem (#50 gap S3), not a casing
// problem, and is not reported here — except <include>, where the miss is cheap to see and certain.
// The tool's own comparisons never depend on the host filesystem's case sensitivity: it enumerates
// real directory entries and compares names itself, so it gives the same answer on Windows and Linux.
//
// Usage, from the repo root:
//   dotnet run StrongDev/.ai/tools/caselint.cs                 lint every mod
//   dotnet run StrongDev/.ai/tools/caselint.cs -- --selftest   run the built-in cases
//   dotnet run StrongDev/.ai/tools/caselint.cs -- <ModName>    lint one mod
// Environment: SDTD_TREE overrides the game tree whose Data/Config names are the baseline.
//              CASELINT_SELFTEST_DIR overrides where the selftest builds its throwaway fixture.
using System.Text;
using System.Text.RegularExpressions;

var arguments = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.EndsWith(".cs")).ToArray();
return arguments.Contains("--selftest")
  ? SelfTest.Run()
  : CaseLint.Run(Environment.CurrentDirectory, arguments.FirstOrDefault(a => !a.StartsWith("--")));

internal enum Severity { Error, Warn }

internal sealed record Finding(Severity Severity, string Mod, string File, int Line, string Rule, string Message);

/// <summary>How a relative path resolves against the real directory tree when case is ignored.</summary>
internal enum CaseResolve { Match, Mismatch, Missing }

internal static class CaseLint {
  /// <summary>
  ///   An attribute value that names a file: it ends in an extension the game loads from mod folders,
  ///   optionally with a Unity "?assetname" suffix. Deliberately broad — false positives are culled by
  ///   the disk-is-authority rule (only a case-insensitive hit with a different spelling is reported).
  /// </summary>
  public static readonly Regex XmlFileReference = new(
    "=\\s*\"(?<val>[^\"<>|]+\\.(?:xml|csv|txt|png|jpg|jpeg|tga|dds|wav|ogg|unity3d)(?:\\?[^\"]*)?)\"",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  /// <summary>An &lt;include filename="…"/&gt; patch command (XmlPatchMethods.Include).</summary>
  public static readonly Regex IncludeReference = new(
    "<include\\s[^>]*filename\\s*=\\s*\"(?<val>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

  /// <summary>
  ///   A C# string literal that looks like a relative file path (has an extension). Backslash is
  ///   admitted so both spellings match: a verbatim <c>@"Docs\X.xml"</c> and an escaped
  ///   <c>"Docs\\X.xml"</c> — the doubled backslash just yields an empty segment, which the resolver's
  ///   RemoveEmptyEntries split absorbs. Interpolation braces fail the resolve harmlessly.
  /// </summary>
  public static readonly Regex CodeFileReference = new(
    "\"(?<val>[A-Za-z0-9_][A-Za-z0-9_ ./\\\\-]*\\.(?:xml|csv|txt|png|jpg|dds|json|unity3d))\"",
    RegexOptions.Compiled);

  public static readonly Regex ModFolderPrefix = new(
    "^#?@modfolder\\((?<mod>[^)]*)\\)[:/]?", RegexOptions.Compiled);

  public static int Run(string repoRoot, string? only) {
    var tree = Environment.GetEnvironmentVariable("SDTD_TREE") ?? DefaultTree(repoRoot);
    var vanillaConfigDir = Path.Combine(tree, "Data", "Config");
    if (!Directory.Exists(vanillaConfigDir)) {
      Console.Error.WriteLine($"!! no vanilla Data/Config at '{vanillaConfigDir}'.");
      Console.Error.WriteLine("   Restore a game tree, or set SDTD_TREE to one.");
      return 2;
    }

    // The names XmlPatcher composes as mod.Path + "/Config/" + <name>: every vanilla config file's
    // path relative to Data/Config, forward slashes.
    var vanillaNames = Directory.EnumerateFiles(vanillaConfigDir, "*", SearchOption.AllDirectories)
      .Select(f => Path.GetRelativePath(vanillaConfigDir, f).Replace('\\', '/'))
      .ToHashSet(StringComparer.Ordinal);
    Console.WriteLine($"vanilla baseline: {vanillaConfigDir} ({vanillaNames.Count} files)");

    var mods = Directory.GetDirectories(repoRoot)
      .Where(d => !Path.GetFileName(d).StartsWith(".") && !Path.GetFileName(d).StartsWith("Template"))
      .Where(HasModInfo)
      .Where(d => only == null || string.Equals(Path.GetFileName(d), only, StringComparison.OrdinalIgnoreCase))
      .OrderBy(d => d).ToList();

    var findings = new List<Finding>();
    foreach (var modDir in mods) {
      LintMod(Path.GetFileName(modDir), modDir, repoRoot, vanillaNames, findings);
    }

    Report(findings);
    var errors = findings.Count(f => f.Severity == Severity.Error);
    Console.WriteLine($"{mods.Count} mods scanned — {errors} error(s), {findings.Count - errors} warning(s)");
    return errors > 0 ? 1 : 0;
  }

  /// <summary>Case-insensitive on purpose, so a mod whose ModInfo.xml is misspelled is still linted.</summary>
  private static bool HasModInfo(string dir) =>
    Directory.EnumerateFiles(dir)
      .Any(f => string.Equals(Path.GetFileName(f), "ModInfo.xml", StringComparison.OrdinalIgnoreCase));

  public static void LintMod(string mod, string modDir, string repoRoot, HashSet<string> vanillaNames,
                             List<Finding> into) {
    LintWellKnownName(mod, modDir, repoRoot, modDir, "ModInfo.xml", file: true, "modinfo-name",
      "Mod.LoadDefinitionFromFolder probes <folder>/ModInfo.xml with SdFile.Exists; on Linux this folder " +
      "is not recognized as a mod at all", into);
    LintWellKnownName(mod, modDir, repoRoot, modDir, "Config", file: false, "config-dir-name",
      "ModManager.LoadLocalizations and XmlPatcher.LoadAndPatchConfig probe <mod>/Config by exact name; " +
      "on Linux every patch and localization in it silently never loads", into);
    LintWellKnownName(mod, modDir, repoRoot, modDir, "UIAtlases", file: false, "uiatlases-dir-name",
      "ModManager.LoadUiAtlases probes <mod>/UIAtlases with SdDirectory.Exists; on Linux the atlases " +
      "silently never load", into);

    var configDir = Directory.GetDirectories(modDir)
      .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Config", StringComparison.OrdinalIgnoreCase));
    if (configDir != null) {
      LintWellKnownName(mod, modDir, repoRoot, configDir, "Localization.csv", file: true, "localization-name",
        "Localization.LoadPatchDictionaries probes <Config>/Localization.csv with SdFile.Exists; on Linux " +
        "the mod's localization silently never loads", into);
      LintConfigFileNames(mod, modDir, repoRoot, configDir, vanillaNames, into);
      LintXmlReferences(mod, modDir, repoRoot, configDir, into);
    }

    LintCodeLiterals(mod, modDir, repoRoot, into);
  }

  /// <summary>
  ///   A name the GAME spells (the authority is the literal in the IL): an entry that matches it
  ///   case-insensitively must match it ordinally.
  /// </summary>
  private static void LintWellKnownName(string mod, string modDir, string repoRoot, string parent,
                                        string expected, bool file, string rule, string why,
                                        List<Finding> into) {
    var entries = file ? Directory.GetFiles(parent) : Directory.GetDirectories(parent);
    foreach (var entry in entries) {
      var actual = Path.GetFileName(entry);
      if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(actual, expected, StringComparison.Ordinal)) {
        into.Add(new Finding(Severity.Error, mod, Rel(repoRoot, entry), 0, rule,
          $"'{actual}' must be spelled '{expected}': {why}"));
      }
    }
  }

  /// <summary>
  ///   Every Config file whose Config-relative path matches a vanilla config path case-insensitively
  ///   but not ordinally. The game probes mod.Path + "/Config/" + &lt;vanilla name&gt; and silently
  ///   skips the mod for that file when the probe misses.
  /// </summary>
  private static void LintConfigFileNames(string mod, string modDir, string repoRoot, string configDir,
                                          HashSet<string> vanillaNames, List<Finding> into) {
    var byFold = vanillaNames.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);
    foreach (var path in Directory.EnumerateFiles(configDir, "*", SearchOption.AllDirectories)) {
      var relative = Path.GetRelativePath(configDir, path).Replace('\\', '/');
      if (byFold.TryGetValue(relative, out var vanilla) &&
          !string.Equals(relative, vanilla, StringComparison.Ordinal)) {
        into.Add(new Finding(Severity.Error, mod, Rel(repoRoot, path), 0, "config-file-name",
          $"'{relative}' must be spelled '{vanilla}': XmlPatcher.LoadAndPatchConfig probes " +
          "mod.Path + \"/Config/\" + the vanilla name with SdFile.Exists and skips this mod's patch " +
          "with no log line when it misses — on Linux this file silently never applies"));
      }
    }
  }

  private static void LintXmlReferences(string mod, string modDir, string repoRoot, string configDir,
                                        List<Finding> into) {
    foreach (var xml in Directory.EnumerateFiles(configDir, "*.xml", SearchOption.AllDirectories)
               .OrderBy(f => f)) {
      var text = BlankComments(File.ReadAllText(xml));

      foreach (Match m in IncludeReference.Matches(text)) {
        var value = m.Groups["val"].Value;
        var line = LineOf(text, m.Index);
        // XmlPatchMethods.Include: Path.Combine(Path.GetDirectoryName(<patch file>), filename).
        switch (Resolve(Path.GetDirectoryName(xml)!, value, out var actual)) {
          case CaseResolve.Mismatch:
            into.Add(new Finding(Severity.Error, mod, Rel(repoRoot, xml), line, "include-case",
              $"<include filename=\"{value}\"> exists on disk as '{actual}'; SdFile.Exists misses it on " +
              "Linux and the game logs 'Given file not found'"));
            break;
          case CaseResolve.Missing:
            into.Add(new Finding(Severity.Warn, mod, Rel(repoRoot, xml), line, "include-missing",
              $"<include filename=\"{value}\"> matches nothing beside this file, even ignoring case"));
            break;
        }
      }

      foreach (Match m in XmlFileReference.Matches(text)) {
        var raw = m.Groups["val"].Value;
        var value = raw.Split('?')[0];
        var line = LineOf(text, m.Index);
        var root = modDir;
        Match prefix = ModFolderPrefix.Match(value);
        if (prefix.Success) {
          var target = prefix.Groups["mod"].Value;
          root = target.Length == 0 ? modDir : Path.Combine(repoRoot, target);
          value = value.Substring(prefix.Length).TrimStart('/');
          if (!Directory.Exists(root)) {
            continue; // a foreign mod not in this repo — nothing to compare against
          }
        }

        // Disk is the authority: only a case-insensitive hit with a different spelling is a finding.
        // Try the mod root first (how @modfolder and mod-relative asset paths resolve), then beside
        // the referencing file.
        if (Resolve(root, value, out var actual) == CaseResolve.Mismatch ||
            (!prefix.Success && Resolve(Path.GetDirectoryName(xml)!, value, out actual) == CaseResolve.Mismatch)) {
          into.Add(new Finding(Severity.Warn, mod, Rel(repoRoot, xml), line, "xml-path-ref",
            $"'{raw}' exists on disk as '{actual}'; a case-sensitive filesystem will not resolve it"));
        }
      }
    }
  }

  private static void LintCodeLiterals(string mod, string modDir, string repoRoot, List<Finding> into) {
    foreach (var cs in Directory.EnumerateFiles(modDir, "*.cs", SearchOption.AllDirectories)
               .Where(f => !Rel(modDir, f).StartsWith("bin/") && !Rel(modDir, f).StartsWith("obj/") &&
                           !Rel(modDir, f).StartsWith(".ai/"))
               .OrderBy(f => f)) {
      var text = File.ReadAllText(cs);
      foreach (Match m in CodeFileReference.Matches(text)) {
        var value = m.Groups["val"].Value;
        if (Resolve(modDir, value, out var actual) == CaseResolve.Mismatch) {
          into.Add(new Finding(Severity.Warn, mod, Rel(repoRoot, cs), LineOf(text, m.Index), "code-literal",
            $"\"{value}\" exists on disk as '{actual}'; a path built from it will not resolve on a " +
            "case-sensitive filesystem"));
        }
      }
    }
  }

  /// <summary>
  ///   Walks <paramref name="relative" /> segment by segment under <paramref name="root" />, matching
  ///   each segment against the real directory entries case-insensitively, and reports whether the
  ///   real spellings all match ordinally. Never consults the host's own case rules, so the answer is
  ///   identical on Windows and Linux. <paramref name="actualRelative" /> carries the real spelling on
  ///   a mismatch.
  /// </summary>
  public static CaseResolve Resolve(string root, string relative, out string actualRelative) {
    actualRelative = "";
    var segments = relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 0 || segments.Any(s => s is "." or "..")) {
      return CaseResolve.Missing;
    }

    var current = root;
    var actual = new List<string>();
    var mismatch = false;
    foreach (var expected in segments) {
      if (!Directory.Exists(current)) {
        return CaseResolve.Missing;
      }

      var entry = Directory.EnumerateFileSystemEntries(current)
        .Select(Path.GetFileName)
        .FirstOrDefault(name => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase));
      if (entry == null) {
        return CaseResolve.Missing;
      }

      mismatch |= !string.Equals(entry, expected, StringComparison.Ordinal);
      actual.Add(entry!);
      current = Path.Combine(current, entry!);
    }

    actualRelative = string.Join("/", actual);
    return mismatch ? CaseResolve.Mismatch : CaseResolve.Match;
  }

  /// <summary>Same as loclint: comments blanked, newlines kept, so line numbers hold.</summary>
  public static string BlankComments(string text) {
    var result = new StringBuilder(text.Length);
    var position = 0;
    while (true) {
      var start = text.IndexOf("<!--", position, StringComparison.Ordinal);
      if (start < 0) {
        result.Append(text, position, text.Length - position);
        return result.ToString();
      }

      result.Append(text, position, start - position);
      var end = text.IndexOf("-->", start, StringComparison.Ordinal);
      var stop = end < 0 ? text.Length : end + 3;
      for (var i = start; i < stop; i++) {
        result.Append(text[i] == '\n' ? '\n' : ' ');
      }

      position = stop;
    }
  }

  private static void Report(List<Finding> findings) {
    Console.WriteLine();
    foreach (IGrouping<string, Finding> group in findings.GroupBy(f => f.Mod).OrderBy(g => g.Key)) {
      Console.WriteLine($"== {group.Key}");
      foreach (Finding f in group.OrderBy(f => f.File).ThenBy(f => f.Line)) {
        var where = f.Line > 0 ? $"{f.File}:{f.Line}" : f.File;
        Console.WriteLine($"   {f.Severity,-5} {f.Rule,-18} {where}");
        Console.WriteLine($"         {f.Message}");
      }
    }

    Console.WriteLine();
  }

  private static string DefaultTree(string repoRoot) {
    var packages = Path.Combine(repoRoot, "packages", "7DtD.Assemblies.Game");
    if (Directory.Exists(packages)) {
      var newest = Directory.GetDirectories(packages).OrderBy(d => d).LastOrDefault();
      if (newest != null) {
        return newest;
      }
    }

    return @"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die";
  }

  private static string Rel(string root, string path) =>
    Path.GetRelativePath(root, path).Replace('\\', '/');

  private static int LineOf(string text, int index) {
    var line = 1;
    for (var i = 0; i < index && i < text.Length; i++) {
      if (text[i] == '\n') {
        line++;
      }
    }

    return line;
  }
}

internal static class SelfTest {
  public static int Run() {
    var root = Environment.GetEnvironmentVariable("CASELINT_SELFTEST_DIR")
               ?? Path.Combine(Path.GetTempPath(), "caselint-selftest");
    root = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try {
      return RunIn(root);
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  private static int RunIn(string root) {
    var failures = new List<string>();

    void Check(string what, bool ok) {
      Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
      if (!ok) {
        failures.Add(what);
      }
    }

    // A fixture mod with one hazard of every class. The fixture files carry the WRONG spellings and
    // the references carry the RIGHT ones (or vice versa) — creatable on any filesystem.
    var mod = Path.Combine(root, "FixtureMod");
    Directory.CreateDirectory(Path.Combine(mod, "Config", "XUi"));
    File.WriteAllText(Path.Combine(mod, "ModInfo.xml"), "<xml/>");
    File.WriteAllText(Path.Combine(mod, "Config", "Blocks.xml"),               // vanilla says blocks.xml
      "<configs><include filename=\"extra/More.xml\"/>" +                      // on disk: Extra/more.xml
      "<include filename=\"absent.xml\"/>" +
      "<property value=\"Resources/Pack.unity3d?icon\"/>" +                    // on disk: resources/pack.unity3d
      "<!-- <include filename=\"extra/Commented.xml\"/> --></configs>");
    Directory.CreateDirectory(Path.Combine(mod, "Config", "Extra"));
    File.WriteAllText(Path.Combine(mod, "Config", "Extra", "more.xml"), "<configs/>");
    File.WriteAllText(Path.Combine(mod, "Config", "localization.csv"), "Key,english\n");
    Directory.CreateDirectory(Path.Combine(mod, "Resources"));
    File.WriteAllText(Path.Combine(mod, "Resources", "pack.unity3d"), "");
    Directory.CreateDirectory(Path.Combine(mod, "docs"));
    File.WriteAllText(Path.Combine(mod, "docs", "notes.txt"), "");
    File.WriteAllText(Path.Combine(mod, "Code.cs"),
      "var p = mod.Path + \"Config/blocks.xml\"; var q = @\"Docs\\Notes.txt\";");

    var vanilla = new HashSet<string>(StringComparer.Ordinal) { "blocks.xml", "XUi/styles.xml" };
    var findings = new List<Finding>();
    CaseLint.LintMod("FixtureMod", mod, root, vanilla, findings);
    var rules = findings.Select(f => f.Rule).ToList();

    Check("a wrong-case vanilla config file name is an error", rules.Contains("config-file-name"));
    Check("a wrong-case Localization.csv is an error", rules.Contains("localization-name"));
    Check("a wrong-case include target is an error", rules.Contains("include-case"));
    Check("a missing include target is a warning", rules.Contains("include-missing"));
    Check("a wrong-case asset path is a warning", rules.Contains("xml-path-ref"));
    Check("a wrong-case code literal is a warning", rules.Contains("code-literal"));
    Check("an include inside a comment is invisible",
      findings.Count(f => f.Rule is "include-case" or "include-missing") == 2);
    Check("the code literal names the real spelling",
      findings.Any(f => f.Rule == "code-literal" && f.Message.Contains("Config/Blocks.xml")));
    Check("a backslash-separated code literal is checked too",
      findings.Any(f => f.Rule == "code-literal" && f.Message.Contains("docs/notes.txt")));

    // A clean mod produces no findings.
    var clean = Path.Combine(root, "CleanMod");
    Directory.CreateDirectory(Path.Combine(clean, "Config"));
    File.WriteAllText(Path.Combine(clean, "ModInfo.xml"), "<xml/>");
    File.WriteAllText(Path.Combine(clean, "Config", "blocks.xml"), "<configs/>");
    File.WriteAllText(Path.Combine(clean, "Config", "Localization.csv"), "Key,english\n");
    var cleanFindings = new List<Finding>();
    CaseLint.LintMod("CleanMod", clean, root, vanilla, cleanFindings);
    Check("a clean mod has no findings",
      cleanFindings.Count == 0 || Fail(cleanFindings));

    // A misspelled ModInfo.xml, checked separately so the fixture mod above stays discoverable.
    var broken = Path.Combine(root, "BrokenMod");
    Directory.CreateDirectory(broken);
    File.WriteAllText(Path.Combine(broken, "modinfo.xml"), "<xml/>");
    var brokenFindings = new List<Finding>();
    CaseLint.LintMod("BrokenMod", broken, root, vanilla, brokenFindings);
    Check("a wrong-case ModInfo.xml is an error", brokenFindings.Any(f => f.Rule == "modinfo-name"));

    // The resolver itself.
    Check("resolve reports a clean match",
      CaseLint.Resolve(clean, "Config/blocks.xml", out _) == CaseResolve.Match);
    Check("resolve reports the real spelling on a mismatch",
      CaseLint.Resolve(clean, "config/BLOCKS.xml", out var actual) == CaseResolve.Mismatch &&
      actual == "Config/blocks.xml");
    Check("resolve reports a genuine miss",
      CaseLint.Resolve(clean, "Config/nothing.xml", out _) == CaseResolve.Missing);
    Check("resolve refuses dot-dot traversal",
      CaseLint.Resolve(clean, "../FixtureMod/ModInfo.xml", out _) == CaseResolve.Missing);

    Check("a modfolder prefix is recognized",
      CaseLint.ModFolderPrefix.IsMatch("#@modfolder(SomeMod)/Resources/x.unity3d"));
    Check("a bare value is not a modfolder prefix",
      !CaseLint.ModFolderPrefix.IsMatch("Resources/x.unity3d"));

    Console.WriteLine();
    Console.WriteLine(failures.Count == 0 ? "selftest: all cases pass" : $"selftest: {failures.Count} FAILED");
    return failures.Count == 0 ? 0 : 1;
  }

  private static bool Fail(List<Finding> findings) {
    foreach (Finding f in findings) {
      Console.WriteLine($"       unexpected: {f.Rule} {f.File} {f.Message}");
    }

    return false;
  }
}

// Lints every mod's Config/Localization.csv, and every localization key its XML references (#50 gap S4).
//
// Grounded in the game's own loader, not in convention. Read from Localization.loadCsv's IL in
// V3.1.0 b14 (game unit); the rules below cite what decides each one:
//
//   * The header's FIRST column name is IRRELEVANT. loadCsv overwrites buffer[0] with "KEY" before the
//     check that compares buffer[0] to "KEY", so that check can never fail. "Key", "key", "Column A" —
//     all identical to the loader.
//   * A header with FEWER THAN 2 columns makes loadCsv return false with NO log line at all. The mod's
//     whole localization silently does not load.
//   * Every OTHER header column must match ^[A-Za-z]+$ or start with "context" (OrdinalIgnoreCase).
//     Anything else — a space, a hyphen, a digit — makes loadCsv log an error and REJECT THE ENTIRE
//     FILE. This is why vanilla's own "Context / Alternate Text" is legal.
//   * Column names are matched against the BASE (vanilla) header case-insensitively
//     (Extensions.EqualsCaseInsensitive), so "English" and "english" are the same column. A column the
//     vanilla header does not have is not an error — it is silently appended as a new column, which is
//     how a typo becomes a phantom language rather than a warning.
//   * addCsv logs and skips a row with an empty key, and logs and skips a DUPLICATE key.
//
// So the linter reports what the loader would do, at two severities: ERROR (the game rejects or silently
// drops it) and WARN (the game accepts it, but the outcome is not what the author meant).
//
// Usage, from the repo root:
//   dotnet run StrongDev/.ai/tools/loclint.cs                 lint every mod
//   dotnet run StrongDev/.ai/tools/loclint.cs -- --selftest   run the built-in cases
//   dotnet run StrongDev/.ai/tools/loclint.cs -- <ModName>    lint one mod
// Environment: SDTD_TREE overrides the game tree whose vanilla Localization.csv is the baseline.
using System.Text;
using System.Text.RegularExpressions;

var arguments = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.EndsWith(".cs")).ToArray();
return arguments.Contains("--selftest")
  ? SelfTest.Run()
  : LocLint.Run(Environment.CurrentDirectory, arguments.FirstOrDefault(a => !a.StartsWith("--")));

internal enum Severity { Error, Warn }

internal sealed record Finding(Severity Severity, string Mod, string File, int Line, string Rule, string Message);

internal static class LocLint {
  /// <summary>The loader's own header rule: Localization.languageHeaderMatcher.</summary>
  public static readonly Regex LanguageHeader = new("^[A-Za-z]+$", RegexOptions.Compiled);

  /// <summary>
  ///   The attributes that hold a localization key. <c>*_key</c> covers the XML convention
  ///   (<c>name_key</c>, <c>description_key</c>, <c>tooltip_key</c>, <c>display_name_key</c>); the named
  ///   ones are the PascalCase properties blocks and items use. <c>keyboard</c> and friends must not
  ///   match, hence the underscore.
  /// </summary>
  public static readonly Regex KeyReference = new(
    "(?<attr>[A-Za-z]+_key|\"(?:DescriptionKey|UnlockTooltipKey|CustomIconTintKey)\")\\s*=\\s*\"(?<val>[^\"]*)\"",
    RegexOptions.Compiled);

  /// <summary>
  ///   A <c>&lt;property name="DescriptionKey" value="…"/&gt;</c> pair, which XML spells as two
  ///   attributes rather than one. Blocks and items use both spellings of the property name — the
  ///   PascalCase <c>DescriptionKey</c> and the snake_case <c>display_name_key</c> — so both end in
  ///   "Key" or "_key" and both are matched.
  /// </summary>
  public static readonly Regex PropertyKeyReference = new(
    "name\\s*=\\s*\"(?<attr>[A-Za-z_]*(?:_key|Key))\"\\s+value\\s*=\\s*\"(?<val>[^\"]*)\"",
    RegexOptions.Compiled);

  /// <summary>The languages the game knows, from Localization.GetCurrentLocale.</summary>
  public static readonly HashSet<string> Languages = new(StringComparer.OrdinalIgnoreCase) {
    "english", "german", "spanish", "french", "italian", "japanese", "koreana", "polish", "brazilian",
    "russian", "turkish", "schinese", "tchinese"
  };

  public static int Run(string repoRoot, string? only) {
    var tree = Environment.GetEnvironmentVariable("SDTD_TREE") ?? DefaultTree(repoRoot);
    var vanillaCsv = Path.Combine(tree, "Data", "Config", "Localization.csv");
    if (!File.Exists(vanillaCsv)) {
      Console.Error.WriteLine($"!! no vanilla Localization.csv at '{vanillaCsv}'.");
      Console.Error.WriteLine("   Restore a game tree, or set SDTD_TREE to one.");
      return 2;
    }

    Csv vanilla = Csv.Load(vanillaCsv);
    var vanillaColumns = new HashSet<string>(vanilla.Header, StringComparer.OrdinalIgnoreCase);
    var knownKeys = new HashSet<string>(vanilla.Keys, StringComparer.Ordinal);
    Console.WriteLine($"vanilla baseline: {vanillaCsv}");
    Console.WriteLine($"  {vanilla.Header.Count} columns, {knownKeys.Count} keys");

    var mods = Directory.GetDirectories(repoRoot)
      .Where(d => !Path.GetFileName(d).StartsWith(".") && !Path.GetFileName(d).StartsWith("Template"))
      .Where(d => File.Exists(Path.Combine(d, "ModInfo.xml")))
      .Where(d => only == null || string.Equals(Path.GetFileName(d), only, StringComparison.OrdinalIgnoreCase))
      .OrderBy(d => d).ToList();

    var findings = new List<Finding>();
    var withCsv = 0;

    // Pass 1 — every mod's own CSV, and the union of keys the repo defines. Every mod's keys must be
    // known before any mod's references are checked: a fix mod may reference a key its sibling defines.
    foreach (var modDir in mods) {
      var mod = Path.GetFileName(modDir);
      var path = Path.Combine(modDir, "Config", "Localization.csv");
      if (!File.Exists(path)) {
        continue;
      }

      withCsv++;
      Csv csv;
      try {
        csv = Csv.Load(path);
      } catch (Exception e) {
        findings.Add(new Finding(Severity.Error, mod, Rel(repoRoot, path), 0, "unreadable", e.Message));
        continue;
      }

      LintCsv(mod, Rel(repoRoot, path), csv, vanillaColumns, findings);
      foreach (var key in csv.Keys) {
        knownKeys.Add(key);
      }
    }

    // Pass 2 — every localization key the mods' XML references.
    foreach (var modDir in mods) {
      LintReferences(Path.GetFileName(modDir), modDir, repoRoot, knownKeys, findings);
    }

    Report(findings);
    var errors = findings.Count(f => f.Severity == Severity.Error);
    Console.WriteLine($"{mods.Count} mods scanned, {withCsv} with a Localization.csv — " +
                      $"{errors} error(s), {findings.Count - errors} warning(s)");
    return errors > 0 ? 1 : 0;
  }

  public static void LintCsv(string mod, string file, Csv csv, HashSet<string> vanillaColumns,
                             List<Finding> into) {
    if (csv.Header.Count < 2) {
      into.Add(new Finding(Severity.Error, mod, file, 1, "header-too-short",
        $"the header has {csv.Header.Count} column(s); loadCsv returns false below 2, and logs NOTHING — " +
        "the mod's whole localization silently does not load"));
      return;
    }

    for (var i = 1; i < csv.Header.Count; i++) {
      var column = csv.Header[i];
      if (!LanguageHeader.IsMatch(column) &&
          !column.StartsWith("context", StringComparison.OrdinalIgnoreCase)) {
        into.Add(new Finding(Severity.Error, mod, file, 1, "illegal-column",
          $"column {i + 1} '{column}' is neither ^[A-Za-z]+$ nor context-prefixed; loadCsv logs an error " +
          "and REJECTS THE WHOLE FILE"));
      } else if (!vanillaColumns.Contains(column)) {
        into.Add(new Finding(Severity.Warn, mod, file, 1, "unknown-column",
          $"column {i + 1} '{column}' is not in the vanilla header; the loader silently appends it as a " +
          "new column rather than reporting it, so a typo becomes a phantom language"));
      }
    }

    if (!csv.Header.Skip(1).Any(Languages.Contains)) {
      into.Add(new Finding(Severity.Warn, mod, file, 1, "no-language-column",
        "no column matches a language the game knows (english, german, …), so this file supplies no text"));
    }

    var seen = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach ((var key, var line, var fields) in csv.Rows) {
      if (string.IsNullOrWhiteSpace(key)) {
        into.Add(new Finding(Severity.Error, mod, file, line, "empty-key",
          "addCsv logs 'Entry missing a key!' and skips the row"));
        continue;
      }

      if (seen.TryGetValue(key, out var first)) {
        into.Add(new Finding(Severity.Error, mod, file, line, "duplicate-key",
          $"'{key}' was already defined on line {first}; addCsv logs 'Duplicate key' and skips this row"));
      } else {
        seen[key] = line;
      }

      if (fields > csv.Header.Count) {
        into.Add(new Finding(Severity.Warn, mod, file, line, "extra-fields",
          $"{fields} fields against a {csv.Header.Count}-column header — the surplus has no column"));
      }
    }
  }

  private static void LintReferences(string mod, string modDir, string repoRoot, HashSet<string> knownKeys,
                                     List<Finding> into) {
    var configDir = Path.Combine(modDir, "Config");
    if (!Directory.Exists(configDir)) {
      return;
    }

    foreach (var xml in Directory.EnumerateFiles(configDir, "*.xml", SearchOption.AllDirectories)
               .OrderBy(f => f)) {
      var text = BlankComments(File.ReadAllText(xml));
      foreach (Regex pattern in new[] { KeyReference, PropertyKeyReference }) {
        foreach (Match m in pattern.Matches(text)) {
          var attribute = m.Groups["attr"].Value.Trim('"');
          foreach (var key in m.Groups["val"].Value.Split(',').Select(k => k.Trim())
                     .Where(k => k.Length > 0)) {
            if (knownKeys.Contains(key)) {
              continue;
            }

            into.Add(new Finding(Severity.Error, mod, Rel(repoRoot, xml), LineOf(text, m.Index),
              "unresolved-key",
              $"{attribute}=\"{key}\" resolves to no key in vanilla Localization.csv or any repo mod's"));
          }
        }
      }
    }
  }

  /// <summary>
  ///   Replaces every XML comment with blanks, keeping the newlines so line numbers still hold. Commented-out
  ///   config is inert — a key referenced only inside a comment is not a missing key, and reporting one is a
  ///   false positive (measured: PootPavillion's items.xml carries a disabled item block that named a key the
  ///   mod does not define).
  /// </summary>
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

/// <summary>
///   Enough CSV to read a localization file the way the game's ByteReader.ReadCSV does: comma-separated,
///   double quotes around a field that contains a comma or a newline, "" for a literal quote.
/// </summary>
internal sealed class Csv {
  public List<string> Header { get; } = new();

  /// <summary>Key, 1-based line of the row's first character, and how many fields the row had.</summary>
  public List<(string Key, int Line, int Fields)> Rows { get; } = new();

  public IEnumerable<string> Keys => Rows.Select(r => r.Key).Where(k => !string.IsNullOrWhiteSpace(k));

  public static Csv Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

  public static Csv Parse(string text) {
    if (text.Length > 0 && text[0] == '\uFEFF') {
      text = text.Substring(1);
    }

    var csv = new Csv();
    var line = 1;
    var position = 0;
    var first = true;
    while (position < text.Length) {
      var startLine = line;
      List<string> fields = ReadRow(text, ref position, ref line);
      if (first) {
        csv.Header.AddRange(fields);
        first = false;
      } else if (fields.Count > 1 || fields[0].Length > 0) {
        csv.Rows.Add((fields[0], startLine, fields.Count));
      }
    }

    return csv;
  }

  private static List<string> ReadRow(string text, ref int position, ref int line) {
    var fields = new List<string>();
    var field = new StringBuilder();
    var quoted = false;
    while (position < text.Length) {
      var c = text[position++];
      if (quoted) {
        if (c == '"') {
          if (position < text.Length && text[position] == '"') {
            field.Append('"');
            position++;
          } else {
            quoted = false;
          }
        } else {
          if (c == '\n') {
            line++;
          }

          field.Append(c);
        }

        continue;
      }

      switch (c) {
        case '"' when field.Length == 0:
          quoted = true;
          break;
        case ',':
          fields.Add(field.ToString());
          field.Clear();
          break;
        case '\r':
          break;
        case '\n':
          line++;
          fields.Add(field.ToString());
          return fields;
        default:
          field.Append(c);
          break;
      }
    }

    fields.Add(field.ToString());
    return fields;
  }
}

internal static class SelfTest {
  public static int Run() {
    var failures = new List<string>();

    void Check(string what, bool ok) {
      Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
      if (!ok) {
        failures.Add(what);
      }
    }

    Csv simple = Csv.Parse("Key,english\r\na,Alpha\r\nb,Beta\r\n");
    Check("header is read", simple.Header.SequenceEqual(new[] { "Key", "english" }));
    Check("rows are read", simple.Keys.SequenceEqual(new[] { "a", "b" }));

    Csv quoted = Csv.Parse("Key,english\na,\"has, comma\"\nb,\"has \"\"quote\"\"\"\n");
    Check("a quoted comma does not split a field", quoted.Rows[0].Fields == 2);
    Check("the row after a quoted field still parses", quoted.Keys.SequenceEqual(new[] { "a", "b" }));

    Csv multiline = Csv.Parse("Key,english\na,\"two\nlines\"\nb,Beta\n");
    Check("a newline inside quotes stays in the field", multiline.Keys.SequenceEqual(new[] { "a", "b" }));
    Check("a newline inside quotes still advances the line number", multiline.Rows[1].Line == 4);

    Check("a byte order mark is stripped", Csv.Parse("\uFEFFKey,english\na,A\n").Header[0] == "Key");
    Check("a blank line is not a row", Csv.Parse("Key,english\n\na,A\n").Rows.Count == 1);

    var vanillaColumns = new HashSet<string>(new[] { "Key", "File", "Type", "english" },
      StringComparer.OrdinalIgnoreCase);

    Check("a one-column header is an error",
      Rules("Key\na\n", vanillaColumns).Contains("header-too-short"));
    Check("a header column with a space is an error",
      Rules("Key,Used In,english\na,x,A\n", vanillaColumns).Contains("illegal-column"));
    Check("a context-prefixed column is legal despite its spaces",
      !Rules("Key,Context / Alternate Text,english\na,x,A\n", vanillaColumns).Contains("illegal-column"));
    Check("a non-vanilla column is a warning",
      Rules("Key,Source,english\na,x,A\n", vanillaColumns).Contains("unknown-column"));
    Check("column matching is case-insensitive",
      Rules("Key,File,Type,English\na,x,y,A\n", vanillaColumns).Count == 0);
    Check("no language column is a warning",
      Rules("Key,File\na,x\n", vanillaColumns).Contains("no-language-column"));
    Check("a duplicate key is an error",
      Rules("Key,english\na,A\na,B\n", vanillaColumns).Contains("duplicate-key"));
    Check("an empty key is an error",
      Rules("Key,english\n,A\n", vanillaColumns).Contains("empty-key"));
    Check("a row with surplus fields is a warning",
      Rules("Key,english\na,A,extra\n", vanillaColumns).Contains("extra-fields"));

    Check("a display_name_key reference is found",
      Values(LocLint.KeyReference, "<x display_name_key=\"abc\" />").Contains("abc"));
    Check("a name_key reference is found",
      Values(LocLint.KeyReference, "<buff name_key=\"b_name\" />").Contains("b_name"));
    Check("an unrelated attribute is not a reference",
      Values(LocLint.KeyReference, "<x keyboard=\"abc\" />").Count == 0);
    Check("a DescriptionKey property pair is found",
      Values(LocLint.PropertyKeyReference,
        "<property name=\"DescriptionKey\" value=\"missing_key\" />").Contains("missing_key"));
    Check("a display_name_key property pair is found",
      Values(LocLint.PropertyKeyReference,
        "<property name=\"display_name_key\" value=\"poot_Name\" />").Contains("poot_Name"));
    Check("a key inside an XML comment is invisible",
      Values(LocLint.PropertyKeyReference,
        LocLint.BlankComments("<!-- <property name=\"DescriptionKey\" value=\"dead\" /> -->")).Count == 0);
    Check("blanking a comment keeps the line numbers",
      LocLint.BlankComments("a\n<!--\nx\n-->\nb").Count(c => c == '\n') == 4);
    Check("a key after a comment is still found",
      Values(LocLint.PropertyKeyReference,
        LocLint.BlankComments("<!-- x --><property name=\"DescriptionKey\" value=\"live\" />"))
        .Contains("live"));
    Check("an unrelated property pair is not a reference",
      Values(LocLint.PropertyKeyReference,
        "<property name=\"Extends\" value=\"terrDirt\" />").Count == 0);

    Console.WriteLine();
    Console.WriteLine(failures.Count == 0 ? "selftest: all cases pass" : $"selftest: {failures.Count} FAILED");
    return failures.Count == 0 ? 0 : 1;
  }

  private static List<string> Rules(string csv, HashSet<string> vanillaColumns) {
    var findings = new List<Finding>();
    LocLint.LintCsv("mod", "file.csv", Csv.Parse(csv), vanillaColumns, findings);
    return findings.Select(f => f.Rule).ToList();
  }

  private static List<string> Values(Regex pattern, string text) =>
    pattern.Matches(text).Select(m => m.Groups["val"].Value).ToList();
}

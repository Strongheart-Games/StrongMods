#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Lint the Claude Code and Codex JSON config for the two silent failure modes that actually shipped (2026-08-06):
//
//   * Not-strict-JSON: Claude Code parses its settings files strictly and silently ignores a file that fails —
//     every permission rule, env var, hook, and setting in it goes inert. The incident: one trailing comma in
//     .claude\settings.local.json disabled that whole file (dead GH_CONFIG_DIR, so gh fell back to the human's
//     identity; dead permission allowlists; autoMemoryDirectory ignored) with no error surfaced anywhere.
//   * Byte order mark: Claude Code happens to tolerate one, but strict RFC 8259 parsers (python json,
//     JSON.parse) reject the file, so downstream tooling sees a different config than the harness does.
//
// Three consumers:
//   * The SessionStart hooks in .claude\settings.json and .codex\hooks.json run this for their own harness at
//     the harm point. Contract: findings go to STDOUT and the exit code is 0 even then — both harnesses add
//     SessionStart stdout to the session context only on exit 0, and surfacing beats signalling here. Clean
//     runs print nothing. Harness-specific runs stay isolated: broken Claude config never affects Codex startup,
//     and vice versa.
//   * Tests\SettingsLintTests.cs implements the same FILE checks (syntax + byte order mark) as the enforcing
//     gate (CI + dotnet test). That shared core must stay identical in both; change one, change both.
//
// Honored checks (hook-only by construction — a test process outside a session has no session environment to
// assert against): parsing clean is necessary, not sufficient. A file can be valid on disk while the running
// session still carries an older broken load, and project-scope settings apply only after workspace trust. So,
// data-driven from what the settings files themselves declare: inside a session (gated on
// CLAUDE_PROJECT_DIR/CLAUDECODE), every declared env var must be present with the declared value;
// fully-qualified *_DIR/*_FILE values must exist on this machine regardless of session; and a declared
// autoMemoryDirectory must be absolute or ~/-prefixed — Claude Code silently ignores other shapes.
//
//     dotnet run build/tools/settings_lint.cs                  # lint both harnesses, from the repo root
//     dotnet run build/tools/settings_lint.cs -- --harness claude
//     dotnet run build/tools/settings_lint.cs -- --harness codex
//     dotnet run build/tools/settings_lint.cs -- --selftest
//
// Exit codes: 0 = ran (with findings or without — the hook contract above), 1 = selftest failure,
// 2 = usage or environment error.

using System.Text;
using System.Text.Json;

switch (args) {
  case []: return SettingsLint.Run(AgentHarness.All);
  case ["--harness", "claude"]: return SettingsLint.Run(AgentHarness.Claude);
  case ["--harness", "codex"]: return SettingsLint.Run(AgentHarness.Codex);
  case ["--selftest"]: return SettingsLint.Selftest();
  default:
    Console.Error.WriteLine("usage: settings_lint.cs [--harness claude|codex | --selftest]");
    return 2;
}

internal enum AgentHarness { All, Claude, Codex }

internal static class SettingsLint {
  // Every JSON file each agent harness reads from this repo. settings.local.json is machine-local and
  // gitignored; a candidate that does not exist is simply not this machine's concern. .mcp.json belongs to
  // the Claude group because that is the harness whose startup currently owns and validates it here.
  private static readonly string[] ClaudeCandidates = {
    ".claude/settings.json", ".claude/settings.local.json", ".claude/launch.json", ".mcp.json"
  };
  private static readonly string[] CodexCandidates = { ".codex/hooks.json" };

  public static int Run(AgentHarness harness) {
    bool claudeMissing = (harness is AgentHarness.All or AgentHarness.Claude) && !Directory.Exists(".claude");
    bool codexMissing = (harness is AgentHarness.All or AgentHarness.Codex) && !Directory.Exists(".codex");
    if (claudeMissing || codexMissing) {
      Console.Error.WriteLine(
        $"error: expected agent config directories under {Directory.GetCurrentDirectory()} — run from the repo root");
      return 2;
    }

    IEnumerable<string> candidates = harness switch {
      AgentHarness.Claude => ClaudeCandidates,
      AgentHarness.Codex => CodexCandidates,
      _ => ClaudeCandidates.Concat(CodexCandidates)
    };
    List<string> findings = candidates.Where(File.Exists)
      .SelectMany(path => Check(path, File.ReadAllBytes(path))).ToList();
    if (harness is AgentHarness.All or AgentHarness.Claude) {
      findings.AddRange(HonoredFindings(Environment.GetEnvironmentVariable));
    }
    if (findings.Count > 0) {
      Console.WriteLine($"settings_lint: {findings.Count} problem(s) in agent config files:");
      findings.ForEach(finding => Console.WriteLine($"  {finding}"));
    }
    return 0;
  }

  // Defaults spelled out because they ARE the point: Claude Code's own settings parser rejects trailing
  // commas and comments, so linting any looser would pass files the harness silently drops.
  private static readonly JsonDocumentOptions Strict =
    new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };

  /// <summary>The file checks, pure over bytes so the selftest needs no files.</summary>
  public static List<string> Check(string path, byte[] bytes) {
    var findings = new List<string>();
    if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) {
      findings.Add($"{path}: UTF-16 byte order mark — the file is not UTF-8 at all; re-save it as UTF-8 " +
        "without byte order mark.");
      return findings;
    }
    if (bytes is [0xEF, 0xBB, 0xBF, ..]) {
      findings.Add($"{path}: starts with a UTF-8 byte order mark (U+FEFF), which strict JSON parsers " +
        "(RFC 8259) reject — save as UTF-8 without byte order mark.");
      bytes = bytes[3..];
    }

    try {
      JsonDocument.Parse(Encoding.UTF8.GetString(bytes), Strict).Dispose();
    } catch (JsonException e) {
      string consequence = path.Replace('\\', '/').StartsWith(".claude/")
        ? "Claude Code silently ignores the WHOLE file when it cannot parse — every permission rule, env var, " +
          "hook, and setting in it is inert until this is fixed."
        : "Codex cannot load this hook file until its JSON is fixed.";
      findings.Add($"{path} line {(e.LineNumber ?? 0) + 1}: {Reason(e)} {consequence}");
    }
    return findings;
  }

  /// <summary>Builds the honored-check inputs from the settings files on disk (the only two candidates whose
  ///   declarations the harness applies to the session) and delegates to the pure core. Local is read second
  ///   so its declarations win, matching Claude Code's own scope precedence.</summary>
  private static List<string> HonoredFindings(Func<string, string?> getenv) {
    var inSession = !string.IsNullOrEmpty(getenv("CLAUDE_PROJECT_DIR")) || !string.IsNullOrEmpty(getenv("CLAUDECODE"));
    var declaredEnv = new Dictionary<string, (string Value, string File)>();
    (string Value, string File)? autoMemory = null;

    foreach (var path in new[] { ".claude/settings.json", ".claude/settings.local.json" }) {
      if (!File.Exists(path)) {
        continue;
      }
      byte[] bytes = File.ReadAllBytes(path);
      if (bytes is [0xEF, 0xBB, 0xBF, ..]) {
        bytes = bytes[3..];
      }
      JsonDocument document;
      try {
        document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes), Strict);
      } catch (JsonException) {
        continue; // unparseable: the file checks already reported it, and there is nothing declared to honor
      }
      using (document) {
        if (document.RootElement.TryGetProperty("env", out JsonElement env) && env.ValueKind == JsonValueKind.Object) {
          foreach (JsonProperty property in env.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String)) {
            declaredEnv[property.Name] = (property.Value.GetString()!, path);
          }
        }
        if (document.RootElement.TryGetProperty("autoMemoryDirectory", out JsonElement memory)
            && memory.ValueKind == JsonValueKind.String) {
          autoMemory = (memory.GetString()!, path);
        }
      }
    }
    return HonoredCore(declaredEnv, autoMemory, inSession, getenv);
  }

  /// <summary>The honored checks, pure over declarations and an environment lookup so the selftest can
  ///   inject both. Static plausibility (paths, shapes) runs everywhere; presence/value checks only inside a
  ///   session, where the harness has actually applied the env block.</summary>
  public static List<string> HonoredCore(
      IReadOnlyDictionary<string, (string Value, string File)> declaredEnv,
      (string Value, string File)? autoMemoryDirectory, bool inSession, Func<string, string?> getenv) {
    var findings = new List<string>();

    if (autoMemoryDirectory is { } memory && !memory.Value.StartsWith("~/") && !memory.Value.StartsWith(@"~\")
        && !Path.IsPathFullyQualified(memory.Value)) {
      findings.Add($"{memory.File}: autoMemoryDirectory '{memory.Value}' is neither an absolute path nor " +
        "~/-prefixed — Claude Code silently ignores such values and falls back to the default memory directory.");
    }

    foreach ((var name, (var value, var file)) in declaredEnv.OrderBy(d => d.Key, StringComparer.Ordinal)) {
      // Existence is only checkable for fully-qualified values (~-prefixed ones expand harness-side).
      if (Path.IsPathFullyQualified(value)
          && (name.EndsWith("_DIR") && !Directory.Exists(value) || name.EndsWith("_FILE") && !File.Exists(value))) {
        findings.Add($"{file}: env.{name} points at '{value}', which does not exist on this machine.");
      }
      if (!inSession) {
        continue;
      }
      var actual = getenv(name);
      if (string.IsNullOrEmpty(actual)) {
        findings.Add($"{file}: env.{name} is declared but absent from this session's environment — the env " +
          "block was not applied. If the file changed after this session started, the session still runs the " +
          "old load; restart to pick it up.");
      } else if (actual != value) {
        findings.Add($"{file}: env.{name} is '{actual}' in this session, not the declared '{value}' — a " +
          "higher-precedence scope overrides it, or the session predates the current file.");
      }
    }
    return findings;
  }

  /// <summary>The exception's sentence, without the "Path: $ | LineNumber: ..." locator tail (the line is
  ///   re-reported 1-based by the caller, matching what editors display).</summary>
  private static string Reason(JsonException e) {
    var message = e.Message.Split(" Path: ")[0].Split(" LineNumber: ")[0]
      .Replace("Change the reader options.", "").Trim();
    return message.EndsWith('.') ? message : message + ".";
  }

  public static int Selftest() {
    var failures = new List<string>();
    void ExpectFindings(string name, List<string> findings, params string[] substrings) {
      if (findings.Count != substrings.Length) {
        failures.Add($"{name}: expected {substrings.Length} finding(s), got {findings.Count}: " +
          $"[{string.Join(" | ", findings)}]");
        return;
      }
      foreach ((var finding, var want) in findings.Zip(substrings)) {
        if (!finding.Contains(want)) {
          failures.Add($"{name}: finding '{finding}' does not mention '{want}'");
        }
      }
    }
    void Expect(string name, byte[] bytes, params string[] substrings) =>
      ExpectFindings(name, Check("probe.json", bytes), substrings);

    byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    byte[] Bom(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8(text)).ToArray();

    Expect("clean", Utf8("{ \"a\": [1, 2] }"));
    Expect("trailing comma", Utf8("{ \"a\": [1,\n2,\n] }"), "line 3");
    Expect("comment", Utf8("{ // hi\n \"a\": 1 }"), "line 1");
    Expect("byte order mark only", Bom("{ \"a\": 1 }"), "byte order mark");
    Expect("byte order mark + trailing comma", Bom("{ \"a\": [1,] }"), "byte order mark", "line 1");
    Expect("utf-16", new byte[] { 0xFF, 0xFE, 0x7B, 0x00 }, "UTF-16");

    // Honored-core cases: declarations and environment both injected; no files, no real session.
    var existingDir = Path.GetTempPath();
    var absentDir = Path.Combine(Path.GetTempPath(), "settings-lint-selftest-absent", "nested");
    (string Value, string File) From(string value) => (value, "probe-settings.json");
    Dictionary<string, (string Value, string File)> Declared(string name, string value) => new() { [name] = From(value) };
    string? SessionEnv(string name) => name switch {
      "GOOD" => "v", "PROBE_DIR" => existingDir, "ABSENT_DIR" => absentDir, _ => null
    };

    ExpectFindings("honored", HonoredCore(
      new Dictionary<string, (string Value, string File)> { ["GOOD"] = From("v"), ["PROBE_DIR"] = From(existingDir) },
      From(existingDir), true, SessionEnv));
    ExpectFindings("env not applied", HonoredCore(Declared("MISSING", "x"), null, true, SessionEnv),
      "absent from this session");
    ExpectFindings("env overridden", HonoredCore(Declared("GOOD", "other"), null, true, SessionEnv),
      "not the declared");
    ExpectFindings("dir does not exist", HonoredCore(Declared("ABSENT_DIR", absentDir), null, true, SessionEnv),
      "does not exist");
    ExpectFindings("memory dir not absolute", HonoredCore(
      new Dictionary<string, (string, string)>(), From("relative/memory"), false, SessionEnv), "neither an absolute");
    ExpectFindings("outside a session", HonoredCore(Declared("MISSING", "x"), null, false, SessionEnv));

    failures.ForEach(failure => Console.Error.WriteLine($"FAIL {failure}"));
    Console.WriteLine(failures.Count == 0 ? "selftest: 12 checks passed" : $"selftest: {failures.Count} FAILED");
    return failures.Count == 0 ? 0 : 1;
  }
}

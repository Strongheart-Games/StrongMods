#!/usr/bin/env dotnet
#:property Nullable=enable
#:property PublishAot=false
// Lint the agent-harness JSON config for the two silent failure modes that actually shipped (2026-08-06):
//
//   * Not-strict-JSON: Claude Code parses its settings files strictly and silently ignores a file that fails —
//     every permission rule, env var, hook, and setting in it goes inert. The incident: one trailing comma in
//     .claude\settings.local.json disabled that whole file (dead GH_CONFIG_DIR, so gh fell back to the human's
//     identity; dead permission allowlists; autoMemoryDirectory ignored) with no error surfaced anywhere.
//   * Byte order mark: Claude Code happens to tolerate one, but strict RFC 8259 parsers (python json,
//     JSON.parse) reject the file, so downstream tooling sees a different config than the harness does.
//
// Two consumers, same semantics:
//   * The SessionStart hook in .claude\settings.json runs this at the harm point. Contract: findings go to
//     STDOUT and the exit code is 0 even then — Claude Code adds SessionStart stdout to the session context
//     only on exit 0, and surfacing beats signalling here. Clean runs print nothing.
//   * Tests\SettingsLintTests.cs implements the same checks as the enforcing gate (CI + dotnet test).
//     Change the semantics in one place and the other must follow.
//
//     dotnet run build/tools/settings_lint.cs                  # lint, from the repo root
//     dotnet run build/tools/settings_lint.cs -- --selftest
//
// Exit codes: 0 = ran (with findings or without — the hook contract above), 1 = selftest failure,
// 2 = usage or environment error.

using System.Text;
using System.Text.Json;

switch (args) {
  case []: return SettingsLint.Run();
  case ["--selftest"]: return SettingsLint.Selftest();
  default:
    Console.Error.WriteLine("usage: settings_lint.cs [--selftest]");
    return 2;
}

internal static class SettingsLint {
  // Every JSON file the agent harness reads from this repo. settings.local.json is machine-local and
  // gitignored; a candidate that does not exist is simply not this machine's concern.
  private static readonly string[] Candidates = {
    ".claude/settings.json", ".claude/settings.local.json", ".claude/launch.json", ".mcp.json"
  };

  public static int Run() {
    if (!Directory.Exists(".claude")) {
      Console.Error.WriteLine(
        $"error: no .claude directory under {Directory.GetCurrentDirectory()} — run from the repo root");
      return 2;
    }

    List<string> findings = Candidates.Where(File.Exists)
      .SelectMany(path => Check(path, File.ReadAllBytes(path))).ToList();
    if (findings.Count > 0) {
      Console.WriteLine($"settings_lint: {findings.Count} problem(s) in agent config files:");
      findings.ForEach(finding => Console.WriteLine($"  {finding}"));
    }
    return 0;
  }

  /// <summary>The checks, pure over bytes so the selftest needs no files.</summary>
  public static List<string> Check(string path, byte[] bytes) {
    var findings = new List<string>();
    if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) {
      findings.Add($"{path}: UTF-16 byte order mark — the file is not UTF-8 at all; re-save it as UTF-8 " +
        "without byte order mark.");
      return findings;
    }
    if (bytes is [0xEF, 0xBB, 0xBF, ..]) {
      findings.Add($"{path}: starts with a UTF-8 byte order mark (U+FEFF). Claude Code tolerates it, but " +
        "strict JSON parsers (RFC 8259) reject the file — save as UTF-8 without byte order mark.");
      bytes = bytes[3..];
    }

    // Defaults spelled out because they ARE the point: Claude Code's own settings parser rejects trailing
    // commas and comments, so linting any looser would pass files the harness silently drops.
    var strict = new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
    try {
      JsonDocument.Parse(Encoding.UTF8.GetString(bytes), strict).Dispose();
    } catch (JsonException e) {
      findings.Add($"{path} line {(e.LineNumber ?? 0) + 1}: {Reason(e)} Claude Code silently ignores the " +
        "WHOLE file when it cannot parse — every permission rule, env var, hook, and setting in it is inert " +
        "until this is fixed.");
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
    void Expect(string name, byte[] bytes, params string[] substrings) {
      List<string> findings = Check("probe.json", bytes);
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

    byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    byte[] Bom(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8(text)).ToArray();

    Expect("clean", Utf8("{ \"a\": [1, 2] }"));
    Expect("trailing comma", Utf8("{ \"a\": [1,\n2,\n] }"), "line 3");
    Expect("comment", Utf8("{ // hi\n \"a\": 1 }"), "line 1");
    Expect("byte order mark only", Bom("{ \"a\": 1 }"), "byte order mark");
    Expect("byte order mark + trailing comma", Bom("{ \"a\": [1,] }"), "byte order mark", "line 1");
    Expect("utf-16", new byte[] { 0xFF, 0xFE, 0x7B, 0x00 }, "UTF-16");

    failures.ForEach(failure => Console.Error.WriteLine($"FAIL {failure}"));
    Console.WriteLine(failures.Count == 0 ? "selftest: 6 checks passed" : $"selftest: {failures.Count} FAILED");
    return failures.Count == 0 ? 0 : 1;
  }
}

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tests;

/// <summary>
///   The agent-harness JSON config must be strict JSON with no byte order mark. Claude Code silently ignores a
///   settings file that fails its strict parse — the 2026-08-06 incident: one trailing comma in
///   .claude\settings.local.json disabled that whole file (dead GH_CONFIG_DIR meant gh fell back to the
///   human's identity, the permission allowlists were off, autoMemoryDirectory was ignored) with no error
///   surfaced anywhere. build\tools\settings_lint.cs is this check's runtime twin — a SessionStart hook runs
///   it at the harm point; this test is the enforcing gate (CI sees the tracked files, a local run also sees
///   the machine-local ones). Same semantics by construction: strict System.Text.Json defaults, because that
///   models the parser Claude Code itself uses. Change one, change both.
/// </summary>
public class SettingsLintTests {
  private static readonly string[] Candidates = {
    ".claude/settings.json", ".claude/settings.local.json", ".claude/launch.json", ".mcp.json"
  };

  [Fact]
  public void Agent_config_JSON_is_strict_and_byte_order_mark_free() {
    var repoRoot = Path.GetFullPath(AssemblyMetadata.Get("RepoRoot"));
    var offenders = new List<string>();

    foreach (var relative in Candidates) {
      var path = Path.Combine(repoRoot, relative);
      if (!File.Exists(path)) {
        continue; // settings.local.json is machine-local and gitignored; each machine lints what it has
      }

      byte[] bytes = File.ReadAllBytes(path);
      if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) {
        offenders.Add($"{relative}: UTF-16 byte order mark — the file is not UTF-8 at all; re-save it as " +
          "UTF-8 without byte order mark.");
        continue;
      }
      if (bytes is [0xEF, 0xBB, 0xBF, ..]) {
        offenders.Add($"{relative}: starts with a UTF-8 byte order mark (U+FEFF). Claude Code tolerates it, " +
          "but strict JSON parsers (RFC 8259) reject the file — save as UTF-8 without byte order mark.");
        bytes = bytes[3..];
      }

      try {
        // Defaults spelled out because they ARE the contract under test: Claude Code's own settings parser
        // rejects trailing commas and comments, so testing any looser would pass files the harness drops.
        JsonDocument.Parse(Encoding.UTF8.GetString(bytes),
            new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow })
          .Dispose();
      } catch (JsonException e) {
        var reason = e.Message.Split(" Path: ")[0].Split(" LineNumber: ")[0]
          .Replace("Change the reader options.", "").Trim();
        offenders.Add($"{relative} line {(e.LineNumber ?? 0) + 1}: {reason} Claude Code silently ignores the " +
          "WHOLE file when it cannot parse — every permission rule, env var, hook, and setting in it is " +
          "inert until this is fixed.");
      }
    }

    Assert.True(offenders.Count == 0, string.Join("\n\n", offenders));
  }
}

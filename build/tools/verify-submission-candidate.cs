#!/usr/bin/env dotnet
#:property Nullable=enable
// Verify the exact files a human may submit, not the surrounding dirty working tree.
//
// After changing this file, run `dotnet clean build/tools/verify-submission-candidate.cs`, then
// `dotnet build build/tools/verify-submission-candidate.cs`. Usage:
//   dotnet run --file build/tools/verify-submission-candidate.cs --no-build -- --base HEAD --report <report.html> \
//     --file <repo-relative-file> [--file <repo-relative-file> ...] -- <command> [arguments...] [--then <command> ...]
//
// The tool creates an ignored .scratch worktree at the named base, overlays only the supplied files from the current
// tree, runs the command there, and writes a generated report section only when that exact candidate passes. `--base`
// and `--file` are deliberately explicit: inferring either from a dirty shared tree would recreate #144's failure.

using System.Diagnostics;
using System.Net;
using System.Text;

if (args.SequenceEqual(new[] { "--selftest" })) {
  return SubmissionCandidateVerifier.SelfTest();
}

try {
  return SubmissionCandidateVerifier.Run(args);
} catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) {
  Console.Error.WriteLine($"error: {e.Message}");
  return 2;
}

internal sealed record CandidateOptions(string Base, string Report, List<string> Files, List<List<string>> Commands);

internal static class SubmissionCandidateVerifier {
  private const string SectionStart = "<!-- submission-candidate:verified:start -->";
  private const string SectionEnd = "<!-- submission-candidate:verified:end -->";

  public static int Run(string[] args) {
    CandidateOptions options = Parse(args);
    string root = Path.GetFullPath(Git(Environment.CurrentDirectory, "rev-parse", "--show-toplevel").Trim());
    string baseSha = Git(root, "rev-parse", "--verify", options.Base + "^{commit}").Trim();
    string report = Normalize(root, options.Report);
    List<string> files = NormalizeFiles(root, options.Files);
    if (!files.Contains(report, StringComparer.OrdinalIgnoreCase)) {
      throw new ArgumentException("--report must also be one of the explicit --file values.");
    }

    string reportSource = Path.Combine(root, report);
    if (!File.Exists(reportSource)) {
      throw new ArgumentException($"report does not exist in the current tree: {report}");
    }

    string section = RenderSection(baseSha, files, options.Commands);
    string renderedReport = ReplaceSection(File.ReadAllText(reportSource), section);
    string scratchRoot = Path.Combine(root, ".scratch");
    string worktree = Path.Combine(scratchRoot, "submission-candidate-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratchRoot);
    var added = false;
    try {
      Git(root, "worktree", "add", "--detach", worktree, baseSha);
      added = true;
      foreach (string file in files) {
        Overlay(root, worktree, file);
      }

      File.WriteAllText(Path.Combine(worktree, report), renderedReport, new UTF8Encoding(false));
      foreach (List<string> command in options.Commands) {
        int exitCode = RunCommand(worktree, command);
        if (exitCode != 0) {
          Console.Error.WriteLine($"candidate failed validation with exit code {exitCode}; report was not updated.");
          return exitCode;
        }
      }

      File.WriteAllText(reportSource, renderedReport, new UTF8Encoding(false));
      Console.WriteLine($"verified submission candidate at {baseSha}: {string.Join(", ", files)}");
      return 0;
    } finally {
      if (added) {
        TryRemoveWorktree(root, worktree);
      }
    }
  }

  public static int SelfTest() {
    const string input = "<html>\n<!-- submission-candidate:verified:start -->\nold\n"
      + "<!-- submission-candidate:verified:end -->\n</html>";
    string result = ReplaceSection(input, "<section><p>new &amp; checked</p></section>");
    if (!result.Contains("new &amp; checked", StringComparison.Ordinal) || result.Contains("old", StringComparison.Ordinal)) {
      Console.Error.WriteLine("self-test failed: report section replacement was not exact.");
      return 1;
    }

    try {
      ReplaceSection("<html />", "<section />");
      Console.Error.WriteLine("self-test failed: missing markers were accepted.");
      return 1;
    } catch (ArgumentException) {
    }

    List<string> normalized = NormalizeForwardedArguments(new List<string> {
      "dotnet", "build", "-p:", "SdtdPackagesDir=packages"
    });
    if (!normalized.SequenceEqual(new[] { "dotnet", "build", "-p:SdtdPackagesDir=packages" })) {
      Console.Error.WriteLine("self-test failed: split MSBuild property switch was not normalized.");
      return 1;
    }

    normalized = NormalizeForwardedArguments(new List<string> {
      "dotnet", "build", "-p: SdtdPackagesDir=packages"
    });
    if (!normalized.SequenceEqual(new[] { "dotnet", "build", "-p:SdtdPackagesDir=packages" })) {
      Console.Error.WriteLine("self-test failed: spaced MSBuild property switch was not normalized.");
      return 1;
    }

    normalized = NormalizeForwardedArguments(new List<string> {
      "dotnet", "build", "-p:", " SdtdPackagesDir=packages"
    });
    if (!normalized.SequenceEqual(new[] { "dotnet", "build", "-p:SdtdPackagesDir=packages" })) {
      Console.Error.WriteLine("self-test failed: spaced MSBuild property value was not normalized.");
      return 1;
    }

    if (FormatCommand(new List<string> { "dotnet", "build", "-p:", "SdtdPackagesDir=packages" })
        != "dotnet build -p:SdtdPackagesDir=packages") {
      Console.Error.WriteLine("self-test failed: MSBuild property switch was not formatted canonically.");
      return 1;
    }

    Console.WriteLine("self-test passed");
    return 0;
  }

  private static CandidateOptions Parse(string[] args) {
    string? baseRevision = null;
    string? report = null;
    var files = new List<string>();
    var command = new List<string>();
    for (var index = 0; index < args.Length; index++) {
      string arg = args[index];
      if (arg == "--") {
        command.AddRange(args.Skip(index + 1));
        break;
      }

      if (arg is "--base" or "--report" or "--file") {
        if (++index == args.Length) {
          throw new ArgumentException($"{arg} requires a value.");
        }

        if (arg == "--base") {
          baseRevision = args[index];
        } else if (arg == "--report") {
          report = args[index];
        } else {
          files.Add(args[index]);
        }
      } else {
        throw new ArgumentException($"unknown option: {arg}");
      }
    }

    List<List<string>> commands = SplitCommands(command);
    if (baseRevision is null || report is null || files.Count == 0 || commands.Count == 0) {
      throw new ArgumentException("usage: --base <revision> --report <report.html> --file <path> ... -- <command> ...");
    }

    return new CandidateOptions(baseRevision, report, files, commands);
  }

  private static List<List<string>> SplitCommands(List<string> source) {
    var result = new List<List<string>> { new() };
    foreach (string arg in source) {
      if (arg == "--then") {
        if (result[^1].Count == 0) {
          throw new ArgumentException("--then must follow a command.");
        }

        result.Add(new List<string>());
      } else {
        result[^1].Add(arg);
      }
    }

    if (result[^1].Count == 0) {
      throw new ArgumentException("--then must be followed by a command.");
    }

    return result.Select(NormalizeForwardedArguments).ToList();
  }

  private static List<string> NormalizeForwardedArguments(List<string> source) {
    var result = new List<string>();
    for (int index = 0; index < source.Count; index++) {
      string arg = NormalizeMsBuildPropertySwitch(source[index]);
      if (IsMsBuildPropertySwitch(arg) && index + 1 < source.Count && source[index + 1].Contains('=')) {
        result.Add(arg + source[++index].TrimStart());
      } else {
        result.Add(arg);
      }
    }

    return result;
  }

  private static string NormalizeMsBuildPropertySwitch(string arg) {
    foreach (string propertySwitch in new[] { "-p:", "/p:", "--property:" }) {
      if (arg.StartsWith(propertySwitch, StringComparison.Ordinal) && arg.Length > propertySwitch.Length
          && char.IsWhiteSpace(arg[propertySwitch.Length])) {
        return propertySwitch + arg[propertySwitch.Length..].TrimStart();
      }
    }

    return arg;
  }

  private static bool IsMsBuildPropertySwitch(string arg) => arg is "-p:" or "/p:" or "--property:";

  private static List<string> NormalizeFiles(string root, List<string> source) {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    return source.Select(path => Normalize(root, path)).Select(path => {
      if (!seen.Add(path)) {
        throw new ArgumentException($"candidate file is listed more than once: {path}");
      }

      return path;
    }).ToList();
  }

  private static string Normalize(string root, string submitted) {
    string full = Path.GetFullPath(Path.Combine(root, submitted));
    string rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) {
      throw new ArgumentException($"candidate file is outside the repository: {submitted}");
    }

    return Path.GetRelativePath(root, full).Replace('\\', '/');
  }

  private static void Overlay(string root, string worktree, string file) {
    string source = Path.Combine(root, file);
    string destination = Path.Combine(worktree, file);
    if (Directory.Exists(source)) {
      throw new ArgumentException($"candidate entries must be files, not directories: {file}");
    }

    if (File.Exists(source)) {
      Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
      File.Copy(source, destination, true);
    } else if (File.Exists(destination)) {
      File.Delete(destination);
    } else {
      throw new ArgumentException($"candidate file is absent from both base and current tree: {file}");
    }
  }

  private static string RenderSection(string baseSha, List<string> files, List<List<string>> commands) {
    string items = string.Join("\n", files.Select(file => $"      <li><code>{Html(file)}</code></li>"));
    string validations = string.Join("\n", commands.Select(command =>
      $"        <li><code>{Html(FormatCommand(command))}</code> (exit 0)</li>"));
    return $"""
      <section>
        <h2>Submission candidate (verified)</h2>
        <p>This generated section is the authoritative submission set. It was overlaid onto a scratch worktree at
          <code>{Html(baseSha)}</code> and passed every command below there.</p>
        <p><strong>Validation:</strong></p>
        <ul>
      {validations}
        </ul>
        <ul>
      {items}
        </ul>
      </section>
      """;
  }

  private static string ReplaceSection(string report, string section) {
    int start = report.IndexOf(SectionStart, StringComparison.Ordinal);
    int end = report.IndexOf(SectionEnd, StringComparison.Ordinal);
    if (start < 0 || end < start) {
      throw new ArgumentException($"report must contain {SectionStart} and {SectionEnd} markers.");
    }

    int afterEnd = end + SectionEnd.Length;
    return report[..start] + SectionStart + "\n" + section + SectionEnd + report[afterEnd..];
  }

  private static int RunCommand(string directory, List<string> command) {
    List<string> normalized = NormalizeForwardedArguments(command);
    ProcessResult result = Execute(directory, normalized[0], normalized.Skip(1));
    Console.Write(result.Output);
    return result.ExitCode;
  }

  private static string FormatCommand(List<string> command) => string.Join(' ', command)
    .Replace("-p: ", "-p:", StringComparison.Ordinal)
    .Replace("/p: ", "/p:", StringComparison.Ordinal)
    .Replace("--property: ", "--property:", StringComparison.Ordinal);

  private static string Git(string directory, params string[] args) {
    ProcessResult result = Execute(directory, "git", args);
    if (result.ExitCode != 0) {
      throw new InvalidOperationException($"git {string.Join(' ', args)} failed:\n{result.Output}");
    }

    return result.StandardOutput;
  }

  private static void TryRemoveWorktree(string root, string worktree) {
    ProcessResult result = Execute(root, "git", new[] { "worktree", "remove", "--force", worktree });
    if (result.ExitCode != 0) {
      Console.Error.WriteLine($"warning: could not remove scratch worktree {worktree}:\n{result.Output}");
    }
  }

  private static ProcessResult Execute(string directory, string executable, IEnumerable<string> args) {
    var start = new ProcessStartInfo(executable) {
      WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (string arg in args) {
      start.ArgumentList.Add(arg);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {executable}");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(stdout, stderr);
    return new ProcessResult(process.ExitCode, stdout.Result, stderr.Result);
  }

  private static string Html(string value) => WebUtility.HtmlEncode(value);

  private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError) {
    public string Output => StandardOutput + StandardError;
  }
}

// Builds a scratch dedicated-server tree whose Mods/ folder we control.
//
// Why a tree at all: ModManager.LoadMods scans BOTH ModsBasePath (= UserDataFolder + "/Mods") and
// ModsBasePathLegacy (= Application.dataPath + "/../Mods") whenever they differ, so redirecting user data
// can only ADD a mods dir, never exclude the install's. Application.dataPath follows the executable, so
// giving the exe a new home with a real Mods/ beside it is what actually controls the mod set.
//
// Why hardlinks rather than junctions: a recursive delete of a hardlinked tree removes only these names —
// the install's own directory entries keep the data alive. A recursive delete through a directory junction
// can eat the target, which here would be a 17 GB game install.
//
// Caveat inherent to hardlinks: the linked files ARE the install's files. Anything that writes to one
// IN PLACE writes to the install too. Safe here because the game only reads this content (saves, logs and
// generated worlds all go to UserDataFolder), but it is the reason Mods/ content is COPIED, not linked.
using System.Diagnostics;
using System.Runtime.InteropServices;

// Run from the repo root. Paths are derived rather than hardcoded so the tool carries no user- or
// machine-specific state; override Install for a game installed somewhere other than the Steam default.
string Install = Environment.GetEnvironmentVariable("SDTD_SERVER_DIR")
  ?? @"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die Dedicated Server";
string Dest = Path.Combine(Environment.CurrentDirectory, ".scratch", "tier1", "server");

// Everything the server needs to run. Deliberately absent: the install's Mods (the whole point), plus
// Mods_Vanilla / RefugeBotData / appcache / config / logs, which are backups and third-party mod state.
string[] linkedDirs = { "7DaysToDieServer_Data", "Data", "Dependencies_3.0", "MonoBleedingEdge", "Licenses", "Logos" };

if (Path.GetPathRoot(Install)!.TrimEnd('\\') != Path.GetPathRoot(Dest)!.TrimEnd('\\')) {
  Console.WriteLine($"!! hardlinks need one volume: {Path.GetPathRoot(Install)} vs {Path.GetPathRoot(Dest)}");
  return 1;
}

var sw = Stopwatch.StartNew();
if (Directory.Exists(Dest)) {
  Console.WriteLine("removing previous tree (unlinks names only; install data is untouched)...");
  Directory.Delete(Dest, recursive: true);
}
Directory.CreateDirectory(Dest);

int linked = 0, copied = 0;
var failures = new List<string>();

void LinkFile(string src, string dst) {
  Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
  if (Native.CreateHardLink(dst, src, IntPtr.Zero)) { linked++; return; }
  int err = Marshal.GetLastWin32Error();
  // Some files legitimately cannot be linked (e.g. already at the 1024-link limit); fall back to a copy
  // so the tree stays complete, and record it.
  try { File.Copy(src, dst, overwrite: true); copied++; failures.Add($"copied instead of linked (err {err}): {src}"); }
  catch (Exception ex) { failures.Add($"FAILED {src}: {ex.Message}"); }
}

void LinkTree(string srcDir, string dstDir) {
  foreach (string src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)) {
    LinkFile(src, Path.Combine(dstDir, Path.GetRelativePath(srcDir, src)));
  }
}

foreach (string f in Directory.EnumerateFiles(Install)) {
  LinkFile(f, Path.Combine(Dest, Path.GetFileName(f)));
}
Console.WriteLine($"[{sw.Elapsed.TotalSeconds,6:F1}s] root files: {linked} linked");

foreach (string d in linkedDirs) {
  string src = Path.Combine(Install, d);
  if (!Directory.Exists(src)) { failures.Add($"missing source dir: {d}"); continue; }
  int before = linked;
  LinkTree(src, Path.Combine(Dest, d));
  Console.WriteLine($"[{sw.Elapsed.TotalSeconds,6:F1}s] {d}: {linked - before} linked");
}

// The controlled mod set. 0_TFP_Harmony is not optional — every code mod depends on it implicitly, and a
// tree without it loads no code mods at all while looking perfectly healthy.
string harmonySrc = Path.Combine(Install, "Mods_Vanilla", "0_TFP_Harmony");
if (!Directory.Exists(harmonySrc)) { harmonySrc = Path.Combine(Install, "Mods", "0_TFP_Harmony"); }
string harmonyDst = Path.Combine(Dest, "Mods", "0_TFP_Harmony");
Directory.CreateDirectory(harmonyDst);
foreach (string src in Directory.EnumerateFiles(harmonySrc, "*", SearchOption.AllDirectories)) {
  string dst = Path.Combine(harmonyDst, Path.GetRelativePath(harmonySrc, src));
  Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
  File.Copy(src, dst, overwrite: true);
  copied++;
}
Console.WriteLine($"[{sw.Elapsed.TotalSeconds,6:F1}s] Mods/0_TFP_Harmony: copied from {Path.GetFileName(Path.GetDirectoryName(harmonySrc))}");

Console.WriteLine($"\ntree: {Dest}");
Console.WriteLine($"linked {linked} files, copied {copied}, {failures.Count} notes, {sw.Elapsed.TotalSeconds:F1}s");
foreach (string n in failures.Take(20)) { Console.WriteLine($"  - {n}"); }
return failures.Any(f => f.StartsWith("FAILED") || f.StartsWith("missing")) ? 1 : 0;

static class Native {
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
  public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}

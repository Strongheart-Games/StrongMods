// Tier-1 vertical slice: can we spawn a scratch-isolated dedicated server, drive it over telnet, and shut it
// down cleanly? Characterizes the channel (readiness signal, output framing, timings) rather than asserting
// game behavior. Everything it writes stays under .scratch\tier1.
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

// The hardlinked scratch tree built by buildtree.cs — NOT the live install. Application.dataPath follows
// the executable, so running from here makes ModsBasePathLegacy resolve to this tree's own Mods folder.
// Run from the repo root; derived rather than hardcoded so the tool carries no user-specific state.
string Root = Path.Combine(Environment.CurrentDirectory, ".scratch", "tier1");
string ServerTreeDir = Path.Combine(Root, "server");
const int TelnetPort = 8191;
TimeSpan startupTimeout = TimeSpan.FromMinutes(6);
TimeSpan shutdownTimeout = TimeSpan.FromSeconds(90);

string exe = Path.Combine(ServerTreeDir, "7DaysToDieServer.exe");
string cfg = Path.Combine(Root, "config", "serverconfig.xml");
// Unity's -logfile truncates its target, so every run gets its own file — server logs are the primary
// artifact for diagnosing load time and must never be clobbered by a later run.
string logPath = Path.Combine(Root, "logs", $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
var timings = new List<(string Step, double Seconds)>();
var notes = new List<string>();
var sw = Stopwatch.StartNew();
void Mark(string step) {
  timings.Add((step, Math.Round(sw.Elapsed.TotalSeconds, 2)));
  Console.WriteLine($"[{sw.Elapsed.TotalSeconds,7:F2}s] {step}");
}

var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = ServerTreeDir };
foreach (string a in new[] { "-logfile", logPath, "-quit", "-batchmode", "-nographics", $"-configfile={cfg}", "-dedicated" }) {
  psi.ArgumentList.Add(a);
}
Process proc = Process.Start(psi)!;
Mark($"server process started (pid {proc.Id})");

// The owner's readiness marker: the line they watch for to know a server is up. Timed independently of the
// telnet probe so the gap between "log says ready" and "commands actually answer" is measured, not assumed.
const string ReadinessMarker = "Dymesh door replacement";
double markerAt = -1;
var markerWatch = new Thread(() => {
  while (markerAt < 0 && sw.Elapsed < startupTimeout) {
    try {
      using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      using var sr = new StreamReader(fs);
      if (sr.ReadToEnd().Contains(ReadinessMarker, StringComparison.OrdinalIgnoreCase)) {
        markerAt = Math.Round(sw.Elapsed.TotalSeconds, 2);
        Console.WriteLine($"[{markerAt,7:F2}s] log marker seen: \"{ReadinessMarker}\"");
        return;
      }
    } catch { /* Unity holds the log open; a failed read just retries */ }
    Thread.Sleep(500);
  }
}) { IsBackground = true };
markerWatch.Start();

// --- wait for the telnet listener to accept -------------------------------------------------------------
TcpClient? client = null;
while (sw.Elapsed < startupTimeout) {
  if (proc.HasExited) {
    Console.WriteLine($"!! server exited early with code {proc.ExitCode}");
    notes.Add($"Server exited during startup with code {proc.ExitCode} — see server.log.");
    break;
  }
  try {
    var c = new TcpClient();
    c.Connect("127.0.0.1", TelnetPort);
    client = c;
    break;
  } catch { Thread.Sleep(500); }
}
if (client is null) {
  notes.Add("Never reached a telnet connection.");
  File.WriteAllText(Path.Combine(Root, "FINDINGS-partial.md"), string.Join("\n", notes));
  if (!proc.HasExited) { proc.Kill(true); }
  return 1;
}
Mark("telnet accepted a connection");

// --- reader thread: everything the server sends, with arrival timestamps ---------------------------------
NetworkStream stream = client.GetStream();
object gate = new();
var all = new StringBuilder();
DateTime lastRecv = DateTime.UtcNow;
var reader = new Thread(() => {
  var buf = new byte[16384];
  while (true) {
    int n;
    try { n = stream.Read(buf, 0, buf.Length); } catch { break; }
    if (n <= 0) { break; }
    string s = Encoding.UTF8.GetString(buf, 0, n);
    lock (gate) { all.Append(s); lastRecv = DateTime.UtcNow; }
  }
}) { IsBackground = true };
reader.Start();

string Capture(TimeSpan quiet, TimeSpan max) {
  int start;
  lock (gate) { start = all.Length; }
  var until = DateTime.UtcNow + max;
  while (DateTime.UtcNow < until) {
    Thread.Sleep(150);
    lock (gate) {
      if (all.Length > start && DateTime.UtcNow - lastRecv > quiet) { break; }
    }
  }
  lock (gate) { return all.ToString(start, all.Length - start); }
}

string Send(string cmd) {
  byte[] bytes = Encoding.UTF8.GetBytes(cmd + "\r\n");
  int start;
  lock (gate) { start = all.Length; }
  stream.Write(bytes, 0, bytes.Length);
  stream.Flush();
  var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
  while (DateTime.UtcNow < until) {
    Thread.Sleep(150);
    lock (gate) {
      if (all.Length > start && DateTime.UtcNow - lastRecv > TimeSpan.FromMilliseconds(1200)) { break; }
    }
  }
  lock (gate) { return all.ToString(start, all.Length - start); }
}

string banner = Capture(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(15));
Mark("telnet banner settled");

// --- readiness: poll a cheap command until the game answers ----------------------------------------------
string readinessProbe = "";
bool ready = false;
while (sw.Elapsed < startupTimeout && !proc.HasExited) {
  readinessProbe = Send("gettime");
  if (readinessProbe.Contains("Day", StringComparison.OrdinalIgnoreCase)) { ready = true; break; }
  Thread.Sleep(2000);
}
Mark(ready ? "game answered 'gettime' (READY)" : "gave up waiting for readiness");

// --- the command set -------------------------------------------------------------------------------------
var transcripts = new List<(string Cmd, string Out)>();
if (ready) {
  foreach (string cmd in new[] { "version", "gettime", "listplayers", "listents" }) {
    transcripts.Add((cmd, Send(cmd)));
    Mark($"ran '{cmd}'");
  }
}

// --- shutdown ---------------------------------------------------------------------------------------------
bool hardKilled = false;
string shutdownOut = "";
if (!proc.HasExited) {
  try { shutdownOut = Send("shutdown"); } catch (Exception ex) { notes.Add($"shutdown send threw: {ex.Message}"); }
  if (!proc.WaitForExit((int)shutdownTimeout.TotalMilliseconds)) {
    proc.Kill(true);
    proc.WaitForExit(15000);
    hardKilled = true;
    notes.Add("Graceful shutdown did NOT exit within the timeout; hard-killed.");
  }
}
Mark(hardKilled ? "process hard-killed" : "process exited after 'shutdown'");

// --- findings ----------------------------------------------------------------------------------------------
var md = new StringBuilder();
md.AppendLine("# Tier-1 slice findings").AppendLine();
md.AppendLine($"- Exe: `{exe}`");
md.AppendLine($"- Config: `{cfg}`  (ServerPort 26910, TelnetPort {TelnetPort}, GameWorld Navezgane, UserDataFolder -> scratch)");
md.AppendLine($"- Server log: `{logPath}`");
md.AppendLine($"- Reached readiness: **{ready}**   Hard-killed: **{hardKilled}**   Exit code: {(proc.HasExited ? proc.ExitCode.ToString() : "n/a")}");
md.AppendLine($"- Log marker `{ReadinessMarker}` seen at: **{(markerAt < 0 ? "never" : markerAt + "s")}**"
  + (markerAt >= 0 && ready ? $" — first answering command at {timings.First(t => t.Step.Contains("READY")).Seconds}s (gap {Math.Round(timings.First(t => t.Step.Contains("READY")).Seconds - markerAt, 2)}s)" : ""));
md.AppendLine().AppendLine("## Timings").AppendLine().AppendLine("| Step | Elapsed (s) |").AppendLine("|---|---|");
foreach (var (step, secs) in timings) { md.AppendLine($"| {step} | {secs} |"); }
md.AppendLine().AppendLine("## Telnet banner (raw)").AppendLine().AppendLine("```").AppendLine(banner.Trim()).AppendLine("```");
md.AppendLine().AppendLine("## Command transcripts (raw)").AppendLine();
foreach (var (cmd, output) in transcripts) {
  md.AppendLine($"### `{cmd}`").AppendLine("```").AppendLine(output.Trim()).AppendLine("```").AppendLine();
}
md.AppendLine("### `shutdown`").AppendLine("```").AppendLine(shutdownOut.Trim()).AppendLine("```").AppendLine();
if (notes.Count > 0) {
  md.AppendLine("## Notes").AppendLine();
  foreach (string n in notes) { md.AppendLine($"- {n}"); }
}
File.WriteAllText(Path.Combine(Root, "FINDINGS.md"), md.ToString());
lock (gate) { File.WriteAllText(Path.Combine(Root, "telnet-full.txt"), all.ToString()); }
Console.WriteLine($"\nWrote {Path.Combine(Root, "FINDINGS.md")}");
Console.WriteLine($"Server log: {logPath}");
return 0;

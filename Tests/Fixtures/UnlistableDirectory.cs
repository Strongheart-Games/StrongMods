using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tests.Fixtures;

/// <summary>
///   A directory the current process may traverse but not list — mode <c>--x</c> on Unix, a deny ACE on
///   <c>ListDirectory</c> on Windows. That state is what #89 crashed on, and it is not exotic: it is how a
///   shared host keeps one tenant out of another tenant's listing while still letting each reach its own
///   folder underneath it.
///   <para>
///     The induction is VERIFIED rather than assumed, because it silently does not take on a process that
///     bypasses permission checks. <see cref="Supported" /> says up front whether this process can be kept
///     out of a directory at all; <see cref="Induced" /> reports whether it actually was. A test that skipped
///     on an un-induced directory without checking would pass vacuously, which is the failure this whole
///     fixture exists to prevent.
///   </para>
///   Both mechanisms are reversible and <see cref="Dispose" /> reverses them, so a failed test cannot leave
///   an undeletable tree behind.
/// </summary>
public sealed class UnlistableDirectory : IDisposable {
  private readonly string path;

  private UnlistableDirectory(string path) {
    this.path = path;
  }

  /// <summary>Whether the directory is genuinely unlistable right now.</summary>
  public bool Induced { get; private set; }

  /// <summary>
  ///   False when this process ignores directory permissions outright — root on Unix. A Windows process
  ///   still carries its own user SID in its token whether or not it is elevated, so a deny ACE on that SID
  ///   applies either way.
  /// </summary>
  public static bool Supported => OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess;

  /// <summary>
  ///   Creates <paramref name="directory" /> if needed, then locks listing on it. Populate it BEFORE
  ///   calling: writing inside needs the very permission this takes away.
  /// </summary>
  public static UnlistableDirectory At(string directory) {
    Directory.CreateDirectory(directory);
    var blocked = new UnlistableDirectory(directory);
    if (OperatingSystem.IsWindows()) {
      blocked.SetWindowsListDenied(true);
    } else {
      File.SetUnixFileMode(directory, UnixFileMode.UserExecute);
    }

    blocked.Induced = blocked.ListingFails();
    return blocked;
  }

  public void Dispose() {
    try {
      if (OperatingSystem.IsWindows()) {
        SetWindowsListDenied(false);
      } else {
        File.SetUnixFileMode(path,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
    } catch (Exception) {
      // Best effort. The temp tree is disposable either way, and throwing here would mask the real
      // assertion failure that brought us to Dispose.
    }
  }

  private bool ListingFails() {
    try {
      Directory.GetFileSystemEntries(path);
      return false;
    } catch (Exception e) when (e is UnauthorizedAccessException or IOException) {
      return true;
    }
  }

  [SupportedOSPlatform("windows")]
  private void SetWindowsListDenied(bool denied) {
    var info = new DirectoryInfo(path);
    DirectorySecurity security = info.GetAccessControl();
    var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory,
      InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
    if (denied) {
      security.AddAccessRule(rule);
    } else {
      security.RemoveAccessRule(rule);
    }

    info.SetAccessControl(security);
  }
}

// Action detection for build\Deploy.targets and build\Overlay.targets.
//
// Copy with SkipUnchangedFiles=true considers a pair unchanged only when its size and last-write time match.
// This task exposes that same decision so deploy logging reports real file-system actions. It is compiled and
// cached in-process by the RoslynCodeTaskFactory declaration in DeployLogging.targets.

using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;

public class DeployFileChangeDetector : Microsoft.Build.Utilities.Task {
  [Required]
  public ITaskItem[] SourceFiles { get; set; }

  [Required]
  public ITaskItem[] DestinationFiles { get; set; }

  [Output]
  public ITaskItem[] ChangedSourceFiles { get; set; }

  public override bool Execute() {
    if (SourceFiles.Length != DestinationFiles.Length) {
      Log.LogError("Deploy change detection received {0} source files and {1} destination files.",
        SourceFiles.Length, DestinationFiles.Length);
      return false;
    }

    var changed = new List<ITaskItem>();
    for (var i = 0; i < SourceFiles.Length; i++) {
      var source = new FileInfo(SourceFiles[i].ItemSpec);
      var destination = new FileInfo(DestinationFiles[i].ItemSpec);
      if (!destination.Exists || source.Length != destination.Length ||
          source.LastWriteTimeUtc != destination.LastWriteTimeUtc) {
        changed.Add(SourceFiles[i]);
      }
    }

    ChangedSourceFiles = changed.ToArray();
    return true;
  }
}

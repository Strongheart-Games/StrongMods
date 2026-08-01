// The well-formedness check task for build\XmlLint.targets (#16; design: .ai/xml-lint-plan.md).
//
// NOT part of any project: the UsingTask in XmlLint.targets points RoslynCodeTaskFactory at this file, which
// compiles it at build time, caches the assembly by source hash, and runs it in-process. It is a .cs on disk so
// IDEs give it real C# treatment. Only MSBuild's default task references are available when it compiles
// (Microsoft.Build.Framework, Microsoft.Build.Utilities.Core, netstandard) — do not add other dependencies.

using System;
using System.Xml;
using Microsoft.Build.Framework;

public class XmlWellFormednessCheck : Microsoft.Build.Utilities.Task {
  [Required]
  public ITaskItem[] Files { get; set; }

  public override bool Execute() {
    // No DTDs (nothing we ship declares one; Prohibit turns a DOCTYPE into a lint error) and no resolver
    // (the lint must never touch the network or follow external references).
    var settings = new XmlReaderSettings {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      CloseInput = true
    };
    foreach (var item in Files) {
      // FullPath for I/O — MSBuild's current directory is not reliably the project directory.
      var path = item.GetMetadata("FullPath");
      try {
        using (var reader = XmlReader.Create(path, settings)) {
          while (reader.Read()) { }
        }
      } catch (XmlException e) {
        Log.LogError(null, null, null, path, e.LineNumber, e.LinePosition, 0, 0, "{0}", e.Message);
      } catch (Exception e) {
        Log.LogError(null, null, null, path, 0, 0, 0, 0, "{0}", e.Message);
      }
    }
    return !Log.HasLoggedErrors;
  }
}

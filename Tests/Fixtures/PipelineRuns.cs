using System;
using System.Collections.Concurrent;

namespace Tests.Fixtures;

/// <summary>
///   One replayed <see cref="PatchPipeline" /> per declared version label, shared by every test class that
///   asserts over the replay (clean application in <c>PatchApplicationTests</c>, post-patch values in
///   <c>PatchValueAssertionTests</c>). The replay is expensive — every mod's Config applied to the unit's
///   full vanilla XML — so it runs once per label per test session, exactly the memoization
///   <c>PatchApplicationTests</c> carried privately before a second class needed it.
///   Under the <c>-p:SdtdDir</c> escape hatch there is one pseudo-label and the pipeline runs unfiltered
///   against that tree, as before the version axis existed.
/// </summary>
public static class PipelineRuns {
  private static readonly ConcurrentDictionary<string, Lazy<PatchPipeline>> Pipelines = new();

  public static PatchPipeline For(string label) => Pipelines.GetOrAdd(label,
    l => new Lazy<PatchPipeline>(() => PatchPipeline.Run(HostFor(l), SmokeTestCtx.TreeIsDeclared ? l : null))).Value;

  /// <summary>The host a label's pipeline ran in — needed to read its merged documents with
  ///   <see cref="PatcherHost.XmlOf" />, since an XmlFile's type identity lives in its host.</summary>
  public static PatcherHost HostFor(string label) =>
    SmokeTestCtx.TreeIsDeclared ? PatcherHost.ForLabel(label) : PatcherHost.Instance.Value;
}

using Xunit;

namespace Tests.Fixtures;

/// <summary>
///   Conformance tests share one <see cref="PatcherHost" /> — and with it the game's global patch registry and a
///   single log subscription — so they must not run concurrently. xunit never parallelizes within a
///   collection; putting every conformance class in this one serializes them. The smoke tests stay in the
///   default collection: they touch neither.
/// </summary>
[CollectionDefinition(Name)]
public class PatcherHostCollection {
  public const string Name = "PatcherHost";
}

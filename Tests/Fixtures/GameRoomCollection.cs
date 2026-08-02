using Xunit;

namespace Tests.Fixtures;

/// <summary>
///   Conformance tests share one <see cref="GameRoom" /> — and with it the game's global patch registry and a
///   single log subscription — so they must not run concurrently. xunit never parallelizes within a
///   collection; putting every conformance class in this one serializes them. The smoke tests stay in the
///   default collection: they touch neither.
/// </summary>
[CollectionDefinition(Name)]
public class GameRoomCollection {
  public const string Name = "GameRoom";
}

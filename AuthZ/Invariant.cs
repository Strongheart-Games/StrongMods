using System;

namespace AuthZ {
  /// <summary>
  /// One checkable statement about an inbound packet that a well-behaved client never violates.
  /// </summary>
  /// <remarks>
  /// An invariant states a positive fact and reports whether it holds. It never decides what to do about a failure:
  /// that is <see cref="InvariantMode"/>, which a server operator sets. Keeping the two apart is what lets every rule
  /// ship in log-only mode and be promoted to blocking one at a time, on evidence.
  /// </remarks>
  public abstract class Invariant {
    /// <summary>Stable identifier, used as the settings key and in every log line. Never renamed lightly.</summary>
    public abstract string Id { get; }

    public abstract Family Family { get; }

    /// <summary>The package type this invariant reads. Determines what the engine patches.</summary>
    public abstract Type Package { get; }

    /// <summary>One sentence, phrased as the fact that should hold.</summary>
    public abstract string Statement { get; }

    public virtual Severity Severity => Severity.Violation;

    /// <summary>What the operator does when it fails. Set from settings at startup.</summary>
    public InvariantMode Mode { get; set; } = InvariantMode.Log;

    /// <summary>
    /// Returns true when the invariant holds. On false, <paramref name="detail"/> carries what was claimed and what
    /// the server believes instead — the log line is only as useful as this string.
    /// </summary>
    /// <remarks>
    /// Must not throw and must not mutate game state. A check that cannot decide returns true: an invariant engine
    /// that guesses is worse than one that stays quiet.
    /// </remarks>
    public abstract bool Holds(NetPackage package, ClientInfo sender, World world, out string detail);
  }
}

namespace AuthZ {

  /// <summary>What a server operator does when an invariant fails.</summary>
  public enum InvariantMode {
    /// <summary>Do not check at all. No cost.</summary>
    Off,

    /// <summary>Check, log a violation, let the packet through unchanged. The safe default.</summary>
    Log,

    /// <summary>Check, log a violation, and drop the packet.</summary>
    Block
  }

  /// <summary>How alarming a failure is. Severity drives the log level, never the action.</summary>
  public enum Severity {
    /// <summary>A real client could plausibly produce this under lag or an unusual mod.</summary>
    Suspicious,

    /// <summary>No unmodified client produces this.</summary>
    Violation
  }

  /// <summary>The family an invariant belongs to. Documented in <c>.ai/netpackage-invariants.md</c>.</summary>
  public enum Family {
    /// <summary>A — the entity a packet names is the sender's own.</summary>
    SenderBinding,

    /// <summary>B — nothing a player sends may harm another player or what they own.</summary>
    Ownership,

    /// <summary>C — the claim agrees with where the server believes things are.</summary>
    Plausibility,

    /// <summary>D — the packet arrived at a point in the connection where no real client sends it.</summary>
    Lifecycle,

    /// <summary>E — the packet arrived far more often than any real client sends it.</summary>
    Rate,

    /// <summary>F — a field is outside the range the game itself can produce.</summary>
    Domain,

    /// <summary>G — an admin-only operation from a client the server does not consider an admin.</summary>
    Authority
  }
}

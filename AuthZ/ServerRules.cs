namespace AuthZ {
  /// <summary>The server settings the invariants key off.</summary>
  /// <remarks>
  /// Read fresh on every call rather than cached: a server preference can change mid-session, and an invariant that
  /// kept enforcing a rule the operator turned off would be worse than one that checks a game preference each time.
  /// </remarks>
  public static class ServerRules {
    /// <summary>True when the server forbids players killing players outright.</summary>
    public static bool IsNoKilling =>
      (EnumPlayerKillingMode)GamePrefs.GetInt(EnumGamePrefs.PlayerKillingMode) == EnumPlayerKillingMode.NoKilling;

    /// <summary>True when the server runs Easy Anti-Cheat, so an unmodified client is the expected case.</summary>
    /// <remarks>
    /// Not a gate on any invariant. It changes what a violation <em>means</em>: with EAC on, a packet no stock client
    /// can produce is stronger evidence than the same packet on an EAC-off server full of client mods.
    /// </remarks>
    public static bool IsEacEnabled => GamePrefs.GetBool(EnumGamePrefs.EACEnabled);
  }
}

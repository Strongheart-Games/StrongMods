using System;
using System.Collections.Generic;

namespace StrongUtils.KeyValueStore {
  /// <summary>
  ///   Provides persistent storage for globally unique key-value pairs.
  ///   Keys are case-sensitive strings. Values may be any supported primitive type.
  ///   Persistence is implementation-defined (e.g. XML file, SQLite, registry).
  /// </summary>
  public interface IKeyValueStore {
    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    /// <summary>Sets or overwrites a value. Creates the key if it does not exist.</summary>
    void Set(string key, string value);

    void Set(string key, int value);
    void Set(string key, long value);
    void Set(string key, float value);
    void Set(string key, double value);
    void Set(string key, bool value);

    // -------------------------------------------------------------------------
    // Read — typed
    // -------------------------------------------------------------------------

    /// <summary>
    ///   Returns the stored value cast to T, or <paramref name="defaultValue" /> if
    ///   the key is absent or the value cannot be converted to T.
    ///   Supported types: string, int, long, float, double, bool.
    /// </summary>
    T Get<T>(string key, T defaultValue = default) where T : IConvertible;

    // -------------------------------------------------------------------------
    // Read — existence / inspection
    // -------------------------------------------------------------------------

    bool Contains(string key);

    /// <summary>Returns the raw stored string representation of the value, or null if absent.</summary>
    string GetRaw(string key);

    /// <summary>Returns the underlying primitive type tag for the stored value.</summary>
    VarType? GetVarType(string key);

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    /// <summary>Removes the key. No-op if absent.</summary>
    void Remove(string key);

    /// <summary>Removes all stored keys.</summary>
    void Clear();

    // -------------------------------------------------------------------------
    // Enumeration
    // -------------------------------------------------------------------------

    /// <summary>Returns all keys currently in the store.</summary>
    IReadOnlyCollection<string> GetAllKeys();

    // -------------------------------------------------------------------------
    // Atomic test-and-set
    // -------------------------------------------------------------------------

    /// <summary>
    ///   Updates the value for <paramref name="key" /> to <paramref name="newValue" /> only if
    ///   the current stored value equals <paramref name="expectedValue" />.
    ///   Returns true if the update was applied, false if the current value did not match.
    /// </summary>
    bool TestAndSet(string key, string expectedValue, string newValue);

    bool TestAndSet(string key, int expectedValue, int newValue);
    bool TestAndSet(string key, long expectedValue, long newValue);
    bool TestAndSet(string key, float expectedValue, float newValue);
    bool TestAndSet(string key, double expectedValue, double newValue);
    bool TestAndSet(string key, bool expectedValue, bool newValue);

    // -------------------------------------------------------------------------
    // Persistence control
    // -------------------------------------------------------------------------

    /// <summary>
    ///   Immediately flushes any pending writes to the backing store.
    ///   Implementations may auto-flush; this guarantees it.
    /// </summary>
    void Flush();

    /// <summary>
    ///   Reloads the store from the backing source, discarding any unflushed in-memory state.
    /// </summary>
    void Reload();

    // -------------------------------------------------------------------------
    // Change notification
    // -------------------------------------------------------------------------

    /// <summary>
    ///   Raised after any key is created, updated, or deleted.
    ///   The event arg carries the key, old raw value, new raw value, and the change type.
    /// </summary>
    event EventHandler<VarChangedEventArgs> VarChanged;
  }

  // -------------------------------------------------------------------------
  // Supporting types
  // -------------------------------------------------------------------------

  public enum VarType { String, Int, Long, Float, Double, Bool }

  public enum VarChangeType { Created, Updated, Deleted }

  public sealed class VarChangedEventArgs : EventArgs {
    public VarChangedEventArgs(string key, string oldRawValue, string newRawValue, VarChangeType changeType) {
      Key = key;
      OldRawValue = oldRawValue;
      NewRawValue = newRawValue;
      ChangeType = changeType;
    }

    public string Key { get; }
    public string OldRawValue { get; } // null on Created
    public string NewRawValue { get; } // null on Deleted
    public VarChangeType ChangeType { get; }
  }
}

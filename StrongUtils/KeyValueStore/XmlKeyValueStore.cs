using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace StrongUtils.KeyValueStore {
  /// <summary>
  ///   Persists key-value pairs to an XML file.
  ///   All values are stored as their string representation with a type attribute.
  ///   Thread-safe; all mutations are protected by a single lock.
  ///   VarChanged events are raised outside the lock to avoid deadlocks in handlers.
  ///   Malformed persisted state is logged and treated as an empty store so it cannot stop startup.
  /// </summary>
  public class XmlKeyValueStore : IKeyValueStore {
    private const string RootElement = "Store";
    private const string EntryElement = "Entry";
    private const string KeyAttr = "key";
    private const string TypeAttr = "type";
    private const string ValueAttr = "value";
    private readonly string _filePath;
    private readonly object _lock = new();

    // In-memory store: key -> (rawValue, type)
    private readonly Dictionary<string, (string Raw, VarType Type)> _store = new();

    public XmlKeyValueStore(string filePath) {
      _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
      if (File.Exists(_filePath)) {
        Load();
      }
    }

    public event EventHandler<VarChangedEventArgs> VarChanged;

    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    public void Set(string key, string value) {
      SetInternal(key, value, VarType.String);
    }

    public void Set(string key, int value) {
      SetInternal(key, value.ToString(), VarType.Int);
    }

    public void Set(string key, long value) {
      SetInternal(key, value.ToString(), VarType.Long);
    }

    public void Set(string key, float value) {
      SetInternal(key, value.ToString("R", CultureInfo.InvariantCulture), VarType.Float);
    }

    public void Set(string key, double value) {
      SetInternal(key, value.ToString("R", CultureInfo.InvariantCulture), VarType.Double);
    }

    public void Set(string key, bool value) {
      SetInternal(key, value.ToString(), VarType.Bool);
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    public T Get<T>(string key, T defaultValue = default) where T : IConvertible {
      lock (_lock) {
        if (!_store.TryGetValue(key, out (string Raw, VarType Type) entry)) {
          return defaultValue;
        }

        try {
          return (T)Convert.ChangeType(entry.Raw, typeof(T), CultureInfo.InvariantCulture);
        } catch {
          return defaultValue;
        }
      }
    }

    public bool Contains(string key) {
      lock (_lock) {
        return _store.ContainsKey(key);
      }
    }

    public string GetRaw(string key) {
      lock (_lock) {
        return _store.TryGetValue(key, out (string Raw, VarType Type) e) ? e.Raw : null;
      }
    }

    public VarType? GetVarType(string key) {
      lock (_lock) {
        return _store.TryGetValue(key, out (string Raw, VarType Type) e) ? e.Type : null;
      }
    }

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    public void Remove(string key) {
      string oldRaw;

      lock (_lock) {
        if (!_store.TryGetValue(key, out (string Raw, VarType Type) entry)) {
          return;
        }

        oldRaw = entry.Raw;
        _store.Remove(key);
        FlushLocked();
      }

      RaiseChanged(key, oldRaw, null, VarChangeType.Deleted);
    }

    public void Clear() {
      List<(string Key, string Raw)> entries;

      lock (_lock) {
        entries = new List<(string Key, string Raw)>();
        foreach (KeyValuePair<string, (string Raw, VarType Type)> entry in _store) {
          entries.Add((entry.Key, entry.Value.Raw));
        }

        _store.Clear();
        FlushLocked();
      }

      foreach ((string key, string raw) in entries) {
        RaiseChanged(key, raw, null, VarChangeType.Deleted);
      }
    }

    // -------------------------------------------------------------------------
    // Enumeration
    // -------------------------------------------------------------------------

    public IReadOnlyCollection<string> GetAllKeys() {
      lock (_lock) {
        return new List<string>(_store.Keys);
      }
    }

    // -------------------------------------------------------------------------
    // Atomic test-and-set
    // -------------------------------------------------------------------------

    public bool TestAndSet(string key, string expectedValue, string newValue) {
      return TasInternal(key, expectedValue, newValue, VarType.String);
    }

    public bool TestAndSet(string key, int expectedValue, int newValue) {
      return TasInternal(key, expectedValue.ToString(), newValue.ToString(), VarType.Int);
    }

    public bool TestAndSet(string key, long expectedValue, long newValue) {
      return TasInternal(key, expectedValue.ToString(), newValue.ToString(), VarType.Long);
    }

    public bool TestAndSet(string key, float expectedValue, float newValue) {
      return TasInternal(key, expectedValue.ToString("R", CultureInfo.InvariantCulture),
        newValue.ToString("R", CultureInfo.InvariantCulture), VarType.Float);
    }

    public bool TestAndSet(string key, double expectedValue, double newValue) {
      return TasInternal(key, expectedValue.ToString("R", CultureInfo.InvariantCulture),
        newValue.ToString("R", CultureInfo.InvariantCulture), VarType.Double);
    }

    public bool TestAndSet(string key, bool expectedValue, bool newValue) {
      return TasInternal(key, expectedValue.ToString(), newValue.ToString(), VarType.Bool);
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    /// <summary>Flushes pending writes to disk. Acquires the lock.</summary>
    public void Flush() {
      lock (_lock) {
        FlushLocked();
      }
    }

    public void Reload() {
      lock (_lock) {
        _store.Clear();
        if (File.Exists(_filePath)) {
          Load();
        }
      }
    }

    private void SetInternal(string key, string raw, VarType type) {
      ValidateKey(key);

      string oldRaw;
      VarChangeType changeType;

      lock (_lock) {
        if (_store.TryGetValue(key, out (string Raw, VarType Type) existing)) {
          oldRaw = existing.Raw;
          changeType = VarChangeType.Updated;
        } else {
          oldRaw = null;
          changeType = VarChangeType.Created;
        }

        _store[key] = (raw, type);
        FlushLocked();
      }

      RaiseChanged(key, oldRaw, raw, changeType);
    }

    private bool TasInternal(string key, string expectedRaw, string newRaw, VarType type) {
      ValidateKey(key);

      lock (_lock) {
        if (!_store.TryGetValue(key, out (string Raw, VarType Type) entry)) {
          Log.Out("Not found");
          return false;
        }

        // Type mismatch → treat as no-match
        if (entry.Type != type) {
          Log.Out($"Type mismatch: expected {type}, got {entry.Type}");
          return false;
        }

        if (!string.Equals(entry.Raw, expectedRaw, StringComparison.Ordinal)) {
          Log.Out($"Value mismatch: expected {expectedRaw}, got {entry.Raw}");
          return false;
        }

        _store[key] = (newRaw, type);
        FlushLocked();
      }

      RaiseChanged(key, expectedRaw, newRaw, VarChangeType.Updated);
      return true;
    }

    /// <summary>Flushes to disk. Caller must already hold <see cref="_lock" />.</summary>
    private void FlushLocked() {
      var root = new XElement(RootElement);

      foreach (KeyValuePair<string, (string Raw, VarType Type)> kvp in _store) {
        root.Add(new XElement(EntryElement,
          new XAttribute(KeyAttr, kvp.Key),
          new XAttribute(TypeAttr, kvp.Value.Type.ToString()),
          new XAttribute(ValueAttr, kvp.Value.Raw)));
      }

      new XDocument(root).Save(_filePath);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>Loads from disk. Caller must already hold <see cref="_lock" /> or be in the constructor.</summary>
    private void Load() {
      try {
        var doc = XDocument.Load(_filePath);
        XElement root = doc.Root;
        if (root is null || root.Name != RootElement) {
          throw new InvalidDataException($"Expected a {RootElement} root element.");
        }

        var loaded = new Dictionary<string, (string Raw, VarType Type)>();
        foreach (XElement el in root.Elements(EntryElement)) {
          string key = el.Attribute(KeyAttr)?.Value;
          string raw = el.Attribute(ValueAttr)?.Value;
          string typeName = el.Attribute(TypeAttr)?.Value;
          if (key is null || raw is null || typeName is null ||
              !Enum.TryParse(typeName, out VarType type) || !Enum.IsDefined(typeof(VarType), type)) {
            throw new InvalidDataException("An Entry must have key, type, and value attributes with a supported type.");
          }

          if (!string.IsNullOrEmpty(key)) {
            loaded[key] = (raw, type);
          }
        }

        _store.Clear();
        foreach (KeyValuePair<string, (string Raw, VarType Type)> entry in loaded) {
          _store[entry.Key] = entry.Value;
        }
      } catch (Exception e) when (e is InvalidDataException or XmlException) {
        _store.Clear();
        Log.Error($"[StrongUtils] Ignoring malformed key-value store '{_filePath}': {e.Message}");
      }
    }

    private static void ValidateKey(string key) {
      if (string.IsNullOrWhiteSpace(key)) {
        throw new ArgumentException("Key must be a non-empty, non-whitespace string.", nameof(key));
      }
    }

    private void RaiseChanged(string key, string oldRaw, string newRaw, VarChangeType changeType) {
      VarChanged?.Invoke(this, new VarChangedEventArgs(key, oldRaw, newRaw, changeType));
    }
  }
}

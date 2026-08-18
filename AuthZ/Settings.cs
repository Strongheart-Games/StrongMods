using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace AuthZ {
  /// <summary>
  /// Per-invariant mode plus the few thresholds a rule needs, read from
  /// <c>&lt;save game folder&gt;/StrongMods/AuthZ.xml</c>.
  /// </summary>
  /// <remarks>
  /// Defaults put every invariant in <see cref="InvariantMode.Log"/>. Nothing blocks until an operator says so,
  /// because a rule that has never run against real traffic has no business dropping packets.
  /// </remarks>
  public sealed class Settings {
    public static Settings Current { get; private set; } = new();

    /// <summary>Mode overrides, keyed by <see cref="Invariant.Id"/>.</summary>
    public Dictionary<string, InvariantMode> Modes { get; } = new();

    public InvariantMode DefaultMode = InvariantMode.Log;

    /// <summary>Ceiling for <c>exp.delta</c>. Generous by default; tighten it from your own logs.</summary>
    public int MaxExperiencePerPacket = 100000;

    public static void Load(XDocument document, Action<string> warn) {
      var settings = new Settings();
      var root = document?.Root;
      if (root == null) {
        Current = settings;
        return;
      }

      var defaults = root.Element("defaults");
      if (defaults != null) {
        settings.DefaultMode = ReadMode(defaults, "mode", settings.DefaultMode, warn);
      }

      var thresholds = root.Element("thresholds");
      if (thresholds != null) {
        var raw = (string)thresholds.Attribute("max_experience_per_packet");
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0) {
          settings.MaxExperiencePerPacket = value;
        } else if (raw != null) {
          warn("thresholds/@max_experience_per_packet must be a positive integer; keeping " +
               settings.MaxExperiencePerPacket + ".");
        }
      }

      var invariants = root.Element("invariants");
      if (invariants != null) {
        foreach (var element in invariants.Elements("invariant")) {
          var id = (string)element.Attribute("id");
          if (string.IsNullOrEmpty(id)) {
            continue;
          }

          settings.Modes[id.Trim()] = ReadMode(element, "mode", settings.DefaultMode, warn);
        }
      }

      Current = settings;
    }

    /// <summary>The mode configured for one invariant id, or the file's default when it names none.</summary>
    public InvariantMode ModeFor(string id) {
      return Modes.TryGetValue(id, out var mode) ? mode : DefaultMode;
    }

    /// <summary>
    /// Reports every settings entry that names an invariant this build does not have.
    /// </summary>
    /// <remarks>
    /// Silence here would be the worst outcome: an operator who mistypes an id, or keeps one across a rename, would
    /// believe a rule is configured while it is not running at all.
    /// </remarks>
    public void ReportUnknownIds(ICollection<string> knownIds, Action<string> warn) {
      foreach (var id in Modes.Keys) {
        if (!knownIds.Contains(id)) {
          warn("invariant id '" + id + "' is not one this build knows about; the setting does nothing.");
        }
      }
    }

    private static InvariantMode ReadMode(XElement element, string name, InvariantMode fallback, Action<string> warn) {
      var raw = (string)element.Attribute(name);
      if (raw == null) {
        return fallback;
      }

      if (Enum.TryParse<InvariantMode>(raw.Trim(), ignoreCase: true, out var mode)) {
        return mode;
      }

      warn("'" + raw + "' is not a mode (off, log, block); using " + fallback + ".");
      return fallback;
    }
  }
}

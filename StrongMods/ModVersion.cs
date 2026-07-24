using System.Collections.Generic;

namespace StrongMods {
  /// <summary>
  ///   A numeric-segment version ("1.2.3") as defined by the ModInfo Dependencies specification (section 4).
  ///   Segments are compared numerically, left to right; missing segments are treated as zero, so "1.2" equals "1.2.0".
  ///   Deliberately not System.Version, which throws on inputs outside two to four numeric parts.
  /// </summary>
  public readonly struct ModVersion {
    private readonly int[] _segments;

    public string Original { get; }

    private ModVersion(int[] segments, string original) {
      _segments = segments;
      Original = original;
    }

    public int SegmentCount => _segments?.Length ?? 0;

    /// <summary>Returns the segment at <paramref name="index" />, or zero past the end.</summary>
    public int GetSegment(int index) {
      return _segments != null && index < _segments.Length ? _segments[index] : 0;
    }

    /// <summary>
    ///   Parses a version after normalization (section 4.3): surrounding whitespace is trimmed and a single leading
    ///   'V'/'v' is stripped. Segments must be digits only — prerelease labels and build suffixes are rejected.
    /// </summary>
    public static bool TryParse(string text, out ModVersion version) {
      version = default;
      var normalized = Normalize(text);
      if (string.IsNullOrEmpty(normalized)) {
        return false;
      }

      var parts = normalized.Split('.');
      var segments = new int[parts.Length];
      for (var i = 0; i < parts.Length; i++) {
        var part = parts[i];
        if (part.Length == 0) {
          return false;
        }

        var value = 0;
        foreach (var c in part) {
          if (c < '0' || c > '9') {
            return false;
          }

          value = value * 10 + (c - '0');
        }

        segments[i] = value;
      }

      version = new ModVersion(segments, text);
      return true;
    }

    public static string Normalize(string text) {
      if (text is null) {
        return null;
      }

      var trimmed = text.Trim();
      if (trimmed.Length > 0 && (trimmed[0] == 'V' || trimmed[0] == 'v')) {
        trimmed = trimmed.Substring(1);
      }

      return trimmed;
    }

    /// <summary>Segment-wise numeric comparison with zero-padding (section 4.4).</summary>
    public static int Compare(ModVersion a, ModVersion b) {
      var length = a.SegmentCount > b.SegmentCount ? a.SegmentCount : b.SegmentCount;
      for (var i = 0; i < length; i++) {
        var result = a.GetSegment(i).CompareTo(b.GetSegment(i));
        if (result != 0) {
          return result;
        }
      }

      return 0;
    }

    public int CompareTo(ModVersion other) {
      return Compare(this, other);
    }

    public override string ToString() {
      if (_segments is null) {
        return string.Empty;
      }

      var parts = new List<string>(_segments.Length);
      foreach (var segment in _segments) {
        parts.Add(segment.ToString());
      }

      return string.Join(".", parts);
    }
  }
}

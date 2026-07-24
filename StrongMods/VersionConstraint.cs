namespace StrongMods {
  /// <summary>
  ///   A version constraint in NuGet interval notation, per section 4 of the ModInfo Dependencies specification:
  ///   a bare version is a minimum, "[1.2]" is an exact pin, "[1.2,2.0)" is a half-open range, "3.*" floats on the
  ///   listed prefix, and "*" matches anything.
  /// </summary>
  public sealed class VersionConstraint {
    private readonly ModVersion _floatingPrefix; // segments preceding the asterisk
    private readonly int _floatingSegmentCount;
    private readonly bool _hasLower;
    private readonly bool _hasUpper;

    private readonly Kind _kind;
    private readonly ModVersion _lower;
    private readonly bool _lowerInclusive;
    private readonly ModVersion _upper;
    private readonly bool _upperInclusive;

    private VersionConstraint(string original, Kind kind, ModVersion floatingPrefix, int floatingSegmentCount,
      bool hasLower, ModVersion lower, bool lowerInclusive, bool hasUpper, ModVersion upper, bool upperInclusive) {
      Original = original;
      _kind = kind;
      _floatingPrefix = floatingPrefix;
      _floatingSegmentCount = floatingSegmentCount;
      _hasLower = hasLower;
      _lower = lower;
      _lowerInclusive = lowerInclusive;
      _hasUpper = hasUpper;
      _upper = upper;
      _upperInclusive = upperInclusive;
    }

    public string Original { get; }

    /// <summary>
    ///   Parses a constraint. On failure, <paramref name="error" /> describes the authoring error without naming the
    ///   declaring mod; callers add that context.
    /// </summary>
    public static bool TryParse(string text, out VersionConstraint constraint, out string error) {
      constraint = null;
      error = null;

      var trimmed = text?.Trim();
      if (string.IsNullOrEmpty(trimmed)) {
        error = "constraint is empty";
        return false;
      }

      if (trimmed == "*") {
        constraint = new VersionConstraint(text, Kind.Any, default, 0, false, default, false, false, default, false);
        return true;
      }

      if (trimmed[0] == '[' || trimmed[0] == '(') {
        return TryParseRange(text, trimmed, out constraint, out error);
      }

      if (trimmed.Contains("*")) {
        return TryParseFloating(text, trimmed, out constraint, out error);
      }

      // Bare version: a minimum, equivalent to "[version,)"
      if (!ModVersion.TryParse(trimmed, out ModVersion minimum)) {
        error = $"version '{trimmed}' is not a dot-separated list of numbers";
        return false;
      }

      constraint = new VersionConstraint(text, Kind.Range, default, 0, true, minimum, true, false, default, false);
      return true;
    }

    private static bool TryParseFloating(string original, string trimmed, out VersionConstraint constraint,
      out string error) {
      constraint = null;
      error = null;

      if (!trimmed.EndsWith(".*") || trimmed.IndexOf('*') != trimmed.Length - 1) {
        error = $"in '{trimmed}' the asterisk may appear only as the final segment";
        return false;
      }

      var prefixText = trimmed.Substring(0, trimmed.Length - 2);
      if (!ModVersion.TryParse(prefixText, out ModVersion prefix)) {
        error = $"version '{prefixText}' is not a dot-separated list of numbers";
        return false;
      }

      constraint = new VersionConstraint(original, Kind.Floating, prefix, prefix.SegmentCount, false, default, false,
        false, default, false);
      return true;
    }

    private static bool TryParseRange(string original, string trimmed, out VersionConstraint constraint,
      out string error) {
      constraint = null;
      error = null;

      var lastChar = trimmed[trimmed.Length - 1];
      if (lastChar != ']' && lastChar != ')') {
        error = $"range '{trimmed}' does not end with ']' or ')'";
        return false;
      }

      var lowerInclusive = trimmed[0] == '[';
      var upperInclusive = lastChar == ']';
      var inner = trimmed.Substring(1, trimmed.Length - 2);

      if (inner.Contains("*")) {
        error = $"range '{trimmed}' may not contain an asterisk";
        return false;
      }

      var commaIndex = inner.IndexOf(',');
      if (commaIndex < 0) {
        // Single-version form: only "[version]" (exact) is valid
        if (!lowerInclusive || !upperInclusive) {
          error = $"'{trimmed}' is not valid; an exact version must use square brackets, like [1.2]";
          return false;
        }

        if (!ModVersion.TryParse(inner, out ModVersion exact)) {
          error = $"version '{inner.Trim()}' is not a dot-separated list of numbers";
          return false;
        }

        constraint = new VersionConstraint(original, Kind.Range, default, 0, true, exact, true, true, exact, true);
        return true;
      }

      if (inner.IndexOf(',', commaIndex + 1) >= 0) {
        error = $"range '{trimmed}' contains more than one comma";
        return false;
      }

      var lowerText = inner.Substring(0, commaIndex).Trim();
      var upperText = inner.Substring(commaIndex + 1).Trim();
      if (lowerText.Length == 0 && upperText.Length == 0) {
        error = $"range '{trimmed}' must include at least one bound";
        return false;
      }

      var hasLower = lowerText.Length > 0;
      var hasUpper = upperText.Length > 0;
      ModVersion lower = default;
      ModVersion upper = default;
      if (hasLower && !ModVersion.TryParse(lowerText, out lower)) {
        error = $"version '{lowerText}' is not a dot-separated list of numbers";
        return false;
      }

      if (hasUpper && !ModVersion.TryParse(upperText, out upper)) {
        error = $"version '{upperText}' is not a dot-separated list of numbers";
        return false;
      }

      if (hasLower && hasUpper) {
        var comparison = ModVersion.Compare(lower, upper);
        if (comparison > 0 || (comparison == 0 && (!lowerInclusive || !upperInclusive))) {
          error = $"range '{trimmed}' matches nothing";
          return false;
        }
      }

      constraint = new VersionConstraint(original, Kind.Range, default, 0, hasLower, lower, lowerInclusive, hasUpper,
        upper, upperInclusive);
      return true;
    }

    public bool Satisfies(ModVersion version) {
      switch (_kind) {
        case Kind.Any:
          return true;
        case Kind.Floating: {
          for (var i = 0; i < _floatingSegmentCount; i++) {
            if (version.GetSegment(i) != _floatingPrefix.GetSegment(i)) {
              return false;
            }
          }

          return true;
        }
        default: {
          if (_hasLower) {
            var comparison = ModVersion.Compare(version, _lower);
            if (comparison < 0 || (comparison == 0 && !_lowerInclusive)) {
              return false;
            }
          }

          if (_hasUpper) {
            var comparison = ModVersion.Compare(version, _upper);
            if (comparison > 0 || (comparison == 0 && !_upperInclusive)) {
              return false;
            }
          }

          return true;
        }
      }
    }

    public override string ToString() {
      return Original?.Trim();
    }

    private enum Kind {
      Any,
      Floating,
      Range
    }
  }
}

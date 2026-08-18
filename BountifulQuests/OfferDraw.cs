using System.Collections.Generic;

namespace BountifulQuests {
  /// <summary>
  /// Picks which quest to try next when filling one tier page, applying the configured weights, per-type caps and
  /// repeat avoidance.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Free of game types on purpose. The caller hands over the quest ids it is willing to offer, in its own order, and
  /// gets back indices into that same list. Everything the game knows and this does not — whether a POI could be
  /// found, whether spawning is on — stays with the caller, which reports the outcome through
  /// <see cref="Accept"/> or <see cref="Reject"/>.
  /// </para>
  /// <para>
  /// Vanilla draws uniformly from the trader's quest_list rows, so a tier naming two clear variants and one fetch is
  /// two-thirds clear whether or not anyone chose that. Weighting here replaces row-counting with intent.
  /// </para>
  /// </remarks>
  public sealed class OfferDraw {
    private readonly OfferConfig _config;
    private readonly string[] _questIds;
    private readonly int[] _weights;
    private readonly bool[] _removed;
    private readonly bool[] _usedThisPage;
    private readonly Dictionary<string, int> _takenByRule = new();

    public OfferDraw(OfferConfig config, IList<string> questIds) {
      _config = config;
      _questIds = new string[questIds.Count];
      _weights = new int[questIds.Count];
      _removed = new bool[questIds.Count];
      _usedThisPage = new bool[questIds.Count];
      for (var i = 0; i < questIds.Count; i++) {
        _questIds[i] = questIds[i];
        _weights[i] = config.WeightFor(questIds[i]);
      }
    }

    /// <summary>How many offers this page has accepted so far.</summary>
    public int Accepted { get; private set; }

    /// <summary>
    /// Chooses the next candidate. <paramref name="roll"/> is a uniform value in [0,1). Returns -1 when nothing is
    /// eligible, which is the caller's signal to stop filling this page.
    /// </summary>
    /// <remarks>
    /// Repeat avoidance is a preference, not a rule: when every remaining candidate has already appeared on this
    /// page, the page keeps filling with repeats rather than ending short.
    /// </remarks>
    public int Draw(double roll) {
      var index = DrawFrom(roll, skipUsed: _config.AvoidRepeatOnPage);
      if (index < 0 && _config.AvoidRepeatOnPage) {
        index = DrawFrom(roll, skipUsed: false);
      }

      return index;
    }

    /// <summary>Records that the candidate produced a real offer.</summary>
    public void Accept(int index) {
      Accepted++;
      _usedThisPage[index] = true;

      var rule = _config.RuleFor(_questIds[index]);
      if (rule == null) {
        return;
      }

      _takenByRule.TryGetValue(rule.Match, out var taken);
      _takenByRule[rule.Match] = taken + 1;
    }

    /// <summary>Records that the candidate could not produce an offer this time. It stays eligible.</summary>
    public void Reject(int index) {
      // Nothing to record: vanilla also retries a candidate that failed to find a POI.
    }

    /// <summary>Takes a candidate out of the running for the rest of the page (vanilla's single_quest rule).</summary>
    public void Remove(int index) {
      _removed[index] = true;
    }

    /// <summary>Whether the candidate may still be drawn.</summary>
    public bool IsEligible(int index) {
      if (_removed[index] || _weights[index] <= 0) {
        return false;
      }

      var rule = _config.RuleFor(_questIds[index]);
      if (rule == null || rule.Max <= 0) {
        return true;
      }

      _takenByRule.TryGetValue(rule.Match, out var taken);
      return taken < rule.Max;
    }

    private int DrawFrom(double roll, bool skipUsed) {
      var total = 0;
      for (var i = 0; i < _questIds.Length; i++) {
        if (IsEligible(i) && !(skipUsed && _usedThisPage[i])) {
          total += _weights[i];
        }
      }

      if (total <= 0) {
        return -1;
      }

      var target = roll * total;
      var running = 0;
      for (var i = 0; i < _questIds.Length; i++) {
        if (!IsEligible(i) || (skipUsed && _usedThisPage[i])) {
          continue;
        }

        running += _weights[i];
        if (target < running) {
          return i;
        }
      }

      // Floating-point drift at the top of the range: fall back to the last eligible candidate.
      for (var i = _questIds.Length - 1; i >= 0; i--) {
        if (IsEligible(i) && !(skipUsed && _usedThisPage[i])) {
          return i;
        }
      }

      return -1;
    }
  }
}

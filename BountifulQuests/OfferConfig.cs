using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace BountifulQuests {
  /// <summary>
  /// How one class of quest is drawn. A row of the <c>&lt;types&gt;</c> table in <c>Config/BountifulQuests.xml</c>.
  /// </summary>
  /// <remarks>
  /// <see cref="Match"/> is a glob over the quest id, so it keeps working for quests this repo did not write.
  /// When two rows both match an id, the LONGER pattern wins — see <see cref="OfferConfig.WeightFor"/>. That rule is
  /// order-independent on purpose: an admin appending a row to the table cannot silently change what earlier rows do.
  /// </remarks>
  public sealed class QuestTypeRule {
    public string Match = "*";
    public int Weight = 1;
    public int Max;
  }

  /// <summary>Final ordering of a trader's offer page.</summary>
  public enum OfferSort {
    /// <summary>Nearest POI first. What vanilla does.</summary>
    Distance,
    Tier,
    Type,
    None
  }

  /// <summary>
  /// Every knob BountifulQuests exposes, plus the parser that reads them and the clamping that keeps them sane.
  /// </summary>
  /// <remarks>
  /// Deliberately free of game types so the selection logic can be exercised without the game. Defaults reproduce
  /// vanilla behaviour exactly, so a missing or unreadable config file is a safe outcome rather than a broken server.
  /// </remarks>
  public sealed class OfferConfig {
    public const int MinOffersPerTier = 4;
    public const int MaxOffersPerTier = 64;

    /// <summary>
    /// How many offer rows the shipped XML patches can actually show on one tier page: 24 response rows in the
    /// grown <c>windowResponses</c> grid, minus "nevermind" and the deduplicated prev and next links.
    /// </summary>
    /// <remarks>
    /// Not a clamp. Offers past this point are generated and are real; they are simply unreachable in dialog until
    /// the display grows. <see cref="ModApi"/> logs the shortfall at startup so it is never a silent one.
    /// </remarks>
    public const int DisplayBudgetPerTier = 21;

    /// <summary>Vanilla's <c>distanceIndices</c> is <c>{0,0,0,1,2,2,2}</c> — three near, one mid, three far.</summary>
    public static readonly int[] VanillaBandWeights = { 3, 1, 3 };

    public int OffersPerTier = 7;
    public int MinTier = 1;

    /// <summary>Highest tier page offered. Zero means "the player's current faction tier", which is vanilla.</summary>
    public int MaxTier;

    public OfferSort Sort = OfferSort.Distance;

    /// <summary>Metres from the trader area. A POI nearer than this is refused. Zero disables the floor.</summary>
    public float MinDistance;

    /// <summary>Metres from the trader area. A POI further than this is refused. Zero disables the cap.</summary>
    public float MaxDistance;

    public float NearBandEdge = 500f;
    public float MidBandEdge = 1500f;
    public int[] BandWeights = (int[])VanillaBandWeights.Clone();

    public int DefaultWeight = 1;
    public List<QuestTypeRule> TypeRules = new();

    public bool AvoidRepeatOnPage;

    /// <summary>Whether every knob survived clamping. False means the log carries at least one correction.</summary>
    public bool LoadedClean = true;

    /// <summary>
    /// Reads the config document. Never throws: a malformed document yields defaults, which are vanilla behaviour.
    /// </summary>
    public static OfferConfig Parse(XDocument document, Action<string> warn) {
      var config = new OfferConfig();
      var root = document?.Root;
      if (root == null) {
        return config;
      }

      var offers = root.Element("offers");
      if (offers != null) {
        config.OffersPerTier = ReadInt(offers, "per_tier", config.OffersPerTier);
        config.MinTier = ReadInt(offers, "min_tier", config.MinTier);
        config.MaxTier = ReadInt(offers, "max_tier", config.MaxTier);
        config.Sort = ReadSort(offers, "sort", config.Sort);
      }

      var distance = root.Element("distance");
      if (distance != null) {
        config.MinDistance = ReadFloat(distance, "min", config.MinDistance);
        config.MaxDistance = ReadFloat(distance, "max", config.MaxDistance);
        config.NearBandEdge = ReadFloat(distance, "near", config.NearBandEdge);
        config.MidBandEdge = ReadFloat(distance, "mid", config.MidBandEdge);
        config.BandWeights = ReadBandWeights(distance, "band_weights", config.BandWeights);
      }

      var types = root.Element("types");
      if (types != null) {
        config.DefaultWeight = ReadInt(types, "default_weight", config.DefaultWeight);
        foreach (var element in types.Elements("type")) {
          var match = (string)element.Attribute("match");
          if (string.IsNullOrWhiteSpace(match)) {
            continue;
          }

          config.TypeRules.Add(new QuestTypeRule {
            Match = match.Trim(),
            Weight = ReadInt(element, "weight", 1),
            Max = ReadInt(element, "max", 0)
          });
        }
      }

      var repeats = root.Element("repeats");
      if (repeats != null) {
        config.AvoidRepeatOnPage = ReadBool(repeats, "avoid_offered", config.AvoidRepeatOnPage);
      }

      config.Clamp(warn);
      return config;
    }

    /// <summary>
    /// Pulls every out-of-range value back inside its bounds, reporting each correction once.
    /// </summary>
    public void Clamp(Action<string> warn) {
      OffersPerTier = ClampInt("offers/@per_tier", OffersPerTier, MinOffersPerTier, MaxOffersPerTier, warn);
      MinTier = ClampInt("offers/@min_tier", MinTier, 1, 6, warn);
      MaxTier = ClampInt("offers/@max_tier", MaxTier, 0, 6, warn);
      if (MaxTier != 0 && MaxTier < MinTier) {
        Report(warn, "offers/@max_tier (" + MaxTier + ") is below offers/@min_tier (" + MinTier + "); raising it.");
        MaxTier = MinTier;
      }

      MinDistance = ClampFloat("distance/@min", MinDistance, 0f, 100000f, warn);
      MaxDistance = ClampFloat("distance/@max", MaxDistance, 0f, 100000f, warn);
      if (MaxDistance != 0f && MaxDistance < MinDistance) {
        Report(warn, "distance/@max (" + MaxDistance + ") is below distance/@min (" + MinDistance + "); ignoring both.");
        MinDistance = 0f;
        MaxDistance = 0f;
      }

      NearBandEdge = ClampFloat("distance/@near", NearBandEdge, 1f, 100000f, warn);
      MidBandEdge = ClampFloat("distance/@mid", MidBandEdge, 1f, 100000f, warn);
      if (MidBandEdge <= NearBandEdge) {
        Report(warn, "distance/@mid (" + MidBandEdge + ") is not above distance/@near (" + NearBandEdge +
                     "); restoring the vanilla 500/1500 edges.");
        NearBandEdge = 500f;
        MidBandEdge = 1500f;
      }

      if (BandWeights == null || BandWeights.Length != 3 || BandWeights.Any(w => w < 0) || BandWeights.Sum() <= 0) {
        Report(warn, "distance/@band_weights must be three non-negative integers with a positive sum; " +
                     "restoring the vanilla 3,1,3.");
        BandWeights = (int[])VanillaBandWeights.Clone();
      }

      DefaultWeight = ClampInt("types/@default_weight", DefaultWeight, 0, 1000, warn);
      foreach (var rule in TypeRules) {
        rule.Weight = ClampInt("type[@match='" + rule.Match + "']/@weight", rule.Weight, 0, 1000, warn);
        rule.Max = ClampInt("type[@match='" + rule.Match + "']/@max", rule.Max, 0, MaxOffersPerTier, warn);
      }
    }

    /// <summary>
    /// The draw weight for one quest id: the longest matching <c>&lt;type&gt;</c> row, or
    /// <see cref="DefaultWeight"/> when nothing matches. Returns null when no row matched.
    /// </summary>
    public QuestTypeRule RuleFor(string questId) {
      QuestTypeRule best = null;
      foreach (var rule in TypeRules) {
        if (!Glob.IsMatch(questId, rule.Match)) {
          continue;
        }

        if (best == null || rule.Match.Length > best.Match.Length) {
          best = rule;
        }
      }

      return best;
    }

    public int WeightFor(string questId) {
      return RuleFor(questId)?.Weight ?? DefaultWeight;
    }

    /// <summary>
    /// Which distance band offer <paramref name="slot"/> should prefer: 0 near, 1 mid, 2 far. The band weights
    /// describe one cycle, and the cycle repeats for however many offers a page holds.
    /// </summary>
    public int BandForSlot(int slot) {
      var cycle = BandWeights[0] + BandWeights[1] + BandWeights[2];
      var position = ((slot % cycle) + cycle) % cycle;
      if (position < BandWeights[0]) {
        return 0;
      }

      return position < BandWeights[0] + BandWeights[1] ? 1 : 2;
    }

    private static int ReadInt(XElement element, string name, int fallback) {
      var raw = (string)element.Attribute(name);
      return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static float ReadFloat(XElement element, string name, float fallback) {
      var raw = (string)element.Attribute(name);
      return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static bool ReadBool(XElement element, string name, bool fallback) {
      var raw = (string)element.Attribute(name);
      return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private static OfferSort ReadSort(XElement element, string name, OfferSort fallback) {
      var raw = (string)element.Attribute(name);
      return Enum.TryParse<OfferSort>(raw, ignoreCase: true, out var value) ? value : fallback;
    }

    private static int[] ReadBandWeights(XElement element, string name, int[] fallback) {
      var raw = (string)element.Attribute(name);
      if (string.IsNullOrWhiteSpace(raw)) {
        return fallback;
      }

      var parts = raw.Split(',');
      var weights = new int[parts.Length];
      for (var i = 0; i < parts.Length; i++) {
        if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out weights[i])) {
          return fallback;
        }
      }

      return weights;
    }

    private int ClampInt(string what, int value, int min, int max, Action<string> warn) {
      if (value >= min && value <= max) {
        return value;
      }

      var clamped = value < min ? min : max;
      Report(warn, what + " was " + value + ", outside " + min + ".." + max + "; using " + clamped + ".");
      return clamped;
    }

    private float ClampFloat(string what, float value, float min, float max, Action<string> warn) {
      if (value >= min && value <= max) {
        return value;
      }

      var clamped = value < min ? min : max;
      Report(warn, what + " was " + value + ", outside " + min + ".." + max + "; using " + clamped + ".");
      return clamped;
    }

    private void Report(Action<string> warn, string message) {
      LoadedClean = false;
      warn?.Invoke(message);
    }
  }

  /// <summary>Case-insensitive <c>*</c> / <c>?</c> globbing, anchored at both ends.</summary>
  /// <remarks>
  /// Anchoring is what makes the shipped table read the way it looks: <c>*_clear</c> matches
  /// <c>tier3_clear</c> and not <c>tier3_clear_infested</c>.
  /// </remarks>
  public static class Glob {
    public static bool IsMatch(string text, string pattern) {
      if (text == null || pattern == null) {
        return false;
      }

      return IsMatch(text, 0, pattern, 0);
    }

    private static bool IsMatch(string text, int t, string pattern, int p) {
      while (p < pattern.Length) {
        var c = pattern[p];
        if (c == '*') {
          // Collapse runs of '*' so "a**b" costs no more than "a*b".
          while (p < pattern.Length && pattern[p] == '*') {
            p++;
          }

          if (p == pattern.Length) {
            return true;
          }

          for (var skip = t; skip <= text.Length; skip++) {
            if (IsMatch(text, skip, pattern, p)) {
              return true;
            }
          }

          return false;
        }

        if (t == text.Length) {
          return false;
        }

        if (c != '?' && char.ToLowerInvariant(c) != char.ToLowerInvariant(text[t])) {
          return false;
        }

        t++;
        p++;
      }

      return t == text.Length;
    }
  }
}

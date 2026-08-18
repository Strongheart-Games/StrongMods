using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.SortBoxes;

/// <summary>
///   What a keyword written on a box's sign does. The split matters architecturally: a trigger runs code when
///   something happens to its own container, while a property is a standing fact other features ask about.
///   StrongBoxes' current listener bag models only the first, and "nosort" or "mailbox" cannot be expressed
///   in it at all.
/// </summary>
public enum BoxKeywordKind {
  /// <summary>Acts on its own container when an event fires — <c>sort</c>.</summary>
  Trigger,

  /// <summary>A standing property another feature queries — <c>nosort</c>, <c>mailbox</c>.</summary>
  Property,
}

/// <summary>One keyword in the vocabulary: its word, what kind it is, and what it means.</summary>
public sealed record BoxKeyword(string Word, BoxKeywordKind Kind, string Summary) {
  /// <summary>An argument-taking keyword such as <c>sort:ammo</c> declares itself here.</summary>
  public bool TakesArgument { get; init; }
}

/// <summary>A keyword found on a sign, with whatever argument followed it.</summary>
public sealed record BoxDirective(string Word, string Argument, int Line) {
  public override string ToString() =>
    Argument == null ? $"{Word}@{Line}" : $"{Word}:{Argument}@{Line}";
}

/// <summary>
///   A prototype of the sign-text-as-command grammar for StrongBoxes, written to make the design choices in
///   the #31 research report testable. It is a PARSER plus a REGISTRY, deliberately separated from any
///   behaviour, because the parsing edge cases are where a name-driven scheme actually fails: null sign text,
///   markup a player pasted in, a box innocently labelled "sort", two features claiming one word.
///   <para>
///     The grammar it implements is the one the report recommends: a keyword is a whole line, matched
///     case-insensitively after trimming, with an optional <c>:argument</c>. Other lines are ignored, so a
///     player keeps the sign for its real purpose and adds a directive line to it. Compare
///     <see cref="ParseWholeSignExactly" />, which is what ServerTools and StrongBoxes do today.
///   </para>
///   Not shipping code, and not in StrongBoxes.
/// </summary>
public sealed class BoxKeywordVocabulary {
  private readonly Dictionary<string, BoxKeyword> keywords = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  ///   Registers a keyword, refusing a word another feature already claimed. The refusal is the point: with a
  ///   bag of independent predicates, two features can both answer to "sort" and neither knows.
  /// </summary>
  public void Register(BoxKeyword keyword) {
    if (keywords.TryGetValue(keyword.Word, out BoxKeyword existing)) {
      throw new InvalidOperationException(
        $"'{keyword.Word}' is already registered as a {existing.Kind} ({existing.Summary}). Two features " +
        "cannot claim one keyword — pick another word or extend the existing one.");
    }

    keywords[keyword.Word] = keyword;
  }

  /// <summary>Every registered word, which is what a help command or a chat hint would print.</summary>
  public IReadOnlyCollection<BoxKeyword> Registered => keywords.Values.ToList();

  public bool Knows(string word) => word != null && keywords.ContainsKey(word);

  /// <summary>
  ///   The recommended reading: every line of the sign is examined, and a line that is exactly a registered
  ///   keyword (after trimming, case-insensitively, with markup stripped) becomes a directive. Lines that are
  ///   not keywords are left alone, so the sign stays usable as a label.
  /// </summary>
  public IReadOnlyList<BoxDirective> Parse(string signText) {
    var found = new List<BoxDirective>();
    if (string.IsNullOrWhiteSpace(signText)) {
      return found;
    }

    string[] lines = signText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    for (var i = 0; i < lines.Length; i++) {
      var line = StripMarkup(lines[i]).Trim();
      if (line.Length == 0) {
        continue;
      }

      var colon = line.IndexOf(':');
      var word = colon < 0 ? line : line.Substring(0, colon).Trim();
      var argument = colon < 0 ? null : line.Substring(colon + 1).Trim();
      if (!keywords.TryGetValue(word, out BoxKeyword keyword)) {
        continue;
      }

      // A keyword that takes no argument must appear alone, so "sort: my junk" is a label, not a directive.
      if (argument != null && !keyword.TakesArgument) {
        continue;
      }

      found.Add(new BoxDirective(keyword.Word, argument, i));
    }

    return found;
  }

  /// <summary>
  ///   What ServerTools' <c>Sorter</c> and StrongBoxes' <c>IsSortBox</c> do today: lowercase the WHOLE sign
  ///   and compare it to the keyword. Kept here to make the difference measurable — this reading cannot see a
  ///   keyword on a labelled sign, and cannot carry two keywords at once.
  /// </summary>
  public BoxDirective ParseWholeSignExactly(string signText) {
    if (signText == null) {
      return null;
    }

    var text = signText.ToLowerInvariant();
    return keywords.TryGetValue(text, out BoxKeyword keyword) ? new BoxDirective(keyword.Word, null, 0) : null;
  }

  /// <summary>
  ///   Whether a container carries a given property keyword — the query "nosort" and "mailbox" need and that
  ///   a trigger-only listener bag cannot answer.
  /// </summary>
  public bool HasProperty(string signText, string word) =>
    Parse(signText).Any(d => string.Equals(d.Word, word, StringComparison.OrdinalIgnoreCase) &&
                             keywords[d.Word].Kind == BoxKeywordKind.Property);

  /// <summary>
  ///   Removes the game's sign markup so a coloured keyword still reads as one. 7 Days to Die signs carry
  ///   Unity rich-text tags, and a player who colours their sign has not stopped meaning the word.
  /// </summary>
  public static string StripMarkup(string line) {
    if (line == null) {
      return "";
    }

    var builder = new System.Text.StringBuilder(line.Length);
    var depth = 0;
    foreach (var c in line) {
      if (c == '[' || c == '<') {
        depth++;
      } else if (c == ']' || c == '>') {
        if (depth > 0) {
          depth--;
        }
      } else if (depth == 0) {
        builder.Append(c);
      }
    }

    return builder.ToString();
  }

  /// <summary>The vocabulary the report proposes, as a worked example.</summary>
  public static BoxKeywordVocabulary Proposed() {
    var vocabulary = new BoxKeywordVocabulary();
    vocabulary.Register(new BoxKeyword("sort", BoxKeywordKind.Trigger,
      "push contents into nearby containers that already hold the same item"));
    vocabulary.Register(new BoxKeyword("nosort", BoxKeywordKind.Property,
      "never receive items from a sort box"));
    vocabulary.Register(new BoxKeyword("mailbox", BoxKeywordKind.Property,
      "anyone may deposit; only the owner may withdraw"));
    vocabulary.Register(new BoxKeyword("sortonly", BoxKeywordKind.Property, "receive only the named items") {
      TakesArgument = true,
    });
    return vocabulary;
  }
}

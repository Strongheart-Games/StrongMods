using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Tests.SortBoxes;

/// <summary>
///   The parsing edge cases a sign-name keyword scheme has to survive, exercised against the prototype in
///   <see cref="BoxKeywordVocabulary" /> for the #31 research report. Sign text is player-authored, so every
///   case here is something a real player will eventually type.
/// </summary>
public sealed class BoxKeywordVocabularyTests {
  private readonly BoxKeywordVocabulary vocabulary = BoxKeywordVocabulary.Proposed();
  private readonly ITestOutputHelper output;

  public BoxKeywordVocabularyTests(ITestOutputHelper output) {
    this.output = output;
  }

  // -----------------------------------------------------------------------------------------------
  // Whole-sign matching, and what it costs
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   Whole-sign matching forces a player to choose between a directive and a label. The moment they write
  ///   anything else on the sign — which is the whole reason signs exist in a shared base — the keyword stops
  ///   working, silently.
  /// </summary>
  [Theory]
  [InlineData("sort")]
  [InlineData("SORT")]
  [InlineData("Sort")]
  public void Whole_sign_matching_sees_a_bare_keyword(string text) {
    Assert.NotNull(vocabulary.ParseWholeSignExactly(text));
  }

  [Theory]
  [InlineData("sort ")]
  [InlineData(" sort")]
  [InlineData("sort\nkitchen")]
  [InlineData("kitchen\nsort")]
  [InlineData("[ff0000]sort[-]")]
  public void Whole_sign_matching_misses_the_same_keyword_the_moment_anything_surrounds_it(string text) {
    Assert.Null(vocabulary.ParseWholeSignExactly(text));
    Assert.NotEmpty(vocabulary.Parse(text));
  }

  /// <summary>
  ///   Line matching keeps the sign usable. The label a base-mate reads and the directive the mod reads live
  ///   on the same sign.
  /// </summary>
  [Fact]
  public void Line_matching_lets_one_sign_carry_a_label_and_a_directive() {
    IReadOnlyList<BoxDirective> found = vocabulary.Parse("Kitchen overflow\nsort");

    output.WriteLine($"parsed: {string.Join(", ", found)}");

    BoxDirective directive = Assert.Single(found);
    Assert.Equal("sort", directive.Word);
    Assert.Equal(1, directive.Line);
  }

  // -----------------------------------------------------------------------------------------------
  // Player-authored text
  // -----------------------------------------------------------------------------------------------

  /// <summary>Sign text can be absent entirely. Every reader must survive it — StrongBoxes' does not today.</summary>
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\n\n")]
  public void Empty_or_absent_sign_text_yields_no_directives_and_does_not_throw(string text) {
    Assert.Empty(vocabulary.Parse(text));
  }

  /// <summary>Colour markup is decoration, not meaning. A player who colours a keyword still means it.</summary>
  [Theory]
  [InlineData("[ff0000]sort[-]", "sort")]
  [InlineData("<color=red>nosort</color>", "nosort")]
  [InlineData("[decor]mailbox", "mailbox")]
  public void Markup_around_a_keyword_is_stripped_before_matching(string text, string word) {
    Assert.Equal(word, Assert.Single(vocabulary.Parse(text)).Word);
  }

  [Theory]
  [InlineData("\r\n")]
  [InlineData("\n")]
  [InlineData("\r")]
  public void Every_line_ending_a_client_might_send_splits_the_same_way(string newline) {
    Assert.Single(vocabulary.Parse($"Storage{newline}sort"));
  }

  /// <summary>
  ///   The central hazard of name-driven behaviour: a box can be labelled with a keyword for an ordinary
  ///   reason. "sort" alone on a line is a directive under any scheme, but a sentence containing the word is
  ///   not — and a scheme that matched substrings would fire on all of these.
  /// </summary>
  [Theory]
  [InlineData("sort this out later")]
  [InlineData("needs sorting")]
  [InlineData("assorted junk")]
  [InlineData("resort supplies")]
  public void A_sentence_that_merely_contains_a_keyword_is_not_a_directive(string text) {
    Assert.Empty(vocabulary.Parse(text));
  }

  /// <summary>
  ///   A colon turns a bare keyword into a labelled line, so a player writing "sort: junk" as a note does not
  ///   trigger a keyword that takes no argument.
  /// </summary>
  [Fact]
  public void A_keyword_that_takes_no_argument_is_not_matched_when_one_is_given() {
    Assert.Empty(vocabulary.Parse("sort: whatever fits"));
    Assert.Equal("ammo", Assert.Single(vocabulary.Parse("sortonly: ammo")).Argument);
  }

  // -----------------------------------------------------------------------------------------------
  // The trigger / property split
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   "nosort" and "mailbox" are not events. They are facts another feature reads about a container it is
  ///   considering, which is why a registry of trigger callbacks cannot express them.
  /// </summary>
  [Fact]
  public void A_property_keyword_is_queryable_on_a_container_that_never_fires_an_event() {
    Assert.True(vocabulary.HasProperty("Ammo\nnosort", "nosort"));
    Assert.False(vocabulary.HasProperty("Ammo", "nosort"));
    // A trigger is not a property, even though both parse.
    Assert.False(vocabulary.HasProperty("sort", "sort"));
  }

  /// <summary>One sign can carry both a trigger and a property, and the parse keeps them distinct.</summary>
  [Fact]
  public void One_sign_can_carry_several_directives() {
    IReadOnlyList<BoxDirective> found = vocabulary.Parse("Workshop\nsort\nnosort");

    output.WriteLine($"parsed: {string.Join(", ", found)}");

    Assert.Equal(new[] { "sort", "nosort" }, found.Select(d => d.Word));
  }

  // -----------------------------------------------------------------------------------------------
  // The registry
  // -----------------------------------------------------------------------------------------------

  /// <summary>
  ///   Two features claiming one word is a bug the registry can catch at registration. StrongBoxes' current
  ///   <c>ConcurrentBag&lt;Listener&gt;</c> cannot: every listener is asked, and both would answer.
  /// </summary>
  [Fact]
  public void Registering_a_word_another_feature_already_claimed_is_refused() {
    var fresh = BoxKeywordVocabulary.Proposed();

    InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
      fresh.Register(new BoxKeyword("sort", BoxKeywordKind.Property, "something else entirely")));

    output.WriteLine(error.Message);
    Assert.Contains("already registered", error.Message);
  }

  /// <summary>
  ///   The vocabulary can enumerate itself, which is what makes a help command or an in-game hint possible.
  ///   A bag of opaque predicates cannot be printed.
  /// </summary>
  [Fact]
  public void The_vocabulary_can_list_itself_for_help_text() {
    foreach (BoxKeyword keyword in vocabulary.Registered.OrderBy(k => k.Word)) {
      output.WriteLine($"  {keyword.Word,-10} {keyword.Kind,-8} {keyword.Summary}");
    }

    Assert.Equal(4, vocabulary.Registered.Count);
    Assert.Contains(vocabulary.Registered, k => k.Kind == BoxKeywordKind.Property);
  }

  /// <summary>
  ///   ServerTools' sorter excludes targets by comparing their sign against a hardcoded list of other
  ///   features' box names. A registry replaces that with a question — "does this container carry the nosort
  ///   property?" — which stays correct as keywords are added.
  /// </summary>
  [Fact]
  public void Exclusion_by_property_does_not_need_a_hardcoded_list_of_other_features_names() {
    var excluded = new[] { "Ammo\nnosort", "nosort", "[ff0000]nosort[-]" };
    var included = new[] { "Ammo", "Ammo\nno sort", "nosorting" };

    Assert.All(excluded, t => Assert.True(vocabulary.HasProperty(t, "nosort"), t));
    Assert.All(included, t => Assert.False(vocabulary.HasProperty(t, "nosort"), t));
  }
}

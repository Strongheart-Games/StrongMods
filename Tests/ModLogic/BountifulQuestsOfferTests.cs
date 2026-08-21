using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>Game-free selection rules used while BountifulQuests fills one trader offer page.</summary>
[Collection(ModLogicCollection.Name)]
public sealed class BountifulQuestsOfferTests {
  private readonly Type configType;
  private readonly Type drawType;
  private readonly Type ruleType;

  public BountifulQuestsOfferTests() {
    ModLogicHost host = ModLogicHost.For("BountifulQuests");
    configType = host.ModType("BountifulQuests.OfferConfig");
    drawType = host.ModType("BountifulQuests.OfferDraw");
    ruleType = host.ModType("BountifulQuests.QuestTypeRule");
  }

  [Fact]
  public void Parse_clamps_contradictory_settings_and_reports_each_correction() {
    var warnings = new List<string>();
    object config = ModLogicHost.CallStatic(configType, "Parse", XDocument.Parse(
      "<BountifulQuests><offers per_tier=\"1\" min_tier=\"4\" max_tier=\"2\" />" +
      "<distance min=\"200\" max=\"100\" near=\"900\" mid=\"500\" band_weights=\"1,2\" />" +
      "</BountifulQuests>"), (Action<string>)warnings.Add);

    Assert.Equal(4, Field<int>(config, "OffersPerTier"));
    Assert.Equal(4, Field<int>(config, "MaxTier"));
    Assert.Equal(0f, Field<float>(config, "MinDistance"));
    Assert.Equal(0f, Field<float>(config, "MaxDistance"));
    Assert.Equal(new[] { 3, 1, 3 }, Field<int[]>(config, "BandWeights"));
    Assert.False(Field<bool>(config, "LoadedClean"));
    Assert.NotEmpty(warnings);
  }

  [Fact]
  public void The_longest_matching_rule_wins_regardless_of_rule_order() {
    object config = Config();
    AddRule(config, "*_clear", weight: 2, max: 0);
    AddRule(config, "tier3_clear", weight: 7, max: 0);

    Assert.Equal(7, (int)ModLogicHost.Call(config, "WeightFor", "tier3_clear"));
    Assert.Equal(2, (int)ModLogicHost.Call(config, "WeightFor", "tier2_clear"));
    Assert.Equal(1, (int)ModLogicHost.Call(config, "WeightFor", "tier3_fetch"));
  }

  [Fact]
  public void Distance_band_schedule_repeats_the_configured_weights() {
    object config = Config();
    ModLogicHost.SetInstance(config, "BandWeights", new[] { 2, 1, 1 });

    var bands = new List<int>();
    for (var slot = 0; slot < 8; slot++) {
      bands.Add((int)ModLogicHost.Call(config, "BandForSlot", slot));
    }

    Assert.Equal(new[] { 0, 0, 1, 2, 0, 0, 1, 2 }, bands);
  }

  [Fact]
  public void Weighted_draw_uses_the_configured_boundaries() {
    object config = Config();
    AddRule(config, "clear", weight: 3, max: 0);
    AddRule(config, "fetch", weight: 1, max: 0);
    object draw = Draw(config, "clear", "fetch");

    Assert.Equal(0, (int)ModLogicHost.Call(draw, "Draw", 0d));
    Assert.Equal(0, (int)ModLogicHost.Call(draw, "Draw", 0.749d));
    Assert.Equal(1, (int)ModLogicHost.Call(draw, "Draw", 0.75d));
  }

  [Fact]
  public void A_type_cap_excludes_the_rule_after_its_first_accepted_offer() {
    object config = Config();
    AddRule(config, "clear", weight: 1, max: 1);
    object draw = Draw(config, "clear", "fetch");

    ModLogicHost.Call(draw, "Accept", 0);

    Assert.Equal(1, (int)ModLogicHost.Call(draw, "Draw", 0d));
  }

  [Fact]
  public void A_removed_single_quest_is_not_drawn_again() {
    object draw = Draw(Config(), "first", "second");

    ModLogicHost.Call(draw, "Remove", 0);

    Assert.Equal(1, (int)ModLogicHost.Call(draw, "Draw", 0d));
  }

  [Fact]
  public void A_rejected_candidate_stays_eligible_and_does_not_fill_a_page_slot() {
    object draw = Draw(Config(), "first");

    ModLogicHost.Call(draw, "Reject", 0);

    Assert.Equal(0, (int)ModLogicHost.Call(draw, "Draw", 0d));
    Assert.Equal(0, Property<int>(draw, "Accepted"));
  }

  [Fact]
  public void Repeat_avoidance_prefers_unused_candidates_then_falls_back_to_fill_the_page() {
    object config = Config();
    ModLogicHost.SetInstance(config, "AvoidRepeatOnPage", true);
    object draw = Draw(config, "first", "second");

    ModLogicHost.Call(draw, "Accept", 0);
    Assert.Equal(1, (int)ModLogicHost.Call(draw, "Draw", 0d));
    ModLogicHost.Call(draw, "Accept", 1);

    Assert.Equal(0, (int)ModLogicHost.Call(draw, "Draw", 0d));
  }

  [Fact]
  public void No_eligible_candidate_ends_page_fill() {
    object config = Config();
    ModLogicHost.SetInstance(config, "DefaultWeight", 0);

    Assert.Equal(-1, (int)ModLogicHost.Call(Draw(config, "unweighted"), "Draw", 0d));
  }

  private object Config() => Activator.CreateInstance(configType)!;

  private object Draw(object config, params string[] questIds) =>
    Activator.CreateInstance(drawType, config, questIds)!;

  private void AddRule(object config, string match, int weight, int max) {
    object rule = Activator.CreateInstance(ruleType)!;
    ModLogicHost.SetInstance(rule, "Match", match);
    ModLogicHost.SetInstance(rule, "Weight", weight);
    ModLogicHost.SetInstance(rule, "Max", max);
    object rules = configType.GetField("TypeRules")!.GetValue(config)!;
    ModLogicHost.Call(rules, "Add", rule);
  }

  private static T Field<T>(object target, string name) =>
    (T)target.GetType().GetField(name)!.GetValue(target)!;

  private static T Property<T>(object target, string name) =>
    (T)target.GetType().GetProperty(name)!.GetValue(target)!;
}

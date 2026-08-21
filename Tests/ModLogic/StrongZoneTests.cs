using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongUtils' <c>StrongZone</c> — the axis-aligned XZ box that every zone feature (buffs, no-hostiles,
///   no-claims, StrongSworn-only) is expressed in — and the zone <em>diff</em> that decides which enter/leave
///   callbacks fire as an entity moves.
///   <para>
///     Both are pure geometry over <c>Vector2i</c> and <c>Vector3</c>, and neither logs, so they run for real
///     in the real-Unity universe (the stub cannot supply <c>Vector3</c>). Nothing here needs a world.
///   </para>
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class StrongZoneTests {
  private readonly ModLogicHost host;
  private readonly Type zoneType;
  private readonly Type zonesType;
  private readonly Type vector2i;
  private readonly Type vector3;

  public StrongZoneTests() {
    host = ModLogicHost.For("StrongUtils", false);
    zoneType = host.ModType("StrongUtils.StrongZone");
    zonesType = host.ModType("StrongUtils.StrongZones");
    vector2i = host.GameType("Vector2i");
    vector3 = host.TypeFrom("UnityEngine.CoreModule", "UnityEngine.Vector3");
  }

  // The box
  // ---------------------------------------------------------------------------------------------

  /// <summary>Either corner may be given first — the zone sorts them, so all four orderings are one box.</summary>
  [Theory]
  [InlineData(0, 0, 10, 20)]
  [InlineData(10, 20, 0, 0)]
  [InlineData(0, 20, 10, 0)]
  [InlineData(10, 0, 0, 20)]
  public void Corners_are_normalized_whichever_order_they_arrive_in(int ax, int az, int bx, int bz) {
    object zone = Zone("z", ax, az, bx, bz);

    Assert.Equal(0, Field(zone, "MinX"));
    Assert.Equal(0, Field(zone, "MinZ"));
    Assert.Equal(10, Field(zone, "MaxX"));
    Assert.Equal(20, Field(zone, "MaxZ"));
  }

  [Fact]
  public void Negative_coordinates_normalize_the_same_way() {
    object zone = Zone("z", 5, 5, -15, -25);

    Assert.Equal(-15, Field(zone, "MinX"));
    Assert.Equal(-25, Field(zone, "MinZ"));
    Assert.Equal(5, Field(zone, "MaxX"));
    Assert.Equal(5, Field(zone, "MaxZ"));
  }

  [Theory]
  [InlineData(0, 0, 10, 20, 5f, 10f)]
  [InlineData(0, 0, 11, 0, 5.5f, 0f)]
  [InlineData(-10, -10, 10, 10, 0f, 0f)]
  public void Center_is_the_midpoint_of_the_box(int ax, int az, int bx, int bz, float cx, float cz) {
    object center = ModLogicHost.Call(Zone("z", ax, az, bx, bz), "get_Center");

    Assert.Equal(cx, (float)vector3.GetField("x")!.GetValue(center)!, 4);
    Assert.Equal(cz, (float)vector3.GetField("z")!.GetValue(center)!, 4);
  }

  /// <summary>The radius is the circumscribing one: center to corner, not the inscribed half-width.</summary>
  [Fact]
  public void Radius_reaches_the_corner() {
    var radius = (float)ModLogicHost.Call(Zone("z", 0, 0, 6, 8), "get_Radius");

    Assert.Equal(5f, radius, 4);
  }

  [Fact]
  public void A_zero_size_zone_is_a_single_point() {
    object zone = Zone("z", 7, 9, 7, 9);

    Assert.Equal(0f, (float)ModLogicHost.Call(zone, "get_Radius"), 4);
    Assert.True(Contains(zone, 7, 9));
    Assert.False(Contains(zone, 8, 9));
  }

  /// <summary>Containment is inclusive on every edge — a block exactly on the boundary is inside.</summary>
  [Theory]
  [InlineData(0, 0, true)]
  [InlineData(10, 20, true)]
  [InlineData(0, 20, true)]
  [InlineData(10, 0, true)]
  [InlineData(5, 10, true)]
  [InlineData(-1, 10, false)]
  [InlineData(11, 10, false)]
  [InlineData(5, -1, false)]
  [InlineData(5, 21, false)]
  public void Containment_is_inclusive_on_the_edges(int x, int z, bool inside) {
    Assert.Equal(inside, Contains(Zone("z", 0, 0, 10, 20), x, z));
  }

  /// <summary>A zone is a column: height is not part of the test, by design.</summary>
  [Fact]
  public void Containment_ignores_height() {
    object zone = Zone("z", 0, 0, 10, 10);

    Assert.True(Contains(zone, 5, 5, -100f));
    Assert.True(Contains(zone, 5, 5, 250f));
  }

  // ---------------------------------------------------------------------------------------------
  // Tags
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void A_zone_with_no_tags_has_every_flag_off() {
    object zone = Zone("z", 0, 0, 1, 1);

    Assert.Empty((IEnumerable)zoneType.GetField("Tags")!.GetValue(zone)!);
    foreach (var flag in new[] { "Buff", "NoReset", "NoHostiles", "NoClaims", "StrongSwornOnly" }) {
      Assert.False((bool)ModLogicHost.Call(zone, "get_" + flag), flag);
    }
  }

  [Theory]
  [InlineData("buff", "Buff")]
  [InlineData("no_reset", "NoReset")]
  [InlineData("no_hostiles", "NoHostiles")]
  [InlineData("no_claims", "NoClaims")]
  [InlineData("strongsworn_only", "StrongSwornOnly")]
  public void Each_known_tag_raises_exactly_its_own_flag(string tag, string flag) {
    object zone = Zone("z", 0, 0, 1, 1, tag);

    foreach (var candidate in new[] { "Buff", "NoReset", "NoHostiles", "NoClaims", "StrongSwornOnly" }) {
      Assert.Equal(candidate == flag, (bool)ModLogicHost.Call(zone, "get_" + candidate));
    }
  }

  /// <summary>Tag matching is exact and case-sensitive — "No_Claims" is not "no_claims".</summary>
  [Fact]
  public void Tag_matching_is_case_sensitive() {
    Assert.False((bool)ModLogicHost.Call(Zone("z", 0, 0, 1, 1, "No_Claims"), "get_NoClaims"));
  }

  [Fact]
  public void An_unknown_tag_raises_no_flag_and_is_still_kept() {
    object zone = Zone("z", 0, 0, 1, 1, "something_else");

    Assert.False((bool)ModLogicHost.Call(zone, "get_Buff"));
    Assert.Equal(new[] { "something_else" }, Tags(zone));
  }

  // ---------------------------------------------------------------------------------------------
  // XML
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void A_well_formed_zone_element_parses() {
    object zone = FromXml("<zone name=\"town\" cornerXZ=\"-10,-20\" oppositeCornerXZ=\"30,40\" />");

    Assert.Equal("town", zoneType.GetField("Name")!.GetValue(zone));
    Assert.Equal(-10, Field(zone, "MinX"));
    Assert.Equal(-20, Field(zone, "MinZ"));
    Assert.Equal(30, Field(zone, "MaxX"));
    Assert.Equal(40, Field(zone, "MaxZ"));
  }

  [Fact]
  public void Tags_come_from_a_comma_separated_child_element_and_are_trimmed() {
    object zone = FromXml("<zone name=\"t\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\">" +
                          "<tags> buff , no_claims ,, no_reset </tags></zone>");

    Assert.Equal(new[] { "buff", "no_claims", "no_reset" }, Tags(zone));
    Assert.True((bool)ModLogicHost.Call(zone, "get_Buff"));
  }

  [Theory]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\" />")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\"><tags></tags></zone>")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\"><tags>   </tags></zone>")]
  public void A_missing_or_blank_tags_element_yields_no_tags(string xml) {
    Assert.Empty(Tags(FromXml(xml)));
  }

  [Theory]
  [InlineData("<zone cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\" />", "name")]
  [InlineData("<zone name=\"\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1,1\" />", "name")]
  [InlineData("<zone name=\"t\" oppositeCornerXZ=\"1,1\" />", "cornerXZ")]
  [InlineData("<zone name=\"t\" cornerXZ=\"\" oppositeCornerXZ=\"1,1\" />", "cornerXZ")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0\" oppositeCornerXZ=\"1,1\" />", "cornerXZ")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0,0\" oppositeCornerXZ=\"1,1\" />", "cornerXZ")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0\" />", "oppositeCornerXZ")]
  [InlineData("<zone name=\"t\" cornerXZ=\"0,0\" oppositeCornerXZ=\"1\" />", "oppositeCornerXZ")]
  public void A_malformed_zone_element_is_rejected_by_name(string xml, string mentions) {
    ArgumentException error = Assert.Throws<ArgumentException>(() => FromXml(xml));

    Assert.Contains(mentions, error.Message);
  }

  [Fact]
  public void Writing_a_zone_back_out_uses_the_normalized_corners() {
    object zone = Zone("town", 30, 40, -10, -20, "buff", "no_reset");

    var xml = (XElement)ModLogicHost.Call(zone, "ToXml");

    Assert.Equal("zone", xml.Name.LocalName);
    Assert.Equal("town", xml.Attribute("name")!.Value);
    Assert.Equal("-10,-20", xml.Attribute("cornerXZ")!.Value);
    Assert.Equal("30,40", xml.Attribute("oppositeCornerXZ")!.Value);
    Assert.Equal("buff,no_reset", xml.Element("tags")!.Value);
  }

  [Fact]
  public void A_zone_with_no_tags_writes_no_tags_element() {
    Assert.Null(((XElement)ModLogicHost.Call(Zone("t", 0, 0, 1, 1), "ToXml")).Element("tags"));
  }

  [Fact]
  public void Name_corners_and_tags_survive_a_write_then_read() {
    object original = Zone("town", -10, -20, 30, 40, "buff", "no_claims");

    object round = ModLogicHost.CallStatic(zoneType, "FromXml", ModLogicHost.Call(original, "ToXml"));

    Assert.Equal(zoneType.GetField("Name")!.GetValue(original), zoneType.GetField("Name")!.GetValue(round));
    foreach (var edge in new[] { "MinX", "MinZ", "MaxX", "MaxZ" }) {
      Assert.Equal(Field(original, edge), Field(round, edge));
    }

    Assert.Equal(Tags(original), Tags(round));
  }

  // ---------------------------------------------------------------------------------------------
  // The diff that drives enter/leave callbacks
  // ---------------------------------------------------------------------------------------------

  [Fact]
  public void A_zone_newly_containing_the_position_is_added() {
    object zone = Zone("z", 0, 0, 10, 10);

    (List<object> added, List<object> removed) = Diff(null, new[] { zone }, 5, 5);

    Assert.Same(zone, Assert.Single(added));
    Assert.Null(removed);
  }

  [Fact]
  public void A_zone_already_held_is_neither_added_nor_removed() {
    object zone = Zone("z", 0, 0, 10, 10);

    (List<object> added, List<object> removed) = Diff(new[] { zone }, new[] { zone }, 5, 5);

    Assert.Null(added);
    Assert.Null(removed);
  }

  [Fact]
  public void A_zone_no_longer_offered_is_removed() {
    object zone = Zone("z", 0, 0, 10, 10);

    (List<object> added, List<object> removed) = Diff(new[] { zone }, null, 5, 5);

    Assert.Null(added);
    Assert.Same(zone, Assert.Single(removed));
  }

  /// <summary>
  ///   Candidacy is per-chunk, so the new list holds zones the entity is not actually standing in. Only the
  ///   containing ones count — which is what makes a walk out of a zone register as a leave.
  /// </summary>
  [Fact]
  public void A_candidate_that_does_not_contain_the_position_is_not_added() {
    object elsewhere = Zone("far", 100, 100, 110, 110);

    (List<object> added, List<object> removed) = Diff(null, new[] { elsewhere }, 5, 5);

    Assert.Null(added);
    Assert.Null(removed);
  }

  [Fact]
  public void A_held_zone_still_offered_but_no_longer_containing_the_position_is_removed() {
    object zone = Zone("z", 0, 0, 10, 10);

    (List<object> added, List<object> removed) = Diff(new[] { zone }, new[] { zone }, 50, 50);

    Assert.Null(added);
    Assert.Same(zone, Assert.Single(removed));
  }

  [Fact]
  public void Overlapping_zones_are_each_reported() {
    object first = Zone("a", 0, 0, 10, 10);
    object second = Zone("b", 5, 5, 15, 15);

    (List<object> added, List<object> removed) = Diff(null, new[] { first, second }, 7, 7);

    Assert.Equal(2, added.Count);
    Assert.Null(removed);
  }

  [Fact]
  public void One_zone_can_be_added_while_another_is_removed() {
    object left = Zone("left", 0, 0, 10, 10);
    object right = Zone("right", 20, 20, 30, 30);

    (List<object> added, List<object> removed) = Diff(new[] { left }, new[] { left, right }, 25, 25);

    Assert.Same(right, Assert.Single(added));
    Assert.Same(left, Assert.Single(removed));
  }

  [Fact]
  public void Two_empty_sides_report_nothing() {
    (List<object> added, List<object> removed) = Diff(null, null, 0, 0);

    Assert.Null(added);
    Assert.Null(removed);
  }

  // ---------------------------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------------------------

  private object Zone(string name, int ax, int az, int bx, int bz, params string[] tags) =>
    Activator.CreateInstance(zoneType, name, Corner(ax, az), Corner(bx, bz),
      tags.Length == 0 ? null : new List<string>(tags), null);

  private object Corner(int x, int z) => Activator.CreateInstance(vector2i, x, z);

  private object Position(float x, float z, float y = -1f) => Activator.CreateInstance(vector3, x, y, z);

  private bool Contains(object zone, float x, float z, float y = -1f) =>
    (bool)ModLogicHost.Call(zone, "Contains", Position(x, z, y));

  private object FromXml(string xml) =>
    ModLogicHost.CallStatic(zoneType, "FromXml", XElement.Parse(xml));

  private static int Field(object zone, string name) => (int)zone.GetType().GetField(name)!.GetValue(zone)!;

  private static string[] Tags(object zone) =>
    ((IEnumerable)zone.GetType().GetField("Tags")!.GetValue(zone)!).Cast<string>().ToArray();

  /// <summary>
  ///   Calls the private <c>StrongZones.FindZoneChanges</c>, whose two results are <c>out</c> parameters.
  ///   The lists it builds are <c>List&lt;StrongZone&gt;</c> in the mod's own context, so they come back as
  ///   plain object sequences.
  /// </summary>
  private (List<object> Added, List<object> Removed) Diff(object[] oldZones, object[] newZones, float x, float z) {
    MethodInfo method = zonesType.GetMethod("FindZoneChanges", BindingFlags.NonPublic | BindingFlags.Static)!;
    var args = new[] { ZoneList(oldZones), ZoneList(newZones), Position(x, z), null, null };
    method.Invoke(null, args);
    return (Materialize(args[3]), Materialize(args[4]));
  }

  private object ZoneList(object[] zones) {
    if (zones == null) {
      return null;
    }

    object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(zoneType));
    foreach (object zone in zones) {
      ModLogicHost.Call(list, "Add", zone);
    }

    return list;
  }

  private static List<object> Materialize(object list) =>
    list == null ? null : ((IEnumerable)list).Cast<object>().ToList();
}

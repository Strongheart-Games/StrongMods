using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

[Collection(ModLogicCollection.Name)]
public sealed class StrongZonesReloadTests {
  private readonly Type zoneType;
  private readonly Type zonesType;
  private readonly Type vector2i;

  public StrongZonesReloadTests() {
    ModLogicHost host = ModLogicHost.For("StrongUtils");
    zoneType = host.ModType("StrongUtils.StrongZone");
    zonesType = host.ModType("StrongUtils.StrongZones");
    vector2i = host.GameType("Vector2i");
  }

  [Fact]
  public void Reloading_custom_zones_keeps_the_prefab_zones() {
    object prefabZone = Zone("prefab", 0, 0, 10, 10);
    SetZones(new[] { prefabZone }, new[] { Zone("old-custom", 20, 20, 30, 30) });

    MethodInfo reload = zonesType.GetMethod("UpdateCustomZones", BindingFlags.NonPublic | BindingFlags.Static)!;
    reload.Invoke(null, new object[] { XElement.Parse("<config><zone name=\"custom\" cornerXZ=\"40,40\" " +
                                                       "oppositeCornerXZ=\"50,50\" /></config>") });

    object reloaded = zonesType.GetField("s_zones", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    Assert.Same(prefabZone, Assert.Single(Zones(reloaded, "_prefabZones")));
    Assert.Equal("custom", zoneType.GetField("Name")!.GetValue(Assert.Single(Zones(reloaded, "_customZones"))));
  }

  private object Zone(string name, int ax, int az, int bx, int bz) =>
    Activator.CreateInstance(zoneType, name, Corner(ax, az), Corner(bx, bz), null, null)!;

  private object Corner(int x, int z) => Activator.CreateInstance(vector2i, x, z)!;

  private object ZoneList(IEnumerable<object> zones) {
    object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(zoneType))!;
    foreach (object zone in zones) {
      ModLogicHost.Call(list, "Add", zone);
    }

    return list;
  }

  private void SetZones(IEnumerable<object> prefabZones, IEnumerable<object> customZones) {
    ConstructorInfo constructor = zonesType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
      new[] { typeof(List<>).MakeGenericType(zoneType), typeof(List<>).MakeGenericType(zoneType) }, null)!;
    object zones = constructor.Invoke(new[] { ZoneList(prefabZones), ZoneList(customZones) });
    zonesType.GetField("s_zones", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, zones);
  }

  private static List<object> Zones(object zones, string field) =>
    ((IEnumerable)zones.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(zones)!)
    .Cast<object>().ToList();
}

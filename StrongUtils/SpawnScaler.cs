using System.Collections.Generic;
using UniLinq;

namespace StrongUtils {
  public class SpawnScaler {
    private static readonly HashSet<string> s_groupsToScale = new() {
      "ZombiesWastelandNight",
      "ZombiesAllWasteland",
      "ZombiesWastelandDowntown"
    };

    private static readonly Dictionary<string, double> s_entityScalesByName = new() {
      { "zombieMutated", .25 },
      { "zombieMutatedFeral", .25 }
    };

    private static readonly Dictionary<int, float> s_entityScales;

    static SpawnScaler() {
      s_entityScales = s_entityScalesByName.ToDictionary(e => EntityClass.FromString(e.Key), e => (float)e.Value);
    }

    public static void ScaleEntityGroup(string entityGroupName, ref float totalp) {
      if (!s_groupsToScale.Contains(entityGroupName)) {
        return;
      }

      List<SEntityClassAndProb> group = EntityGroups.list[entityGroupName];
      for (var i = 0; i < group.Count; ++i) {
        SEntityClassAndProb entity = group[i];
        if (!s_entityScales.TryGetValue(entity.entityClassId, out var scale)) {
          continue;
        }

        var original = entity.prob;
        var scaled = original * scale;
        entity.prob = scaled;
        totalp += scaled - original;
        group[i] = entity;
      }

      Log.Out(
        $"[StrongUtils] Scaled {entityGroupName}:\n    {string.Join(",\n    ", group.Select(e => $"{EntityClass.GetEntityClassName(e.entityClassId)}, {e.prob}"))}");
    }
  }
}

using System.Collections;
using System.Collections.Generic;
using UniLinq;

namespace StrongUtils.Commands {
  public class ResetPoisCommand : ConsoleCmdAbstract {
    private const string CommandName = "resetpois";

    public override string getDescription() {
      return "Resets all instances of a named POI in the world, forcing them to respawn with fresh loot and zombies.";
    }

    public override string getHelp() {
      return string.Join("\n", $"Usage: {CommandName} <poi_name>", "", "Arguments:",
        "  poi_name  The exact prefab name of the POI to reset (case-insensitive).",
        "            Use 'listprefabs' to find valid prefab names.", "", "Examples:",
        $"  {CommandName} remnant_house_01", $"  {CommandName} house_modern_07", "", "Notes:",
        "  - Resets ALL instances of the named POI across the entire map.",
        "  - Players inside an affected POI may experience a reset mid-visit.");
    }

    public override string[] getCommands() {
      return new[] { CommandName };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      GameManager.Instance.StartCoroutine(ExecuteCo(@params, senderInfo));
    }

    private IEnumerator ExecuteCo(List<string> @params, CommandSenderInfo senderInfo) {
      if (@params == null || @params.Count == 0 || string.IsNullOrWhiteSpace(@params[0])) {
        SdtdConsole.Instance.Output($"[{CommandName}] Error: POI name is required.");
        SdtdConsole.Instance.Output(getHelp());
        yield break;
      }

      var name = @params[0].Trim();

      if (GameManager.Instance is null) {
        SdtdConsole.Instance.Output($"[{CommandName}] Error: GameManager is not available.");
        yield break;
      }

      DynamicPrefabDecorator decorator = GameManager.Instance.GetDynamicPrefabDecorator();
      if (decorator == null) {
        SdtdConsole.Instance.Output($"[{CommandName}] Error: Could not retrieve DynamicPrefabDecorator.");
        yield break;
      }

      if (decorator.allPrefabs == null) {
        SdtdConsole.Instance.Output($"[{CommandName}] Error: Prefab list is not available.");
        yield break;
      }

      var prefabs = decorator.allPrefabs
        .Where(p => p?.prefab?.PrefabName != null && p.prefab.PrefabName.EqualsCaseInsensitive(name))
        .ToList();

      if (prefabs.Count == 0) {
        SdtdConsole.Instance.Output($"[{CommandName}] No POI instances found with name '{name}'.");
        SdtdConsole.Instance.Output(
          $"[{CommandName}] Tip: POI names are prefab names (e.g. 'remnant_house_01'). Use 'listprefabs' to browse available names.");
        yield break;
      }

      World world = GameManager.Instance.World;
      if (world == null) {
        SdtdConsole.Instance.Output($"[{CommandName}] Error: World is not available.");
        yield break;
      }

      yield return world.ResetPOIS(prefabs, QuestEventManager.manualResetTag, -1, null, null);
      SdtdConsole.Instance.Output($"[{CommandName}] Reset {prefabs.Count} instance(s) of '{name}'.");

      foreach (PrefabInstance p in prefabs) {
        foreach (var c in p.GetOccupiedChunks()) {
          DynamicMeshManager.Instance.AddChunk(c, true, true, null);
        }
      }
    }
  }
}

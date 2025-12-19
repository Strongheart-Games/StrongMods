using System;
using System.Collections.Generic;

namespace StrongUtils.Commands {
  public class TeleportPlayerToPrefabCommand : ConsoleCmdAbstract {
    private const string Usage = @"Usage: teleportplayertoprefab <player> <prefab>";

    public override string getDescription() {
      return "Teleports a player to a prefab.";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "teleportplayertoprefab" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo)
    {
      if (@params.Count != 2)
      {
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Error: Usage is <player> <prefab>");
        return;
      }

      var player = @params[0];
      var prefab = @params[1];

      if (TryGetNearestPrefabByName(prefab, out var coordinates))
      {
        SdtdConsole.Instance.ExecuteSync($"teleportplayer {player} {coordinates}", null);
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Teleporting {player} to nearest '{prefab}' at {coordinates}.");
      }
      else
      {
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Error: Could not find a prefab named '{prefab}'.");
      }
    }

    private static bool TryGetNearestPrefabByName(string name, out string coordinates) {
      coordinates = null;
      List<PrefabInstance> allPrefabs = GameManager.Instance.World.ChunkCache.ChunkProvider.GetDynamicPrefabDecorator()?.allPrefabs;
      if (allPrefabs is null) {
        return false;
      }
      List<PrefabInstance> matchingPrefabs = allPrefabs.FindAll(p => p.prefab.PrefabName.EqualsCaseInsensitive(name));
      if (matchingPrefabs.Count == 0) {
        return false;
      }
      // TODO: Pick nearest prefab
      PrefabInstance prefab = matchingPrefabs[0];
      coordinates = $"{prefab.boundingBoxPosition.x} -1 {prefab.boundingBoxPosition.z}";
      return true;
    }

  }
}

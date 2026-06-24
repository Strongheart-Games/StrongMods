using System.Collections.Generic;

namespace StrongUtils.Commands {
  public class TeleportPlayerToBed : ConsoleCmdAbstract {
    private const string Usage = @"Usage: teleportplayertobed <player>";

    public override string getDescription() {
      return "Teleports a player to their bed, if set.";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "teleportplayertobed" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo sender) {
      if (@params.Count != 1) {
        SdtdConsole.Instance.Output("Error: Usage is <player>");
        return;
      }

      var playerIdOrName = @params[0];

      PersistentPlayerData playerData = null;
      ClientInfo client = ConsoleHelper.ParseParamIdOrName(playerIdOrName);
      if (client is null) {
        if (!GameManager.IsDedicatedServer && ConsoleHelper.ParamIsLocalPlayer(playerIdOrName)){
          playerData = GameManager.Instance.GetPersistentLocalPlayer();
        }
      } else {
        playerData = GameManager.Instance.getPersistentPlayerData(client);
      }

      if (playerData is null) {
        SdtdConsole.Instance.Output($"Player name or entity/userid not found: {playerIdOrName}");
        return;
      }

      if (!playerData.HasBedrollPos) {
        SdtdConsole.Instance.Output($"Player has no bedroll: {playerIdOrName}");
        return;
      }

      SdtdConsole.Instance.ExecuteSync(
        $"teleportplayer {playerIdOrName} {playerData.BedrollPos.x} {playerData.BedrollPos.y} {playerData.BedrollPos.z}", null);
    }
  }
}

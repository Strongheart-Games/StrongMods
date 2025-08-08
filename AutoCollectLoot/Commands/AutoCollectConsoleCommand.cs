using System;
using System.Collections.Generic;

namespace AutoCollectLoot.Commands {
  public class AutoCollectConsoleCommand : ConsoleCmdAbstract {
    private const string Usage = @"

Usage:

  1. autocollect
  2. autocollect enable
  3. autocollect disable
  4. autocollect enableoutsidebloodmoon
  5. autocollect disableoutsidebloodmoon

";

    public override string getDescription() {
      return "AutoCollectLoot administration";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "autocollect" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      try {
        if (@params.Count < 1) {
          SdtdConsole.Instance.Output(
            $"AutoCollectLoot current state:\n    Enabled: {AutoCollectLoot.Enabled}\n    EnabledOutsideBloodMoon: {AutoCollectLoot.EnabledOutsideBloodMoon}\n    LootItemNameProperty: {AutoCollectLoot.LootItemNameProperty}");
          return;
        }

        var command = @params[0];
        switch (command) {
          case "enable":
            AutoCollectLoot.Enabled = true;
            break;
          case "disable":
            AutoCollectLoot.Enabled = false;
            break;
          case "enableoutsidebloodmoon":
            AutoCollectLoot.EnabledOutsideBloodMoon = true;
            break;
          case "disableoutsidebloodmoon":
            AutoCollectLoot.EnabledOutsideBloodMoon = false;
            break;
          default:
            SdtdConsole.Instance.Output("Unknown command: " + command);
            break;
        }
      } catch (Exception e) {
        Log.Error("Error in AutoCollectConsoleCommand.Execute: " + e.Message);
      }
    }
  }
}

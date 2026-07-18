using System;
using System.Collections.Generic;

namespace AutoCollectLoot.Commands {
  public class AutoLootConsoleCommand : ConsoleCmdAbstract {
    private const string Usage = @"

Usage:

  1. autoloot
  2. autoloot enable
  3. autoloot disable
  4. autoloot enableoutsidebloodmoon
  5. autoloot disableoutsidebloodmoon

";

    public override string getDescription() {
      return "AutoCollectLoot administration";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "autoloot" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      try {
        if (@params.Count < 1) {
          SdtdConsole.Instance.Output(
            $"AutoCollectLoot current state:\n    Enabled: {AutoCollectLoot.Enabled}\n    EnabledOutsideBloodMoon: {AutoCollectLoot.EnabledOutsideBloodMoon}");
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
        Log.Error("Error in AutoLootConsoleCommand.Execute: " + e.Message);
      }
    }
  }
}

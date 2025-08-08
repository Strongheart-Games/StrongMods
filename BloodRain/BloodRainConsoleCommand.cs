using System;
using System.Collections.Generic;

namespace BloodRain {
  public class BloodRainConsoleCommand : ConsoleCmdAbstract {
    private const string Usage = @"

Usage:

  1. bloodrain start [duration_irl_minutes]
  2. bloodrain stop

";

    public override string getDescription() {
      return "Blood rain administration";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "bloodrain", "br" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      try {
        if (@params.Count < 1) {
          SdtdConsole.Instance.Output("No subcommand specified.");
          SdtdConsole.Instance.Output(Usage);
          return;
        }

        var command = @params[0];
        switch (command) {
          case "start":
            var durationIrlMinutes = 15f;
            if (@params.Count > 1) {
              if (!float.TryParse(@params[1], out durationIrlMinutes)) {
                SdtdConsole.Instance.Output("Unable to parse duration_irl_minutes " + @params[1]);
                SdtdConsole.Instance.Output(Usage);
                return;
              }
            }

            SdtdConsole.Instance.Output($"Starting blood rain for {durationIrlMinutes} IRL minutes...");
            BloodRain.StartBloodRain(durationIrlMinutes);
            break;
          case "stop":
            if (!BloodRain.IsBloodRainTime()) {
              SdtdConsole.Instance.Output("No active blood rain to be stopped");
              return;
            }

            SdtdConsole.Instance.Output("Stopping blood rain...");
            BloodRain.StopBloodRain();
            break;
          default:
            SdtdConsole.Instance.Output("Unknown command: " + command);
            break;
        }
      } catch (Exception e) {
        Log.Error("Error in BloodRainConsoleCommand.Execute: " + e.Message);
      }
    }
  }
}

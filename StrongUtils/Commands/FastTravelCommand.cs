using System;
using System.Collections.Generic;

namespace StrongUtils.Commands {
  public class FastTravelCommand : ConsoleCmdAbstract {

    public override string getDescription() {
      return "Manages fast travel settings and donation tier progress.";
    }

    public override string getHelp() {
      return "Usage:\n" +
             "  1. fasttravel donations (Broadcasts progress to all)\n" +
             "  2. fasttravel donations <name/ID> (Sends progress to specific player)";
    }

    public override string[] getCommands() {
      return new[] { "fasttravel" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      if (@params.Count > 0 && @params[0].Equals("donations", StringComparison.OrdinalIgnoreCase)) {
        var message = FastTravel.GetDonationsStatus();

        if (@params.Count == 2) {
          ClientInfo client = ConsoleHelper.ParseParamIdOrName(@params[1]);
          if (client != null) {
            Chat.Whisper(client, message);
          } else {
            SdtdConsole.Instance.Output($"Player '{@params[1]}' not found.");
          }
        } else {
          Chat.Global(message);
        }
      } else {
        SdtdConsole.Instance.Output(getHelp());
      }
    }
  }
}

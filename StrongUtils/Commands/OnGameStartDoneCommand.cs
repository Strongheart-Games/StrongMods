using System;
using System.Collections.Generic;

namespace StrongUtils.Commands {
  public class OnGameStartDoneCommand : ConsoleCmdAbstract {
    private const string Usage = @"Usage: ongamestartdone ""<command>""";

    public override string getDescription() {
      return "Adds a command to run on game start done.";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "ongamestartdone" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      if (@params.Count == 0) {
        SdtdConsole.Instance.Output(Usage);
        return;
      }

      var command = @params[0];

      try {
        ServerLifecycleCommands.AddCommand(command);
        SdtdConsole.Instance.Output($"Added command to run on game start done: {command}");
      } catch (Exception e) {
        Log.Error($"Error in OnGameStartDoneCommand.Execute: {e.Message}");
        SdtdConsole.Instance.Output($"Error adding command: {e.Message}");
      }
    }
  }
}

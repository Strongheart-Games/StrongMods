using System.Collections.Generic;

namespace StrongUtils.Commands {
  public class DenyAllCommand : ConsoleCmdAbstract {
    private const string Usage = @"

Usage:

  1. denyall enable [<message>]
  2. denyall disable
  3. denyall status

";

    public override string getDescription() {
      return "Tools to manage whether the server authorizes new connections.";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "denyall" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      if (@params.Count < 1) {
        SdtdConsole.Instance.Output("Not enough parameters provided.");
        SdtdConsole.Instance.Output(Usage);
        return;
      }

      var command = @params[0].ToLower();
      switch (command) {
        case "enable":
          var message = @params.Count > 1 ? @params[1] : null;
          DenyAll.Enable(GameUtils.EKickReason.ManualKick, message);
          SdtdConsole.Instance.Output("New connections will be denied.");
          break;
        case "disable":
          if (!DenyAll.IsEnabled()) {
            SdtdConsole.Instance.Output("New connections are already being allowed.");
            return;
          }

          DenyAll.Disable();
          SdtdConsole.Instance.Output("New connections will be allowed.");
          break;
        case "status":
          SdtdConsole.Instance.Output($"Enabled: {DenyAll.IsEnabled()}");
          if (DenyAll.IsEnabled()) {
            SdtdConsole.Instance.Output($"Reason: {DenyAll.GetReason()}");
            SdtdConsole.Instance.Output($"Message: {DenyAll.GetMessage()}");
          }

          break;
        default:
          SdtdConsole.Instance.Output($"Unrecognized command: {command}");
          SdtdConsole.Instance.Output(Usage);
          break;
      }
    }
  }
}

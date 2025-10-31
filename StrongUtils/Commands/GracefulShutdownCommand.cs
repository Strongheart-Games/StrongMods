using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace StrongUtils.Commands {
  public class GracefulShutdownCommand : ConsoleCmdAbstract {
    private const string Usage = @"

Usage:

  1. gracefulshutdown start <minutes>
  2. gracefulshutdown cancel

";

    private static Coroutine s_shutdownCoroutine;

    public override string getDescription() {
      return "Starts a countdown, announced in global chat every minute until the countdown ends, then shuts down the server.";
    }

    public override string getHelp() {
      return Usage;
    }

    public override string[] getCommands() {
      return new[] { "gracefulshutdown", "gshutdown" };
    }

    public override void Execute(List<string> @params, CommandSenderInfo senderInfo) {
      if (@params is null || @params.Count < 1) {
        SdtdConsole.Instance.Output("No subcommand specified.");
        SdtdConsole.Instance.Output(Usage);
        return;
      }

      switch (@params[0].ToLower()) {
        case "start":
          if (@params.Count < 2) {
            SdtdConsole.Instance.Output("start requires a number of minutes for the timer.");
            SdtdConsole.Instance.Output(Usage);
            return;
          }

          int minutes;
          if (!int.TryParse(@params[1], out minutes) || minutes <= 0) {
            SdtdConsole.Instance.Output($"Minutes must be a positive integer: {@params[1]}");
            SdtdConsole.Instance.Output(Usage);
          }

          Start(minutes);
          return;
        case "cancel":
          Cancel();
          return;
        default:
          SdtdConsole.Instance.Output($"Unrecognized subcommand: {@params[0]}");
          SdtdConsole.Instance.Output(Usage);
          return;
      }
    }

    private void Start(int minutes) {
      SdtdConsole.Instance.Output("Counting down to shutdown...");
      s_shutdownCoroutine = GameManager.Instance.StartCoroutine(ShutdownAfterCountdown(minutes));
    }

    private void Cancel() {
      if (s_shutdownCoroutine is null) {
        SdtdConsole.Instance.Output("No pending shutdown to cancel.");
        return;
      }

      SdtdConsole.Instance.Output("Cancelling pending shutdown...");
      GameManager.Instance.StopCoroutine(s_shutdownCoroutine);
      s_shutdownCoroutine = null;
      Announce($"[00ff00]The pending shutdown has been [ff0000]cancelled");
    }

    private IEnumerator ShutdownAfterCountdown(int minutes) {
      for (var i = 0; i < minutes; i++) {
        Announce($"[00ff00]The server will shutdown in [ff0000]{minutes - i} minute(s)");
        yield return new WaitForSeconds(60);
      }

      Shutdown();
    }

    private void Shutdown() {
      SdtdConsole.Instance.ExecuteSync("shutdown", null);
    }

    private void Announce(string message) {
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, message, null, EMessageSender.None);
    }
  }
}

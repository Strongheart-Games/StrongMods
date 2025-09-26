using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrongUtils {
  public class ShutdownHandler {
    private static Coroutine s_shutdownCoroutine;
    private readonly List<string> _args;

    private readonly ConsoleCmdShutdown _command;
    private readonly CommandSenderInfo _senderInfo;
    private int _delayMinutes;
    private bool _cancel;

    public ShutdownHandler(ConsoleCmdShutdown command, List<string> args, CommandSenderInfo senderInfo) {
      _command = command;
      _args = args;
      _senderInfo = senderInfo;
    }

    public void Execute() {
      if (!ParseArgs()) {
        return;
      }

      if (_cancel) {
        if (s_shutdownCoroutine is null) {
          SdtdConsole.Instance.Output("No pending shutdown to cancel.");
          return;
        }

        SdtdConsole.Instance.Output("Cancelling pending shutdown...");
        GameManager.Instance.StopCoroutine(s_shutdownCoroutine);
        s_shutdownCoroutine = null;
        Announce($"[00ff00]The pending shutdown has been [ff0000]cancelled");
        return;
      }

      if (_delayMinutes <= 0) {
        Shutdown();
        return;
      }

      SdtdConsole.Instance.Output("Starting countdown to shutdown...");
      s_shutdownCoroutine = GameManager.Instance.StartCoroutine(ShutdownAfterDelay());
    }

    private IEnumerator ShutdownAfterDelay() {
      for (var i = 0; i < _delayMinutes; i++) {
        Announce($"[00ff00]The server will shutdown in [ff0000]{_delayMinutes - i} minute(s)");
        yield return new WaitForSeconds(60);
      }

      Shutdown();
    }

    private void Shutdown() {
      SdtdConsole.Instance.Output("Shutting server down...");
      Application.Quit();
    }

    private void Announce(string message) {
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, message, null, EMessageSender.None);
    }

    private bool ParseArgs() {
      if (_args is null || _args.Count < 1) {
        return true;
      }

      if ("cancel".EqualsCaseInsensitive(_args[0])) {
        _cancel = true;
      } else if (!int.TryParse(_args[0], out _delayMinutes)) {
        return false;
      }

      return true;
    }
  }
}

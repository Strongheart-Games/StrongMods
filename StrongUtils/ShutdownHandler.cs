using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrongUtils {
  public class ShutdownHandler {

    private readonly ConsoleCmdShutdown _command;
    private readonly List<string> _args;
    private readonly CommandSenderInfo _senderInfo;
    private int _delayMinutes = 0;

    public ShutdownHandler(ConsoleCmdShutdown command, List<string> args, CommandSenderInfo senderInfo) {
      _command = command;
      _args = args;
      _senderInfo = senderInfo;
    }

    public void Execute() {
      if (!ParseArgs()) {
        return;
      }

      if (_delayMinutes < 0) {
        Shutdown();
        return;
      }

      GameManager.Instance.StartCoroutine(ShutdownAfterDelay());
    }

    IEnumerator ShutdownAfterDelay() {
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

      if (!int.TryParse(_args[0], out _delayMinutes)) {
        return false;
      }
      return true;
    }
  }
}

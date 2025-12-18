using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UniLinq;

namespace ChatCommandHelper {
  public class ChatCommandHelper {
    private static List<string> s_hiddenCommands = new();
    private static List<string> s_privilegedCommands = new();
    private static List<string> s_asyncCommands = new();

    private static string s_authorizedCvar = "strongsworn";

    public static bool HandleChatMessagePrefix(ref ModEvents.SChatMessageData _data,
      ref (ModEvents.EModEventResult, Mod) __result) {
      if (!TryGetCommand(_data.Message, out var command)) {
        return true;
      }

      if (s_hiddenCommands.ContainsCaseInsensitive(command)) {
        Whisper(_data, $"Unrecognized command: {_data.Message}");
        __result = (ModEvents.EModEventResult.StopHandlersAndVanilla, Initializer.ModInstance);
        return false;
      }

      if (s_privilegedCommands.ContainsCaseInsensitive(command) && !IsAuthorized(_data)) {
        Whisper(_data, "Only the StrongSworn may use the community fast-travel network. Talk to The Ferryman and pray he finds you worthy.");
        __result = (ModEvents.EModEventResult.StopHandlersAndVanilla, Initializer.ModInstance);
        return false;
      }

      return true;
    }

    public static void HandleChatMessagePostfix(ref ModEvents.SChatMessageData _data,
      ref (ModEvents.EModEventResult, Mod) __result) {
      if (__result.Item1 != ModEvents.EModEventResult.Continue || !TryGetCommand(_data.Message, out var command)) {
        return;
      }

      var whisper = s_asyncCommands.ContainsCaseInsensitive(command)
        ? $"Processing command: {_data.Message}"
        : $"Unrecognized command: {_data.Message}";
      Whisper(_data, whisper);

      // Takaro can see messages that get handled by other mods, so it's okay to stop handling its commands
      __result = (ModEvents.EModEventResult.StopHandlersAndVanilla, Initializer.ModInstance);
    }

    private static bool TryGetCommand(string message, out string command) {
      if (!message.StartsWith("/")) {
        command = null;
        return false;
      }

      var iSpace = message.IndexOf(' ');
      command = message.Substring(1, iSpace > 0 ? iSpace - 1 : message.Length - 1);
      return !string.IsNullOrWhiteSpace(command);
    }

    private static bool IsAuthorized(ModEvents.SChatMessageData _data) {
      var player = GameManager.Instance.World.GetEntity(_data.SenderEntityId) as EntityPlayer;
      // null implies it's the server, which is always authorized
      return player is null || player.GetCVar(s_authorizedCvar) != 0;
    }

    private static void Whisper(ModEvents.SChatMessageData _data, string message) {
      // Use the net package's logic to determine whether to send the message to the client or not
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        new List<int> { _data.SenderEntityId }, EMessageSender.None,
        GeneratedTextManager.BbCodeSupportMode.Supported);
      package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        return;
      }
      List<string> hiddenCommands = new();
      List<string> privilegedCommands = new();
      List<string> asyncCommands = new();
      var authorizedCvar = "strongsworn";
      ParseList(properties, "chat_command_helper_hidden_commands", ref hiddenCommands);
      ParseList(properties, "chat_command_helper_privileged_commands", ref privilegedCommands);
      ParseList(properties, "chat_command_helper_async_commands", ref asyncCommands);
      properties.ParseString("chat_command_helper_authorized_cvar", ref authorizedCvar);
      s_hiddenCommands = hiddenCommands;
      s_privilegedCommands = privilegedCommands;
      s_asyncCommands = asyncCommands;
      s_authorizedCvar = authorizedCvar;

      Log.Out($"[ChatCommandHelper] Loaded configuration: (hidden: {string.Join(", ", hiddenCommands)}, privileged: {string.Join(", ", privilegedCommands)}, async: {string.Join(", ", asyncCommands)}, authorizedCvar: {authorizedCvar}");
    }

    private static void ParseList(DynamicProperties properties, string name, ref List<string> l) {
      var value = "";
      properties?.ParseString(name, ref value);
      l.Clear();
      if (string.IsNullOrEmpty(value)) {
        return;
      }
      l.AddRange(value.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));
    }
  }

  // We need to guarantee hidden and gated commands are handled before vanilla chat commands and async and unrecognized commands after. We could create two separate mods and carefully install them, but this way we get just one and don't have to play naming tricks.
  [HarmonyPatch(typeof(ModEvents.ModEventInterruptible<ModEvents.SChatMessageData>),
    nameof(ModEvents.ModEventInterruptible<ModEvents.SChatMessageData>.Invoke))]
  public class ModEventInterruptible_Invoke_Patch {
    private static bool Prefix(ref ModEvents.SChatMessageData _data, ref (ModEvents.EModEventResult, Mod) __result) {
      return ChatCommandHelper.HandleChatMessagePrefix(ref _data, ref __result);
    }

    private static void Postfix(ref ModEvents.SChatMessageData _data, ref (ModEvents.EModEventResult, Mod) __result) {
      ChatCommandHelper.HandleChatMessagePostfix(ref _data, ref __result);
    }
  }

  [HarmonyPatch(typeof(WorldEnvironment), nameof(WorldEnvironment.OnXMLChanged))]
  public class WorldEnvironment_OnXMLChanged_Patch {
    private static void Postfix() {
      ChatCommandHelper.OnXMLChanged();
    }
  }

  public class Initializer : IModApi {
    public static Mod ModInstance { get; private set; }

    public void InitMod(Mod _modInstance) {
      ModInstance = _modInstance;
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
  }
}

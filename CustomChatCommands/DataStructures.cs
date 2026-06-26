using System.Collections.Generic;

namespace CustomChatCommands {
  public enum ActionType {
    Console,
    Whisper,
    Broadcast,
    Unknown
  }

  public class CommandAction {
    public ActionType Type { get; set; }
    public string CommandText { get; set; }
  }

  public class CommandRequirement {
    public string Type { get; set; }
    public string Name { get; set; }
    public float Value { get; set; }
  }

  public class ChatCommand {
    public string Trigger { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string Description { get; set; }
    public int MinAdminLevel { get; set; } = 1000;

    public List<CommandRequirement> Requirements { get; set; } = new();
    public List<CommandAction> Actions { get; set; } = new();
    public List<CommandAction> UnauthorizedActions { get; set; } = new();
  }
}

using System;
using System.Collections.Generic;

namespace BloodRain {
  public class BloodRainChatCommand {
    private static readonly List<string> s_commands = new() { "/bloodrain", "/br" };

    public static ModEvents.EModEventResult OnChatMessage(ref ModEvents.SChatMessageData data) {
      if (string.IsNullOrEmpty(data.Message) || !s_commands.ContainsCaseInsensitive(data.Message.Trim())) {
        return ModEvents.EModEventResult.Continue;
      }

      string message;
      DateTime? endTime = BloodRain.GetBloodRainEndTime();
      if (endTime is not null) {
          TimeSpan endTimeSpan = (endTime - DateTime.Now).Value;
          message = $"The Blood Rain will end in {endTimeSpan.ToDynamicReadableString()}.";
      } else {
        var minGameDay = BloodRain.GetMinGameDay();
        if (GameManager.Instance.World.WorldDay < minGameDay) {
          message = $"The first Blood Rain cannot occur before game day {minGameDay}.";
        } else {
          DateTime? nextTime = BloodRain.GetNextScheduledBloodRainTime();
          if (nextTime is null) {
            message = "No Blood Rain is active or scheduled.";
          } else {
            TimeSpan nextTimeSpan = (nextTime - DateTime.Now).Value;
            message = $"The next Blood Rain is in {nextTimeSpan.ToDynamicReadableString()}.";
          }
        }
      }

      GameManager.Instance.ChatMessageServer(data.ClientInfo, EChatType.Whisper, -1, message, null,
        EMessageSender.None);
      return ModEvents.EModEventResult.StopHandlersAndVanilla;
    }
  }

  public static class TimeSpanExtensions {
    public static string ToDynamicReadableString(this TimeSpan timeSpan) {
      // 1. Extract the raw structural components (ignoring total/cumulative values)
      var components = new List<(int Value, string Name)> {
        (timeSpan.Days, "day"),
        (timeSpan.Hours, "hour"),
        (timeSpan.Minutes, "minute"),
        (timeSpan.Seconds, "second")
      };

      // 2. Find where your data actually starts (the first non-zero unit)
      var startIndex = components.FindIndex(c => c.Value > 0);

      // Handle case where timespan is less than 1 second
      if (startIndex == -1) {
        return "0 seconds";
      }

      var parts = new List<string>();

      // 3. Add the primary significant unit
      (int Value, string Name) primary = components[startIndex];
      parts.Add($"{primary.Value} {primary.Name}{(primary.Value > 1 ? "s" : "")}");

      // 4. Evaluate precision rules: Add a second unit only if primary value is 1 or 2
      if (primary.Value < 3 && startIndex + 1 < components.Count) {
        (int Value, string Name) secondary = components[startIndex + 1];
        // Only add if the secondary unit actually has a value
        if (secondary.Value > 0) {
          parts.Add($"{secondary.Value} {secondary.Name}{(secondary.Value > 1 ? "s" : "")}");
        }
      }

      // 5. Format the output string nicely
      if (parts.Count == 1) {
        return parts[0];
      }

      return $"{parts[0]} and {parts[1]}";
    }
  }
}

using System;
using System.Collections.Generic;
using Cronos;
using JetBrains.Annotations;

namespace BloodRain {
  public class BloodRainSchedule {
    public float CountdownIrlMinutes;
    public CronExpression CronExpression;
    public float DurationIrlMinutes;
    public DateTime NextStartTime;
    public TimeSpan? NextWarning;

    public BloodRainSchedule(CronExpression cronExpression, float durationIrlMinutes, float countdownIrlMinutes,
      DateTime nextStartTime, TimeSpan? nextWarning = null) {
      CronExpression = cronExpression;
      DurationIrlMinutes = durationIrlMinutes;
      CountdownIrlMinutes = countdownIrlMinutes;
      NextStartTime = nextStartTime;
      NextWarning = nextWarning;
    }
  }

  public class BloodRain {
    public const float DefaultDurationIrlMinutes = 15f;
    public const float DefaultCountdownIrlMinutes = 15f;
    public const int DefaultMinGameDay = 7;
    public const int DefaultPartyEnemyCountMax = 30;
    public const string BloodRainBuff = "buff_blood_rain";
    public const string BloodRainRemainingSecondsCVar = "blood_rain_remaining_seconds";

    [CanBeNull] private static BloodRainSchedule s_schedule;
    private static int s_minGameDay = DefaultMinGameDay;
    private static string s_secondWarningMessage;
    private static int s_partyEnemyCountMax = DefaultPartyEnemyCountMax;

    private static DateTime? s_endTime;

    public static int GetPartyEnemyCountMax() {
      return s_partyEnemyCountMax;
    }

    public static bool IsBloodRainTime() {
      return s_endTime is not null;
    }

    public static float GetRemainingBloodRainSeconds() {
      if (s_endTime is null) {
        return -1;
      }

      return (float)((DateTime)s_endTime - DateTime.Now).TotalSeconds;
    }

    public static DateTime? GetBloodRainEndTime() {
      return s_endTime;
    }

    public static int GetMinGameDay() {
      return s_minGameDay;
    }

    public static DateTime? GetNextScheduledBloodRainTime() {
      return s_schedule?.NextStartTime;
    }

    public static void Update() {
      var worldTime = GameManager.Instance?.World?.worldTime;
      if (worldTime is null) {
        return;
      }

      var currentDay = GameUtils.WorldTimeToDays(worldTime.Value);
      DateTime now = DateTime.Now;
      if (s_schedule?.NextWarning is not null && now >= s_schedule.NextStartTime - s_schedule.NextWarning) {
        if (currentDay < s_minGameDay) {
          UpdateNextStartTime();
          return;
        }

        WarnPlayers();
      }

      if (s_schedule is not null && now >= s_schedule.NextStartTime) {
        if (currentDay >= s_minGameDay) {
          StartBloodRain(s_schedule.DurationIrlMinutes);
        }

        UpdateNextStartTime();
        return;
      }

      if (s_endTime is null || now < s_endTime) {
        UpdateBloodRainBuff();
        return;
      }

      StopBloodRain();
    }

    public static void UpdateNextStartTime() {
      if (s_schedule is null) {
        return;
      }

      DateTime? next = GetNextStartTime(s_schedule.CronExpression, s_schedule.NextStartTime);
      if (next is null) {
        SetSchedule(null);
        return;
      }

      s_schedule.NextStartTime = (DateTime)next;
      s_schedule.NextWarning = GetNextWarningTimeSpan((DateTime)next, s_schedule.CountdownIrlMinutes);
    }

    public static void StartBloodRain(float durationIrlMinutes = DefaultDurationIrlMinutes) {
      if (s_endTime is null) {
        GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
          "[ff0000]The blood rain is upon us; defend yourselves![-]", null,
          EMessageSender.None);
      }

      DateTime newEndTime = DateTime.Now + TimeSpan.FromMinutes(durationIrlMinutes);
      if (s_endTime is null || s_endTime < newEndTime) {
        s_endTime = newEndTime;
        UpdateBloodRainBuff();
        SetBloodRainWeather(durationIrlMinutes);
      }

      BloodRainChallenge.OnBloodRainStart();
    }

    public static void SkipBloodRain() {
      DateTime? originalStartTime = s_schedule?.NextStartTime;
      UpdateNextStartTime();
      DateTime? newStartTime = s_schedule?.NextStartTime;
      if (newStartTime is not null && originalStartTime is not null) {
        var tz = TimeZoneInfo.Local.DisplayName;
        GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
          $"[00ff00]Skipping the blood rain scheduled for {originalStartTime:ddd @ h:mm tt} {tz}; the next blood rain will be {newStartTime:ddd @ h:mm tt} {tz}[-]", null,
          EMessageSender.None);
      }
    }

    public static void StopBloodRain() {
      s_endTime = null;
      UpdateBloodRainBuff();
      SetDefaultWeather();
      BloodRainChallenge.OnBloodRainEnd();
    }

    private static void SetBloodRainWeather(float durationIrlMinutes) {
      WeatherManager.Instance.ForceWeather("bloodRain", durationIrlMinutes * 60);
    }

    private static void SetDefaultWeather(float durationIrlMinutes = 1) {
      WeatherManager.Instance.ForceWeather("default", durationIrlMinutes * 60);
    }

    private static void UpdateBloodRainBuff() {
      List<EntityPlayer> playerList = GameManager.Instance?.World?.Players?.list;
      if (playerList is null) {
        return;
      }

      var remaining = GetRemainingBloodRainSeconds();
      foreach (EntityPlayer p in playerList) {
        if (IsBloodRainTime()) {
          p.SetCVar(BloodRainRemainingSecondsCVar, remaining);
          if (!p.Buffs.HasBuff(BloodRainBuff)) {
            p.Buffs.AddBuff(BloodRainBuff);
          }
        } else {
          p.Buffs.RemoveCustomVar(BloodRainRemainingSecondsCVar);
          if (p.Buffs.HasBuff(BloodRainBuff)) {
            p.Buffs.RemoveBuff(BloodRainBuff);
          }
        }
      }
    }

    private static void WarnPlayers() {
      if (s_schedule?.NextWarning is null) {
        return;
      }

      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
        $"[ff0000]The blood rain will start in {FormatTimeSpan((TimeSpan)s_schedule.NextWarning)}.[-]",
        null, EMessageSender.None);
      if (s_secondWarningMessage is not null && s_secondWarningMessage.Length > 0) {
        GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, s_secondWarningMessage, null, EMessageSender.None);
      }
      s_schedule.NextWarning =
        GetNextWarningTimeSpan(s_schedule.NextStartTime, s_schedule.CountdownIrlMinutes, s_schedule.NextWarning);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
      var hours = (int)timeSpan.TotalMinutes / 60;
      var minutes = (int)timeSpan.TotalMinutes % 60;

      var hourPart = hours > 0 ? $"{hours} {(hours == 1 ? "hour" : "hours")}" : null;
      var minutePart = minutes > 0 ? $"{minutes} {(minutes == 1 ? "minute" : "minutes")}" : null;

      if (hourPart != null && minutePart != null)
        return $"{hourPart} {minutePart}";

      return hourPart ?? minutePart ?? "0 minutes";
    }

    public static void OnXMLChanged() {
      var schedule = "";
      var duration = DefaultDurationIrlMinutes;
      var countdown = DefaultCountdownIrlMinutes;
      var minGameDay = DefaultMinGameDay;
      var secondWarningMessage = "";
      var partyEnemyCountMax = DefaultPartyEnemyCountMax;

      DynamicProperties properties = WorldEnvironment.Properties?.GetClass("blood_rain");
      if (properties is not null) {
        properties.ParseString("schedule_irl", ref schedule);
        properties.ParseFloat("duration_irl_minutes", ref duration);
        properties.ParseFloat("countdown_irl_minutes", ref countdown);
        properties.ParseInt("min_game_day", ref minGameDay);
        properties.ParseString("second_warning_message", ref secondWarningMessage);
        properties.ParseInt("party_enemy_count_max", ref partyEnemyCountMax);
      }
      LoadSchedule(schedule, duration, countdown);
      s_minGameDay = minGameDay;
      s_secondWarningMessage = secondWarningMessage;
      s_partyEnemyCountMax = partyEnemyCountMax;
    }

    private static void LoadSchedule(string scheduleStr = "", float durationIrlMinutes = DefaultDurationIrlMinutes,
      float countdownIrlMinutes = DefaultCountdownIrlMinutes) {
      if (scheduleStr.Length <= 0) {
        SetSchedule(null);
        return;
      }

      CronExpression cronExpression;
      try {
        cronExpression = CronExpression.Parse(scheduleStr);
      } catch (Exception e) {
        Log.Error($"[BloodRain] Could not parse schedule string '{scheduleStr}': {e.Message}");
        SetSchedule(null);
        return;
      }

      DateTime? nextStartTime = GetNextStartTime(cronExpression);
      if (nextStartTime is null) {
        // If there's no future time, no point in storing the schedule
        SetSchedule(null);
        return;
      }

      TimeSpan? nextWarning = GetNextWarningTimeSpan((DateTime)nextStartTime, countdownIrlMinutes);
      SetSchedule(new BloodRainSchedule(cronExpression, durationIrlMinutes, countdownIrlMinutes,
        (DateTime)nextStartTime, nextWarning));
    }

    private static void SetSchedule([CanBeNull] BloodRainSchedule schedule) {
      Log.Out(schedule is null
        ? "[BloodRain] No schedule set"
        : $"[BloodRain] Schedule set to {schedule.CronExpression} with a duration of {schedule.DurationIrlMinutes} IRL minutes");
      s_schedule = schedule;
    }

    private static DateTime? GetNextStartTime(CronExpression cronExpression, DateTime? lastStartTime = null) {
      DateTime fromDateTime = lastStartTime ?? DateTime.Now;
      DateTime.SpecifyKind(fromDateTime, DateTimeKind.Local);
      DateTimeOffset from = fromDateTime;

      DateTimeOffset? nextOffset = cronExpression.GetNextOccurrence(from, TimeZoneInfo.Local);
      DateTime? next = nextOffset?.DateTime;
      Log.Out($"[BloodRain] Next blood rain is scheduled for {next}");
      return next;
    }

    private static TimeSpan? GetNextWarningTimeSpan(DateTime nextStartTime, float countdownIrlMinutes,
      TimeSpan? lastWarning = null) {
      var remaining = (nextStartTime - DateTime.Now).TotalMinutes;
      if (remaining < 1) {
        // Don't warn at 0 minutes
        return null;
      }

      var max = lastWarning is null ? countdownIrlMinutes : ((TimeSpan)lastWarning).TotalMinutes - 1;
      var next = Math.Floor(Math.Min(max, remaining));
      Log.Out($"[BloodRain] Next warning is scheduled for {next} minutes before the blood rain");
      return TimeSpan.FromMinutes(next);
    }

    public static void OnGameAwake(ref ModEvents.SGameAwakeData data) {
      var bloodMoonFrequency = GamePrefs.GetInt(EnumGamePrefs.BloodMoonFrequency);
      if (bloodMoonFrequency == 0) {
        return;
      }
      GamePrefs.Set(EnumGamePrefs.BloodMoonFrequency, 0);
      Log.Warning($"[BloodRain] Forcing BloodMoonFrequency to 0; was set to {bloodMoonFrequency} in the config file.");
    }
  }
}

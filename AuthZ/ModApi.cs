using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;

namespace AuthZ {
  public class ModApi : IModApi {
    private const string SettingsFileName = "AuthZ.xml";
    private const string SettingsFolderName = "StrongMods";
    private const string TemplateRelativePath = @"Docs\AuthZ.default.xml";

    private static string s_modPath;

    public void InitMod(Mod _modInstance) {
      s_modPath = _modInstance.Path;

      // Settings must be read BEFORE PatchAll: the engine's patch surface is decided by which invariants are
      // enabled, and Harmony resolves TargetMethods() at patch time. Reading settings later would patch the
      // defaults and then quietly disagree with the file.
      InvariantEngine.EnsureRegistered();
      LoadSettings(_modInstance);
      InvariantEngine.ApplySettings(Warn);

      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      ReportEnabled();

      ModEvents.PlayerDisconnected.RegisterHandler(OnPlayerDisconnected);
    }

    private static void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData _data) {
      if (_data.ClientInfo != null) {
        InvariantEngine.ForgetClient(_data.ClientInfo);
      }
    }

    /// <summary>
    /// Reads the live settings file, seeding it from the shipped template on first run. Any failure leaves every
    /// invariant at its default log-only mode — a settings typo must never silently disable a check.
    /// </summary>
    private static void LoadSettings(Mod modInstance) {
      string path;
      try {
        path = Path.Combine(Path.Combine(GameIO.GetSaveGameDir(), SettingsFolderName), SettingsFileName);
      } catch (Exception) {
        // InitMod runs before a save is resolvable in some host configurations.
        Log.Out("[AuthZ] No save folder yet; every invariant stays in log mode.");
        return;
      }

      try {
        if (!File.Exists(path)) {
          SeedFromTemplate(path, modInstance);
        }

        if (File.Exists(path)) {
          Settings.Load(XDocument.Load(path), Warn);
        }
      } catch (Exception e) {
        Log.Warning("[AuthZ] Could not read " + path + "; every invariant stays in log mode.");
        Log.Exception(e);
      }
    }

    private static void SeedFromTemplate(string path, Mod modInstance) {
      var template = Path.Combine(modInstance.Path ?? s_modPath ?? "", TemplateRelativePath);
      if (!File.Exists(template)) {
        return;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.Copy(template, path);
      Log.Out("[AuthZ] Wrote default settings to " + path + ".");
    }

    private static void ReportEnabled() {
      var logging = 0;
      var blocking = 0;
      var off = 0;
      foreach (var invariant in InvariantEngine.All) {
        switch (invariant.Mode) {
          case InvariantMode.Log: logging++; break;
          case InvariantMode.Block: blocking++; break;
          default: off++; break;
        }
      }

      Log.Out("[AuthZ] " + InvariantEngine.All.Count + " invariants: " + logging + " logging, " + blocking +
              " blocking, " + off + " off. PlayerKillingMode=" +
              (EnumPlayerKillingMode)GamePrefs.GetInt(EnumGamePrefs.PlayerKillingMode) + ".");

      if (blocking > 0) {
        Log.Warning("[AuthZ] " + blocking + " invariant(s) will DROP packets. Confirm each one against your own " +
                    "logs before leaving it in block mode.");
      }
    }

    private static void Warn(string message) {
      Log.Warning("[AuthZ] " + SettingsFileName + ": " + message);
    }
  }
}

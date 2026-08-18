using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;

namespace BountifulQuests {
  public class ModApi : IModApi {
    private const string SettingsFileName = "BountifulQuests.xml";
    private const string SettingsFolderName = "StrongMods";

    /// <summary>Reference copy shipped with the mod, used to seed the live settings file on first run.</summary>
    private const string TemplateRelativePath = @"Docs\BountifulQuests.default.xml";

    private static string s_modPath;

    /// <summary>
    /// The knobs this mod runs on. Never null: before the settings file is read, and after any failure reading it,
    /// this holds defaults, and the defaults reproduce vanilla behaviour.
    /// </summary>
    public static OfferConfig Config { get; private set; } = new();

    public void InitMod(Mod _modInstance) {
      s_modPath = _modInstance.Path;
      Harmony harmony = new(_modInstance.Name);
      harmony.PatchAll(Assembly.GetExecutingAssembly());

      // Settings live beside the save, not in the mod folder: Deploy MIRRORS the mod folder, so a file shipped
      // under Config\ would lose an admin's edits at the next deploy. Same home StrongUtils uses. The save
      // directory is not resolvable at InitMod time, so the read waits for GameAwake.
      ModEvents.GameAwake.RegisterHandler(OnGameAwake);
    }

    private static void OnGameAwake(ref ModEvents.SGameAwakeData _data) {
      if (ConnectionManager.Instance.IsClient) {
        return;
      }

      LoadSettings();
    }

    /// <summary>
    /// Reads the live settings file, seeding it from the shipped template the first time. Any failure leaves the
    /// defaults in place and says so in the log: a settings typo must not cost a server its quests.
    /// </summary>
    private static void LoadSettings() {
      var path = Path.Combine(Path.Combine(GameIO.GetSaveGameDir(), SettingsFolderName), SettingsFileName);
      try {
        if (!File.Exists(path)) {
          SeedFromTemplate(path);
        }

        if (!File.Exists(path)) {
          Log.Warning("[BountifulQuests] No settings at " + path + "; using defaults (vanilla behaviour).");
          return;
        }

        Config = OfferConfig.Parse(XDocument.Load(path),
          message => Log.Warning("[BountifulQuests] " + SettingsFileName + ": " + message));
      } catch (Exception e) {
        Config = new OfferConfig();
        Log.Warning("[BountifulQuests] Could not read " + path + "; using defaults (vanilla behaviour).");
        Log.Exception(e);
        return;
      }

      Log.Out("[BountifulQuests] " + Config.OffersPerTier + " offers per tier page, tiers " + Config.MinTier + "-" +
              (Config.MaxTier == 0 ? "current" : Config.MaxTier.ToString()) + ", distance " + DescribeWindow() +
              ", sort " + Config.Sort + ". Settings: " + path);

      if (Config.OffersPerTier > OfferConfig.DisplayBudgetPerTier) {
        Log.Warning("[BountifulQuests] offers/@per_tier is " + Config.OffersPerTier + ", but the trader dialog can " +
                    "show only " + OfferConfig.DisplayBudgetPerTier + " per tier page. The extra quests are " +
                    "generated and are real, but no player can reach them. Lower per_tier, or widen the display " +
                    "(Docs/configuration.md, 'Raising the display budget').");
      }
    }

    private static void SeedFromTemplate(string path) {
      var template = Path.Combine(s_modPath ?? "", TemplateRelativePath);
      if (!File.Exists(template)) {
        return;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.Copy(template, path);
      Log.Out("[BountifulQuests] Wrote default settings to " + path + ". Edit it and restart to change anything.");
    }

    private static string DescribeWindow() {
      if (Config.MinDistance <= 0f && Config.MaxDistance <= 0f) {
        return "unrestricted";
      }

      return (Config.MinDistance <= 0f ? "0" : Config.MinDistance.ToString()) + "-" +
             (Config.MaxDistance <= 0f ? "unlimited" : Config.MaxDistance.ToString()) + "m";
    }
  }
}

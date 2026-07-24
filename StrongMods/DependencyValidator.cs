using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace StrongMods {
  /// <summary>
  ///   Validates the &lt;Dependencies&gt; declarations in every loaded mod's ModInfo.xml against the game version and
  ///   the set of mods that will actually remain loaded, per the ModInfo Dependencies specification
  ///   (.ai/modinfo-dependencies-v1-spec.md). Violating mods that load after StrongMods are unloaded via
  ///   <see cref="ModUnloader" />; mods that load before it are outside blocking coverage, so their violations are only
  ///   reported.
  /// </summary>
  public static class DependencyValidator {
    private const string LogPrefix = "[ModInfoDependencies]";
    private const string ModInfoFilename = "ModInfo.xml";

    public static void Validate(Mod validatorMod) {
      var gameVersion = $"{Constants.cVersionInformation.Major}.{Constants.cVersionInformation.Minor}";
      List<ModSnapshot> snapshots = new(ModManager.loadedMods.Count);
      Dictionary<ModSnapshot, Mod> modsBySnapshot = new();

      var seenValidator = false;
      for (var index = 0; index < ModManager.loadedMods.Count; index++) {
        Mod mod = ModManager.loadedMods.list[index];
        if (mod == validatorMod) {
          seenValidator = true;
        } else if (!seenValidator) {
          // This mod's InitModCode already ran, so unloading it would leave exactly the partial load we exist to avoid
          Log.Warning($"{LogPrefix} Mod '{mod.Name}' loads before {validatorMod.Name} and is outside blocking " +
                      "coverage; its dependency violations can only be reported");
        }

        ModUnloader.TryGetUnloadReason(mod, out var alreadyUnloadingReason);
        ModSnapshot snapshot = new(mod.Name, mod.VersionString, ParseDependencies(mod),
          seenValidator && mod != validatorMod, alreadyUnloadingReason);
        snapshots.Add(snapshot);
        modsBySnapshot[snapshot] = mod;
      }

      foreach (ModEvaluationResult result in DependencyEvaluator.Evaluate(snapshots, gameVersion)) {
        if (result.Violations.Count == 0) {
          continue;
        }

        foreach (var violation in result.Violations) {
          Log.Error($"{LogPrefix} {violation}");
        }

        if (result.ShouldUnload) {
          ModUnloader.MarkForUnload(modsBySnapshot[result.Snapshot], result.Violations[0]);
        } else {
          Log.Error($"{LogPrefix} Mod '{result.Snapshot.Name}' has unsatisfied dependencies but is outside " +
                    "blocking coverage; it remains loaded");
        }
      }
    }

    private static ModDependencies ParseDependencies(Mod mod) {
      var modInfoPath = Path.Combine(mod.Path, ModInfoFilename);
      XDocument document;
      try {
        document = XDocument.Load(modInfoPath);
      } catch (Exception e) {
        Log.Warning($"{LogPrefix} Could not read {modInfoPath} for mod '{mod.Name}': {e.Message}");
        return null;
      }

      XElement root = document.Root;
      if (root is null || root.Element("ModInfo") != null) {
        // A root containing a ModInfo child is the v1 format; the Dependencies extension applies to v2 only
        return null;
      }

      return ModDependencies.Parse(root);
    }
  }
}

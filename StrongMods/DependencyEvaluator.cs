using System;
using System.Collections.Generic;

namespace StrongMods {
  /// <summary>What the evaluator needs to know about one loaded mod. Free of game types so it can be unit tested.</summary>
  public sealed class ModSnapshot {
    public ModSnapshot(string name, string versionString, ModDependencies dependencies, bool canUnload,
      string alreadyUnloadingReason = null) {
      Name = name;
      VersionString = versionString;
      Dependencies = dependencies;
      CanUnload = canUnload;
      AlreadyUnloadingReason = alreadyUnloadingReason;
    }

    public string Name { get; }
    public string VersionString { get; }
    public ModDependencies Dependencies { get; } // null when the mod declares none

    /// <summary>False for mods that load before the validator: their violations can only be reported (spec §6).</summary>
    public bool CanUnload { get; }

    /// <summary>Set when another feature (e.g. case-sensitivity validation) already marked this mod for unloading.</summary>
    public string AlreadyUnloadingReason { get; }
  }

  public sealed class ModEvaluationResult {
    public ModEvaluationResult(ModSnapshot snapshot) {
      Snapshot = snapshot;
    }

    public ModSnapshot Snapshot { get; }
    public List<string> Violations { get; } = new();

    /// <summary>True when the validator should unload this mod.</summary>
    public bool ShouldUnload { get; set; }
  }

  /// <summary>
  ///   Evaluates dependency declarations against the set of mods that will actually remain loaded (spec §5).
  ///   Unloading cascades: an unloaded mod is treated as absent for its dependents, and evaluation repeats until the
  ///   surviving set is stable. Violation messages for cascaded failures identify the root cause.
  /// </summary>
  public static class DependencyEvaluator {
    /// <summary>
    ///   <paramref name="mods" /> must be in load order. <paramref name="gameVersion" /> is the normalized game version
    ///   (no leading 'V', no build suffix). Returns one result per snapshot, in the same order.
    /// </summary>
    public static List<ModEvaluationResult> Evaluate(IReadOnlyList<ModSnapshot> mods, string gameVersion) {
      List<ModEvaluationResult> results = new(mods.Count);
      Dictionary<ModSnapshot, ModEvaluationResult> resultsBySnapshot = new();
      // Root-cause message for every mod that is (or becomes) absent from the surviving set
      Dictionary<string, string> unloadReasons = new();
      List<ModSnapshot> surviving = new();

      foreach (ModSnapshot mod in mods) {
        ModEvaluationResult result = new(mod);
        results.Add(result);
        resultsBySnapshot[mod] = result;
        if (mod.AlreadyUnloadingReason != null) {
          if (!unloadReasons.ContainsKey(mod.Name)) {
            unloadReasons[mod.Name] = mod.AlreadyUnloadingReason;
          }
        } else {
          surviving.Add(mod);
        }
      }

      ModVersion.TryParse(gameVersion, out ModVersion parsedGameVersion);

      // Re-evaluate until the surviving set is stable (a fixpoint): unloading one mod can invalidate its dependents.
      while (true) {
        List<ModSnapshot> newlyUnloaded = new();
        foreach (ModSnapshot mod in surviving) {
          if (mod.Dependencies is null || !mod.CanUnload) {
            continue;
          }

          List<string> violations = EvaluateOne(mod, surviving, unloadReasons, gameVersion, parsedGameVersion);
          if (violations.Count == 0) {
            continue;
          }

          ModEvaluationResult result = resultsBySnapshot[mod];
          result.Violations.Clear();
          result.Violations.AddRange(violations);
          result.ShouldUnload = true;
          newlyUnloaded.Add(mod);
        }

        if (newlyUnloaded.Count == 0) {
          break;
        }

        foreach (ModSnapshot mod in newlyUnloaded) {
          surviving.Remove(mod);
          if (!unloadReasons.ContainsKey(mod.Name)) {
            unloadReasons[mod.Name] = resultsBySnapshot[mod].Violations[0];
          }
        }
      }

      // Mods that cannot be unloaded still get their violations reported (spec §6, reporting level)
      foreach (ModSnapshot mod in surviving) {
        if (mod.Dependencies is null || mod.CanUnload) {
          continue;
        }

        ModEvaluationResult result = resultsBySnapshot[mod];
        result.Violations.AddRange(EvaluateOne(mod, surviving, unloadReasons, gameVersion, parsedGameVersion));
      }

      return results;
    }

    private static List<string> EvaluateOne(ModSnapshot mod, List<ModSnapshot> surviving,
      Dictionary<string, string> unloadReasons, string gameVersion, ModVersion parsedGameVersion) {
      List<string> violations = new();
      ModDependencies dependencies = mod.Dependencies;

      foreach (var error in dependencies.AuthoringErrors) {
        violations.Add($"{mod.Name} {error}");
      }

      if (dependencies.GameConstraint != null && !dependencies.GameConstraint.Satisfies(parsedGameVersion)) {
        violations.Add($"{mod.Name} requires game version {dependencies.GameConstraint}, found {gameVersion}");
      }

      foreach (ModDependency dependency in dependencies.Mods) {
        ModSnapshot target = FindByName(surviving, dependency.Name);
        if (target is null) {
          if (unloadReasons.TryGetValue(dependency.Name, out var rootCause)) {
            // An optional dependency that is blocked is treated as absent, which satisfies the declaration
            if (!dependency.Optional) {
              violations.Add($"{mod.Name} requires {dependency.Name}, which was blocked ({rootCause})");
            }
          } else if (!dependency.Optional) {
            violations.Add($"{mod.Name} requires {dependency.Name}, which is not installed");
          }

          continue;
        }

        if (dependency.Constraint is null) {
          continue;
        }

        if (!ModVersion.TryParse(target.VersionString, out ModVersion targetVersion)) {
          violations.Add($"{mod.Name} requires {dependency.Name} {dependency.Constraint}, but the installed " +
                         $"version '{target.VersionString}' could not be parsed");
          continue;
        }

        if (!dependency.Constraint.Satisfies(targetVersion)) {
          violations.Add(
            $"{mod.Name} requires {dependency.Name} {dependency.Constraint}, found {targetVersion}");
        }
      }

      return violations;
    }

    private static ModSnapshot FindByName(List<ModSnapshot> mods, string name) {
      // Ordinal, case-sensitive: the internal <Name> is the stable identifier (spec §3.2)
      foreach (ModSnapshot mod in mods) {
        if (string.Equals(mod.Name, name, StringComparison.Ordinal)) {
          return mod;
        }
      }

      return null;
    }
  }
}

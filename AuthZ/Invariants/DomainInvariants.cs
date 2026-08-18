using System;

namespace AuthZ.Invariants {
  /// <summary>
  /// Family F: a skill level a client asserts is one the game can actually produce.
  /// </summary>
  /// <remarks>
  /// Purely local — the bound comes from the loaded progression XML, so no world lookup and no tuning. It catches the
  /// blunt case that <see cref="SkillLevelForSelfOnly"/> misses: a client raising its <em>own</em> skill past its
  /// maximum.
  /// </remarks>
  public sealed class SkillLevelWithinRange : Invariant {
    public override string Id => "skill.range";
    public override Family Family => Family.Domain;
    public override Type Package => typeof(NetPackageEntitySetSkillLevelServer);
    public override string Statement => "the asserted skill level is between the skill's own minimum and maximum";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var p = (NetPackageEntitySetSkillLevelServer)package;
      var classes = Progression.ProgressionClasses;
      if (classes == null) {
        // Configuration is not loaded yet; an invariant that cannot decide stays quiet.
        return true;
      }

      if (!classes.TryGetValue(p.skill, out var progressionClass) || progressionClass == null) {
        detail = "skill '" + p.skill + "' is not a known progression class";
        return false;
      }

      if (p.level >= progressionClass.MinLevel && p.level <= progressionClass.MaxLevel) {
        return true;
      }

      detail = "claimed level " + p.level + " for '" + p.skill + "', which allows " +
               progressionClass.MinLevel + ".." + progressionClass.MaxLevel;
      return false;
    }
  }

  /// <summary>
  /// Family F: a buff a client asserts is one the game defines.
  /// </summary>
  /// <remarks>
  /// <c>BuffManager.GetBuff</c> returning null means the buff silently does nothing, so this rule catches probing
  /// rather than harm. It is cheap enough to be worth having anyway: a stream of unknown buff names is a strong signal
  /// that somebody is enumerating the surface.
  /// </remarks>
  public sealed class BuffNameIsKnown : Invariant {
    public override string Id => "buff.known-name";
    public override Family Family => Family.Domain;
    public override Severity Severity => Severity.Suspicious;
    public override Type Package => typeof(NetPackageAddRemoveBuff);
    public override string Statement => "the named buff is one the loaded configuration defines";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var name = ((NetPackageAddRemoveBuff)package).buffName;
      if (string.IsNullOrEmpty(name) || BuffManager.GetBuff(name) != null) {
        return true;
      }

      detail = "buff '" + name + "' is not defined";
      return false;
    }
  }

  /// <summary>
  /// Family F: one experience packet awards no more than a plausible single kill or quest.
  /// </summary>
  /// <remarks>
  /// The ceiling is a setting rather than a derived bound, because the game has no single constant to read: quest
  /// rewards, kill XP and crafting XP all flow through the same packet. Ships generous, and in log-only mode, so a
  /// server can find its own real maximum from the log before tightening it.
  /// </remarks>
  public sealed class ExperienceDeltaIsPlausible : Invariant {
    public override string Id => "exp.delta";
    public override Family Family => Family.Domain;
    public override Severity Severity => Severity.Suspicious;
    public override Type Package => typeof(NetPackageEntityAddExpServer);
    public override string Statement => "one experience award is within the configured per-packet ceiling";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var ceiling = Settings.Current.MaxExperiencePerPacket;
      var xp = ((NetPackageEntityAddExpServer)package).xp;
      if (xp >= 0 && xp <= ceiling) {
        return true;
      }

      detail = "awarded " + xp + " experience, ceiling is " + ceiling;
      return false;
    }
  }

  /// <summary>
  /// Family G: the world editor is not reachable from a client on a live server.
  /// </summary>
  /// <remarks>
  /// These packages exist for the in-development editor. A production server never expects one, so the rule needs no
  /// threshold and has no false positives on a server that is not being edited live.
  /// </remarks>
  public abstract class NoEditorTraffic<TPackage> : Invariant where TPackage : NetPackage {
    public override Family Family => Family.Authority;
    public override Type Package => typeof(TPackage);
    public override string Statement => "no world-editor traffic arrives from a client";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = "sent an editor package to a live server";
      return false;
    }
  }

  public sealed class NoEditorVolumeAdd : NoEditorTraffic<NetPackageEditorAddVolumeFromClient> {
    public override string Id => "editor.volume-add";
  }

  public sealed class NoEditorVolumeUpdate : NoEditorTraffic<NetPackageEditorUpdateVolume> {
    public override string Id => "editor.volume-update";
  }
}

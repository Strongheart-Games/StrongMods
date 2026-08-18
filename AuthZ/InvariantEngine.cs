using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuthZ.Invariants;
using HarmonyLib;

namespace AuthZ {
  /// <summary>
  /// Holds every invariant, decides what to patch, and runs the checks for one inbound packet.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <c>ConnectionManager.ProcessPackages</c> is the single point every inbound packet passes through, but it is a
  /// loop over a list it fills itself, so a prefix cannot see the packets and a transpiler over it would be brittle.
  /// The engine instead patches <c>ProcessPackage</c> on each package type that has an enabled invariant. That keeps
  /// the patch surface proportional to the rules actually turned on — four rules patch four methods, not 124 — and
  /// <c>NetPackage.Sender</c> is already set by then, because <c>NetPackageManager.ParsePackage</c> assigns it before
  /// <c>read()</c>.
  /// </para>
  /// <para>
  /// Detection only unless an invariant is explicitly set to <see cref="InvariantMode.Block"/>. A rule that has never
  /// run against real traffic has no business dropping packets.
  /// </para>
  /// </remarks>
  public static class InvariantEngine {
    private static readonly List<Invariant> s_all = new();
    private static readonly Dictionary<Type, List<Invariant>> s_byPackage = new();

    public static IReadOnlyList<Invariant> All => s_all;

    /// <summary>
    /// Registers the invariant set once. Safe to call repeatedly; the second call is a no-op.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="PatchTargets"/> as well as from mod startup, so the patch surface resolves even when
    /// something asks for it before <c>InitMod</c> has run — which is exactly what the repo's patch-target smoke
    /// test does.
    /// </remarks>
    public static void EnsureRegistered() {
      if (s_all.Count == 0) {
        RegisterAll();
      }
    }

    /// <summary>Every invariant the mod knows about, in report order.</summary>
    public static void RegisterAll() {
      s_all.Clear();
      s_byPackage.Clear();

      Register(new ExperienceForSelfOnly());
      Register(new SkillLevelForSelfOnly());
      Register(new ScoreForSelfOnly());
      Register(new ItemActionForSelfOnly());
      Register(new SmellForSelfOnly());
      Register(new LaserSightForSelfOnly());
      Register(new ChatIdentityIsHonest());

      Register(new NoPvpDamage());
      Register(new NoBuffingOthers());
      Register(new NoVelocityOnOthers());
      Register(new NoRetargetingOthers());

      Register(new SkillLevelWithinRange());
      Register(new BuffNameIsKnown());
      Register(new ExperienceDeltaIsPlausible());

      Register(new EnterGameOnce());
      Register(new SpawnRequestOnce());
      Register(new SpawnOnlyWhenUnbound());

      Register(new NoEditorVolumeAdd());
      Register(new NoEditorVolumeUpdate());
    }

    private static void Register(Invariant invariant) {
      s_all.Add(invariant);
      if (!s_byPackage.TryGetValue(invariant.Package, out var forPackage)) {
        forPackage = new List<Invariant>();
        s_byPackage[invariant.Package] = forPackage;
      }

      forPackage.Add(invariant);
    }

    /// <summary>
    /// The <c>ProcessPackage</c> methods to patch: one per package type with at least one enabled invariant.
    /// </summary>
    /// <remarks>
    /// Resolves to the type that actually <em>declares</em> the override, because several packages inherit it from a
    /// base. Patching the declaring type once and dispatching on the runtime type keeps a base-class patch from
    /// firing twice for one packet.
    /// </remarks>
    public static IEnumerable<MethodBase> PatchTargets() {
      EnsureRegistered();
      var seen = new HashSet<MethodBase>();
      foreach (var pair in s_byPackage) {
        if (pair.Value.All(i => i.Mode == InvariantMode.Off)) {
          continue;
        }

        var method = FindProcessPackage(pair.Key);
        if (method == null) {
          Log.Warning("[AuthZ] No ProcessPackage found on " + pair.Key.Name + "; its invariants are inert.");
          continue;
        }

        if (seen.Add(method)) {
          yield return method;
        }
      }
    }

    private static MethodBase FindProcessPackage(Type packageType) {
      for (var type = packageType; type != null && type != typeof(NetPackage); type = type.BaseType) {
        var declared = AccessTools.DeclaredMethod(type, "ProcessPackage",
          new[] { typeof(World), typeof(GameManager) });
        if (declared != null) {
          return declared;
        }
      }

      return null;
    }

    /// <summary>
    /// Runs every enabled invariant registered for this packet's runtime type and its bases.
    /// </summary>
    /// <returns>False when the packet should be dropped.</returns>
    public static bool Allow(NetPackage package, World world) {
      var sender = package.Sender;
      if (sender == null) {
        // No sender means this is not client traffic: a listen host's own packet, or a server-authored one.
        return true;
      }

      var allow = true;
      for (var type = package.GetType(); type != null && type != typeof(NetPackage); type = type.BaseType) {
        if (!s_byPackage.TryGetValue(type, out var invariants)) {
          continue;
        }

        foreach (var invariant in invariants) {
          if (invariant.Mode == InvariantMode.Off) {
            continue;
          }

          bool holds;
          string detail;
          try {
            holds = invariant.Holds(package, sender, world, out detail);
          } catch (Exception e) {
            // A throwing check must never cost the server a packet, and must never go unnoticed either.
            Log.Warning("[AuthZ] invariant " + invariant.Id + " threw; treating the packet as allowed.");
            Log.Exception(e);
            continue;
          }

          if (holds) {
            continue;
          }

          var blocked = invariant.Mode == InvariantMode.Block;
          ViolationLog.Record(invariant, sender, detail ?? "<no detail>", blocked);
          if (blocked) {
            allow = false;
          }
        }
      }

      return allow;
    }

    /// <summary>
    /// Stamps the loaded settings onto every registered invariant, and reports any setting that named nothing.
    /// </summary>
    public static void ApplySettings(Action<string> warn) {
      EnsureRegistered();
      var known = new HashSet<string>();
      foreach (var invariant in s_all) {
        known.Add(invariant.Id);
        invariant.Mode = Settings.Current.ModeFor(invariant.Id);
      }

      Settings.Current.ReportUnknownIds(known, warn);
    }

    /// <summary>Drops per-connection state when a client leaves.</summary>
    public static void ForgetClient(ClientInfo sender) {
      foreach (var invariant in s_all) {
        switch (invariant) {
          case EnterGameOnce once:
            once.Forget(sender);
            break;
          case SpawnRequestOnce once:
            once.Forget(sender);
            break;
        }
      }

      ViolationLog.Forget(sender);
    }
  }
}

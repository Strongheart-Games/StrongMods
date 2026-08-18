using System;

namespace AuthZ.Invariants {
  /// <summary>
  /// Family B: under a no-killing rule, nothing a client sends may harm another player or anything that player owns.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Only active when the server's <c>PlayerKillingMode</c> is <c>NoKilling</c>. Other killing modes allow player
  /// harm by definition, and pretending otherwise would make the whole family noise.
  /// </para>
  /// <para>
  /// "Owns" resolves through <see cref="Ownership"/>, which covers players, vehicles, drones and turrets in one rule.
  /// </para>
  /// </remarks>
  public abstract class PveInvariant<TPackage> : Invariant where TPackage : NetPackage {
    public override Family Family => Family.Ownership;
    public override Type Package => typeof(TPackage);

    protected abstract int TargetEntityIdOf(TPackage package);

    /// <summary>The action this packet performs, for the log line. Lower case, no punctuation.</summary>
    protected abstract string Action { get; }

    public override string Statement =>
      "under NoKilling, a client does not " + Action + " another player or an entity they own";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      if (!ServerRules.IsNoKilling) {
        return true;
      }

      var targetId = TargetEntityIdOf((TPackage)package);
      var target = world?.GetEntity(targetId);
      if (target == null) {
        // Nothing to protect. The game itself tolerates a stale id here.
        return true;
      }

      if (Ownership.SenderMayAct(target, sender)) {
        return true;
      }

      detail = "tried to " + Action + " " + Ownership.Describe(target) + " (entity " + targetId + ")";
      return false;
    }
  }

  /// <summary>
  /// The rule the mod started with, now covering drones and turrets as well as players and vehicles.
  /// </summary>
  public sealed class NoPvpDamage : PveInvariant<NetPackageDamageEntity> {
    public override string Id => "pve.damage";
    protected override string Action => "damage";
    protected override int TargetEntityIdOf(NetPackageDamageEntity p) => p.entityId;
  }

  /// <summary>
  /// Buffs carry poison, bleed, stun and worse, so applying one to another player's entity is harm by another name.
  /// </summary>
  /// <remarks>
  /// The server relays this package to every client in range before applying it, so an unchecked buff is visible to
  /// the whole area, not only its target.
  /// </remarks>
  public sealed class NoBuffingOthers : PveInvariant<NetPackageAddRemoveBuff> {
    public override string Id => "pve.buff";
    protected override string Action => "buff or debuff";
    protected override int TargetEntityIdOf(NetPackageAddRemoveBuff p) => p.entityId;
  }

  /// <summary>Shoving another player's entity is how a no-damage server still gets people killed by fall damage.</summary>
  public sealed class NoVelocityOnOthers : PveInvariant<NetPackageEntityAddVelocity> {
    public override string Id => "pve.velocity";
    protected override string Action => "add velocity to";
    protected override int TargetEntityIdOf(NetPackageEntityAddVelocity p) => p.entityId;
  }

  /// <summary>Pointing hostile AI at another player is harm routed through a third party.</summary>
  public sealed class NoRetargetingOthers : Invariant {
    public override string Id => "pve.attack-target";
    public override Family Family => Family.Ownership;
    public override Type Package => typeof(NetPackageSetAttackTarget);
    public override string Statement =>
      "a client only retargets AI it owns, and not onto another player";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var retarget = (NetPackageSetAttackTarget)package;
      var actor = world?.GetEntity(retarget.entityId);
      if (actor != null && Ownership.BelongsToAnotherPlayer(actor, sender)) {
        detail = "commanded " + Ownership.Describe(actor) + " (entity " + retarget.entityId + ")";
        return false;
      }

      if (!ServerRules.IsNoKilling) {
        return true;
      }

      var victim = world?.GetEntity(retarget.m_targetId);
      if (victim == null || Ownership.SenderMayAct(victim, sender)) {
        return true;
      }

      detail = "pointed entity " + retarget.entityId + " at " + Ownership.Describe(victim) +
               " (entity " + retarget.m_targetId + ")";
      return false;
    }
  }
}

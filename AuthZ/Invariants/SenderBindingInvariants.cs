using System;

namespace AuthZ.Invariants {
  /// <summary>
  /// Family A: the entity a packet names is the sender's own player entity.
  /// </summary>
  /// <remarks>
  /// The cheapest useful check in the engine — one integer comparison against <c>Sender.entityId</c>, no world
  /// lookup, no tuning, and no false positives, because a real client only ever names itself in these packets.
  /// Vanilla has the helper for exactly this (<c>NetPackage.ValidEntityIdForSender</c>); these are the packages that
  /// do not call it.
  /// </remarks>
  public abstract class OwnEntityIdInvariant<TPackage> : Invariant where TPackage : NetPackage {
    public override Family Family => Family.SenderBinding;
    public override Type Package => typeof(TPackage);
    public override string Statement => "the named entity is the sender's own player entity";

    protected abstract int EntityIdOf(TPackage package);

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var claimed = EntityIdOf((TPackage)package);
      if (claimed == sender.entityId) {
        return true;
      }

      var target = world?.GetEntity(claimed);
      detail = "claimed entity " + claimed + " (" + Ownership.Describe(target) + "), sender is entity " +
               sender.entityId;
      return false;
    }
  }

  /// <summary>Experience is awarded to the entity that earned it.</summary>
  public sealed class ExperienceForSelfOnly : OwnEntityIdInvariant<NetPackageEntityAddExpServer> {
    public override string Id => "exp.self-only";
    protected override int EntityIdOf(NetPackageEntityAddExpServer p) => p.entityId;
  }

  /// <summary>A client raises its own skills, never another player's.</summary>
  public sealed class SkillLevelForSelfOnly : OwnEntityIdInvariant<NetPackageEntitySetSkillLevelServer> {
    public override string Id => "skill.self-only";
    protected override int EntityIdOf(NetPackageEntitySetSkillLevelServer p) => p.entityId;
  }

  /// <summary>Kill and score counters move for the player who made them move.</summary>
  public sealed class ScoreForSelfOnly : OwnEntityIdInvariant<NetPackageEntityAddScoreServer> {
    public override string Id => "score.self-only";
    protected override int EntityIdOf(NetPackageEntityAddScoreServer p) => p.entityId;
  }

  /// <summary>A client reports its own laser sight, not somebody else's.</summary>
  public sealed class LaserSightForSelfOnly : OwnEntityIdInvariant<NetPackagePlayerLaserSight> {
    public override string Id => "lasersight.self-only";
    public override Severity Severity => Severity.Suspicious;
    protected override int EntityIdOf(NetPackagePlayerLaserSight p) => p.entityId;
  }

  /// <summary>A client fires its own item actions.</summary>
  public sealed class ItemActionForSelfOnly : OwnEntityIdInvariant<NetPackageItemActionEffects> {
    public override string Id => "itemaction.self-only";
    protected override int EntityIdOf(NetPackageItemActionEffects p) => p.entityId;
  }

  /// <summary>A client emits its own smell. Smell drives zombie attraction, so this is not cosmetic.</summary>
  public sealed class SmellForSelfOnly : OwnEntityIdInvariant<NetPackageEmitSmell> {
    public override string Id => "smell.self-only";
    protected override int EntityIdOf(NetPackageEmitSmell p) => p.EntityId;
  }

  /// <summary>
  /// Chat carries the sender's own entity id, and a client never claims to be the server.
  /// </summary>
  /// <remarks>
  /// <c>GameManager.ChatMessageServer</c> re-derives the display name from the client-supplied
  /// <c>senderEntityId</c> before broadcasting, so a wrong id puts another player's name on the message. The server's
  /// own log line records the real platform id, which is why the log stays trustworthy when the chat window does not.
  /// <c>msgSender</c> is client-supplied as well, and <c>EMessageSender.Server</c> is a legal value for it.
  /// </remarks>
  public sealed class ChatIdentityIsHonest : Invariant {
    public override string Id => "chat.identity";
    public override Family Family => Family.SenderBinding;
    public override Type Package => typeof(NetPackageChat);
    public override string Statement => "chat names the sender's own entity and does not claim to be the server";

    public override bool Holds(NetPackage package, ClientInfo sender, World world, out string detail) {
      detail = null;
      var chat = (NetPackageChat)package;

      if (chat.msgSender == EMessageSender.Server) {
        detail = "message claimed to come from the server";
        return false;
      }

      // -1 is the legitimate "no particular entity" value the game itself writes.
      if (chat.senderEntityId == -1 || chat.senderEntityId == sender.entityId) {
        return true;
      }

      detail = "message claimed entity " + chat.senderEntityId + ", sender is entity " + sender.entityId;
      return false;
    }
  }
}

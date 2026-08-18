namespace AuthZ {
  /// <summary>Who, if anyone, a world entity belongs to.</summary>
  public enum OwnerRelation {
    /// <summary>Nobody owns it: a zombie, an animal, a world entity, a dropped item.</summary>
    Unowned,

    /// <summary>The sender's own player entity.</summary>
    Self,

    /// <summary>Owned by the sender, or the sender is on its allowed-user list, or is riding it.</summary>
    SenderControls,

    /// <summary>Owned by, or is, another player.</summary>
    OtherPlayer
  }

  /// <summary>
  /// Resolves whether the client that sent a packet is entitled to act on the entity the packet names.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The game spreads ownership across three mechanisms and no single accessor covers them all. Vehicles and drones
  /// implement <c>ILockable</c>, which carries an owner and an allowed-user list. Turrets carry
  /// <c>EntityTurret.OwnerID</c>. Everything a player spawns carries <c>Entity.belongsPlayerId</c>, set by
  /// <c>EntityFactory</c>. Checking all three is what makes "and their vehicles, drones, and robots" one rule rather
  /// than three.
  /// </para>
  /// <para>
  /// Identity is compared through <see cref="ClientInfo.InternalId"/> (<c>CrossplatformId ?? PlatformId</c>) as well
  /// as <c>PlatformId</c>. Comparing only <c>PlatformId</c> misreads a crossplay player as a stranger.
  /// </para>
  /// </remarks>
  public static class Ownership {
    public static OwnerRelation Resolve(Entity target, ClientInfo sender) {
      if (target == null || sender == null) {
        return OwnerRelation.Unowned;
      }

      if (target.entityId == sender.entityId) {
        return OwnerRelation.Self;
      }

      if (target is EntityPlayer) {
        return OwnerRelation.OtherPlayer;
      }

      if (target is EntityVehicle vehicle && IsRiding(vehicle, sender)) {
        return OwnerRelation.SenderControls;
      }

      if (target is ILockable lockable) {
        var relation = FromLockable(lockable, sender);
        if (relation != OwnerRelation.Unowned) {
          return relation;
        }
      }

      if (target is EntityTurret turret && turret.OwnerID != null) {
        return MatchesUser(turret.OwnerID, sender) ? OwnerRelation.SenderControls : OwnerRelation.OtherPlayer;
      }

      if (target.belongsPlayerId != -1) {
        return target.belongsPlayerId == sender.entityId ? OwnerRelation.SenderControls : OwnerRelation.OtherPlayer;
      }

      return OwnerRelation.Unowned;
    }

    /// <summary>True when the entity belongs to somebody other than the sender.</summary>
    public static bool BelongsToAnotherPlayer(Entity target, ClientInfo sender) {
      return Resolve(target, sender) == OwnerRelation.OtherPlayer;
    }

    /// <summary>True when the sender may act on the entity: it is theirs, they ride it, or nobody owns it.</summary>
    public static bool SenderMayAct(Entity target, ClientInfo sender) {
      return Resolve(target, sender) != OwnerRelation.OtherPlayer;
    }

    /// <summary>A short human-readable owner, for the violation log.</summary>
    public static string Describe(Entity target) {
      if (target == null) {
        return "<no entity>";
      }

      var kind = target.GetType().Name;
      if (target is ILockable lockable && lockable.GetOwner() != null) {
        return kind + " owned by " + lockable.GetOwner().CombinedString;
      }

      if (target is EntityTurret turret && turret.OwnerID != null) {
        return kind + " owned by " + turret.OwnerID.CombinedString;
      }

      if (target.belongsPlayerId != -1) {
        return kind + " belonging to entity " + target.belongsPlayerId;
      }

      return kind;
    }

    /// <summary>
    /// Anyone in a seat may act on the vehicle they are in. Checked before the lock, because a passenger the owner
    /// let in is not necessarily on the allowed-user list.
    /// </summary>
    private static bool IsRiding(EntityVehicle vehicle, ClientInfo sender) {
      var slots = vehicle.GetAttachMaxCount();
      for (var slot = 0; slot < slots; slot++) {
        var attached = vehicle.GetAttached(slot);
        if (attached != null && attached.entityId == sender.entityId) {
          return true;
        }
      }

      return false;
    }

    private static OwnerRelation FromLockable(ILockable lockable, ClientInfo sender) {
      var owner = lockable.GetOwner();
      if (owner == null) {
        return OwnerRelation.Unowned;
      }

      if (MatchesUser(owner, sender)) {
        return OwnerRelation.SenderControls;
      }

      if (lockable.IsUserAllowed(sender.PlatformId) ||
          (sender.CrossplatformId != null && lockable.IsUserAllowed(sender.CrossplatformId))) {
        return OwnerRelation.SenderControls;
      }

      return OwnerRelation.OtherPlayer;
    }

    private static bool MatchesUser(PlatformUserIdentifierAbs userId, ClientInfo sender) {
      return Equals(userId, sender.PlatformId) ||
             (sender.CrossplatformId != null && Equals(userId, sender.CrossplatformId));
    }
  }
}

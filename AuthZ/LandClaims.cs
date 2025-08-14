using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AuthZ {
  public class LandClaims {
    public const string ViolationBuffName = "buff_land_claim_violation_strong";
    public const string ViolationRemainingSecondsCVarName = "land_claim_violation_remaining_seconds_strong";
    public const int ViolationTimeLimitSeconds = 10;
    public const int MinDistanceForEnemyProtection = 200;
    public static HashSet<MaterialBlock> RoadMaterials;

    public static void HandleViolations(EntityAlive entity) {
      if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer || GameManager.Instance.IsEditMode()) {
        return;
      }

      // Don't bother with vehicles or drones or friendly NPCs
      if (entity is not EntityPlayer && entity is not EntityEnemy) {
        return;
      }

      // Only check every second as this isn't the most efficient search
      if (Time.time - entity.GetAdditionalData().LastTimeLandClaimsChecked < .5f) {
        return;
      }

      entity.GetAdditionalData().LastTimeLandClaimsChecked = Time.time;

      Bedrolls.UpdateBedrollWarning(entity);

      Vector3 position = entity.position;
      Vector3i? nullableClaim = GetLandClaimPosition(new Vector3i(position));
      if (nullableClaim is null) {
        ClearViolations(entity);
        return;
      }

      var claim = (Vector3i)nullableClaim;

      // Access to LCB is authorized if player or allies are online
      // TODO: Add configurable ACL per LCB
      List<PersistentPlayerData> authorizedPlayers = GetOwnerAndAllies(claim);
      if (authorizedPlayers.Count == 0) {
        Log.Warning($"[AuthZ] No authorized players found for land claim at {claim}");
      }

      if (entity is EntityEnemy) {
        if (IsAnyPlayerNearby(authorizedPlayers, claim)) {
          ClearViolations(entity);
          return;
        }

        entity.Despawn();
        return;
      }

      if (IsPlayerOnUnprotectableLand((EntityPlayer)entity) ||
          IsAnyPlayerInParty(authorizedPlayers, (EntityPlayer)entity)) {
        ClearViolations(entity);
        return;
      }

      HandleViolationForPlayer((EntityPlayer)entity, claim, authorizedPlayers);
    }

    public static Vector3i? GetLandClaimPosition(Vector3i position) {
      World world = GameManager.Instance.World;
      if (world.IsWithinTraderArea(position)) {
        return null;
      }

      HashSet<Chunk> chunks = new();
      var diameterBlocks = GameStats.GetInt(EnumGameStats.LandClaimSize);
      var radiusBlocks = (diameterBlocks - 1) / 2;
      var searchChunks = diameterBlocks / 16 + 1;
      var searchMinX = position.x - radiusBlocks;
      var searchMinZ = position.z - radiusBlocks;
      // TODO: Reason about whether this wide a search is necessary
      for (var i = -searchChunks; i <= searchChunks; ++i) {
        var x = searchMinX + i * 16;
        for (var j = -searchChunks; j <= searchChunks; ++j) {
          var z = searchMinZ + j * 16;
          var c = (Chunk)world.GetChunkFromWorldPos(new Vector3i(x, position.y, z));
          if (c == null || !chunks.Add(c)) {
            continue;
          }

          Vector3i? landClaim = GetLandClaimPosition(position, c, radiusBlocks);
          if (landClaim != null) {
            return landClaim;
          }
        }
      }

      return null;
    }

    public static Vector3i? GetLandClaimPosition(Vector3i position, Chunk chunk, int claimRadiusBlocks) {
      List<Vector3i> blocks = chunk.IndexedBlocks["lpblock"];
      if (blocks == null) {
        return null;
      }

      Vector3i chunkWorldPosition = chunk.GetWorldPos();
      foreach (Vector3i b in blocks) {
        if (!BlockLandClaim.IsPrimary(chunk.GetBlock(b))) {
          continue;
        }

        Vector3i blockWorldPosition = b + chunkWorldPosition;
        var deltaX = Math.Abs(blockWorldPosition.x - position.x);
        var deltaZ = Math.Abs(blockWorldPosition.z - position.z);
        if (deltaX <= claimRadiusBlocks && deltaZ <= claimRadiusBlocks) {
          return blockWorldPosition;
        }
      }

      return null;
    }

    // Returns owner first
    public static List<PersistentPlayerData> GetOwnerAndAllies(Vector3i landClaimPosition) {
      List<PersistentPlayerData> authorized = new();
      PersistentPlayerList persistentPlayerList = GameManager.Instance.GetPersistentPlayerList();
      if (persistentPlayerList == null) {
        return authorized;
      }

      PersistentPlayerData owner = persistentPlayerList.GetLandProtectionBlockOwner(landClaimPosition);
      if (owner == null) {
        return authorized;
      }

      authorized.Add(owner);
      HashSet<PlatformUserIdentifierAbs> acl = owner.ACL;
      if (acl == null) {
        return authorized;
      }

      authorized.AddRange(acl.Select(ally => persistentPlayerList.GetPlayerData(ally)));

      return authorized;
    }

    public static bool IsAnyPlayerNearby(IEnumerable<PersistentPlayerData> players, Vector3i position) {
      foreach (PersistentPlayerData p in players) {
        if (p.EntityId <= 0) { // Min ID is 1; offline players are -1
          continue;
        }

        var distance = (p.Position - position).ToVector3().magnitude;
        if (distance <= MinDistanceForEnemyProtection) {
          return true;
        }
      }

      return false;
    }

    public static bool IsPlayerOnUnprotectableLand(EntityPlayer player) {
      return player.prefab is null && IsRoad(player.blockValueStandingOn.Block);
    }

    public static bool IsRoad(Block block) {
      RoadMaterials ??= new HashSet<MaterialBlock> {
        MaterialBlock.materials["Mconcrete"],
        MaterialBlock.materials["Mgravel"]
      };
      return block.shape.IsTerrain() && RoadMaterials.Contains(block.blockMaterial);
    }

    public static bool IsAnyPlayerInParty(IEnumerable<PersistentPlayerData> players, EntityPlayer targetPlayer) {
      List<int> party = targetPlayer.Party?.GetMemberIdList(null) ?? new List<int> { targetPlayer.entityId };
      return players.Any(p => party.Contains(p.EntityId));
    }

    public static void ClearViolations(EntityAlive __instance) {
      // We don't track violations for non-players
      if (__instance is EntityPlayer) {
        __instance.GetAdditionalData().ViolatedClaim = null;
        __instance.Buffs.RemoveCustomVar(ViolationRemainingSecondsCVarName);
        __instance.Buffs.RemoveBuff(ViolationBuffName);
      }
    }

    public static void HandleViolationForPlayer(EntityPlayer player, Vector3i claim,
      List<PersistentPlayerData> allowedPlayers) {
      Vector3i playerPos = new(player.position);
      var biome = GameManager.Instance.World.GetBiome(playerPos.x, playerPos.z)?.LocalizedName ?? "unknown";
      string message;
      if (player.GetAdditionalData().ViolatedClaim != claim) {
        player.GetAdditionalData().ViolatedClaim = claim;
        player.GetAdditionalData().ViolationStartTime = Time.time;
        player.SetCVar(ViolationRemainingSecondsCVarName, ViolationTimeLimitSeconds);
        player.Buffs.AddBuff(ViolationBuffName, _buffDuration: ViolationTimeLimitSeconds);

        var playerDisplayName = player.PlayerDisplayName;
        var ownerDisplayName = allowedPlayers[0].PlayerName.DisplayName;
        var playerDebugLocation = playerPos.ToDebugLocation();
        message = $"{playerDisplayName} entered {ownerDisplayName}'s {biome} claim at ({playerDebugLocation})";
        Log.Out($"[AuthZ] {message}");
        GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, $"[A0]{message}", null, EMessageSender.None);
        return;
      }

      // Wait to take any action until player has been in violation of the same claim for 5+ seconds
      var elapsed = Time.time - player.GetAdditionalData().ViolationStartTime;
      if (elapsed < ViolationTimeLimitSeconds) {
        player.SetCVar(ViolationRemainingSecondsCVarName, ViolationTimeLimitSeconds - elapsed);
        return;
      }

      message =
        $"Removed {player.PlayerDisplayName} from {allowedPlayers[0].PlayerName.DisplayName}'s {biome} claim at ({playerPos.ToDebugLocation()})";
      Log.Out($"[AuthZ] {message}");
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1, $"[ff0000]{message}", null,
        EMessageSender.None);
      RemovePlayerFromClaim(player, claim);
    }

    public static void RemovePlayerFromClaim(EntityPlayer player, Vector3i claim) {
      // Make a direction vector from the LCB to the entity position
      Vector3 direction = player.position - claim;
      // Ignore y (up/down)
      direction.y = 0.0f;
      // Scale to land claim size so that we're sure the player is teleported outside the claim
      var landClaimSize = GameStats.GetInt(EnumGameStats.LandClaimSize);
      direction *= 1.3f * landClaimSize / direction.magnitude;
      // Send them that distance and direction from the LCB
      Vector3 targetPosition = claim + direction;
      var minRange = 0;
      var maxRange = 15;
      if (!player.world.GetRandomSpawnPositionMinMaxToPosition(targetPosition, minRange, maxRange, 1, false,
            out Vector3 newPosition,
            player.entityId, _retryCount: 20, _checkLandClaim: true, _maxLandClaimType: EnumLandClaimOwner.Ally,
            _useSquareRadius: true) ||
          newPosition == Vector3.zero) { // The zero test doesn't seem necessary, but there's a bug somewhere
        targetPosition = claim;
        minRange = (int)(1.1f * landClaimSize);
        maxRange = (int)(1.5f * landClaimSize);
        if (!player.world.GetRandomSpawnPositionMinMaxToPosition(targetPosition, minRange, maxRange, 1,
              false, out newPosition, player.entityId, _retryCount: 20, _checkLandClaim: true,
              _maxLandClaimType: EnumLandClaimOwner.Ally, _useSquareRadius: true) ||
            newPosition == Vector3.zero) { // Same as above
          PersistentPlayerData playerData =
            GameManager.Instance.persistentPlayers.GetPlayerDataFromEntityID(player.entityId);
          if (playerData.HasBedrollPos) {
            Log.Warning(
              $"[AuthZ] Could not find a valid teleport position; respawning {player.PlayerDisplayName} at bedroll");
            newPosition = playerData.BedrollPos;
          } else {
            Log.Warning(
              $"[AuthZ] Could not find a valid teleport position or bedroll; killing {player.PlayerDisplayName}");
            player.DamageEntity(new DamageSource(EnumDamageSource.Internal, EnumDamageTypes.Suicide), 99999, false);
            return;
          }
        }
      }

      Log.Out($"[AuthZ] Teleporting {player.PlayerDisplayName} to {newPosition}");
      if (player.isEntityRemote) {
        SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(player.entityId)
          .SendPackage(NetPackageManager.GetPackage<NetPackageTeleportPlayer>().Setup(newPosition));
      } else {
        player.Teleport(newPosition);
      }
    }

    public static void AuthorizeBlockChanges(NetPackageSetBlock package) {
      List<BlockChangeInfo> unauthorized = new();
      foreach (BlockChangeInfo change in package.blockChanges) {
        if (change.bChangeBlockValue && !change.blockValue.isair &&
            change.blockValue.Block.GetType().IsAssignableFrom(typeof(BlockLandClaim))) {
          World world = GameManager.Instance.World;
          PersistentPlayerList players = GameManager.Instance.GetPersistentPlayerList();
          Vector3i pos = change.pos;
          PersistentPlayerData player = players.GetPlayerDataFromEntityID(change.changedByEntityId);
          var authorized = world.CanPlaceLandProtectionBlockAt(pos, player);
          Log.Out(
            $"[AuthZ] AuthorizeBlockChanges {player.PlayerName.DisplayName} attempted to place a LCB; authorized: {authorized}");
          if (!authorized) {
            unauthorized.Add(change);
          }
        }
      }

      foreach (BlockChangeInfo change in unauthorized) {
        package.blockChanges.Remove(change);
      }
    }
  }

  public class EntityAliveAdditionalData {
    public float LastTimeLandClaimsChecked;
    public Vector3? ViolatedClaim;
    public float ViolationStartTime;
  }

  public static class EntityAliveExtension {
    private static readonly ConditionalWeakTable<EntityAlive, EntityAliveAdditionalData> Data = new();

    public static EntityAliveAdditionalData GetAdditionalData(this EntityAlive entity) {
      return Data.GetOrCreateValue(entity);
    }
  }
}

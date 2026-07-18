using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace AutoCollectLoot {
  public class LootLottery {
    private static GameRandom s_random;
    private readonly EntityAlive _killed;

    public LootLottery(EntityAlive killed) {
      if (s_random is null) {
        s_random = new GameRandom();
        s_random.SetSeed((int)Stopwatch.GetTimestamp());
      }

      _killed = killed;
    }

    public EntityPlayer ChooseWinner(float killshotBonusChance = 0.5f) {
      if (_killed is null) {
        return null;
      }

      // Apply the killshot bonus
      var killer = _killed.entityThatKilledMe as EntityPlayer;
      if (killer is not null && s_random.NextDouble() < killshotBonusChance) {
        return killer;
      }

      var range = GameStats.GetInt(EnumGameStats.PartySharedKillRange);
      List<Entity> candidates;
      if (killer is not null) {
        if (killer.Party == null) {
          return killer;
        }

        candidates = new List<Entity>();
        for (var i = 0; i < killer.Party.MemberList.Count; i++) {
          EntityPlayer member = killer.Party.MemberList[i];
          if (member.entityId == killer.entityId || Vector3.Distance(killer.position, member.position) < range) {
            candidates.Add(member);
          }
        }
      } else {
        candidates = new List<Entity>();
        GameManager.Instance.World.GetEntitiesAround(EntityFlags.Player, _killed.position, range, candidates);
      }

      // TODO: bad luck protection

      if (candidates.Count == 0) {
        return null;
      }

      var candidatesStr = string.Join(", ", candidates.OfType<EntityPlayer>().Select(p => p.PlayerDisplayName));
      Log.Out($"[AutoCollectLoot] Choosing loot winner from candidates: {candidatesStr}");
      return (EntityPlayer)candidates[s_random.RandomRange(candidates.Count)];
    }
  }
}

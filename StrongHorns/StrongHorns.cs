using System;
using System.Collections.Concurrent;
using Audio;
using HarmonyLib;
using UnityEngine;

namespace StrongHorns {
  public static class StrongHorns {
    private static readonly ConcurrentBag<Action<EntityVehicle>> s_onHonkListeners = new();

    public static void RegisterHonkListener(Action<EntityVehicle> listener) {
      if (listener is not null) {
        s_onHonkListeners.Add(listener);
      }
    }

    private static void OnHonk(EntityVehicle vehicle) {
      foreach (Action<EntityVehicle> listener in s_onHonkListeners) {
        listener.Invoke(vehicle);
      }
    }

    [HarmonyPatch(typeof(Server), nameof(Server.Play), typeof(Vector3), typeof(string), typeof(float), typeof(int),
      typeof(float))]
    public class Server_Play1_Patch {
      private static void Prefix(string soundGroupName, int entityId) {
        Entity entity = GameManager.Instance.World.GetEntity(entityId);
        Server_Play2_Patch.Prefix(entity, soundGroupName);
      }
    }

    [HarmonyPatch(typeof(Server), nameof(Server.Play), typeof(Entity), typeof(string), typeof(float), typeof(bool),
      typeof(float))]
    public class Server_Play2_Patch {
      public static void Prefix(Entity playOnEntity, string soundGroupName) {
        if (playOnEntity is not EntityVehicle vehicle) {
          return;
        }

        if (soundGroupName is null || soundGroupName != vehicle.vehicle.GetHornSoundName()) {
          return;
        }

        OnHonk(vehicle);
      }
    }
  }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;

namespace StrongBoxes {
  public static class StrongBoxes {
    private static readonly ConcurrentBag<Listener> s_listeners = new();

    public static void RegisterOnCloseListener(Predicate<string> filter, Action<TileEntity> callback) {
      s_listeners.Add(new Listener(filter, callback));
    }

    public static HashSet<long> CalculateAdjacentChunkKeys(Vector3i pos) {
      var posX = World.toChunkXZ(pos.x);
      var posZ = World.toChunkXZ(pos.z);
      var keys = new HashSet<long>();
      for (var x = posX - 1; x <= posX + 1; x++) {
        for (var z = posZ - 1; z <= posZ + 1; z++) {
          keys.Add(WorldChunkCache.MakeChunkKey(x, z));
        }
      }

      return keys;
    }

    private static void OnUnlock(TEFeatureStorage storage) {
      TileEntityComposite parent = storage.Parent;
      if (!storage.Parent.TryGetSelfOrFeature(out TEFeatureSignable signable)) {
        return;
      }

      var text = signable.GetAuthoredText().Text.ToLower();
      foreach (Listener l in s_listeners) {
        if (l.Filter(text)) {
          l.Callback?.Invoke(parent);
        }
      }
    }

    [HarmonyPatch(typeof(TEFeatureStorage), nameof(TEFeatureStorage.OnUnlockedServer))]
    public class TEFeatureStorage_OnUnlockedServer_Patch {
      private static void Prefix(TEFeatureStorage __instance) {
        OnUnlock(__instance);
      }
    }

    private struct Listener {
      public readonly Predicate<string> Filter;
      public readonly Action<TileEntity> Callback;

      public Listener(Predicate<string> filter, Action<TileEntity> callback) {
        Filter = filter;
        Callback = callback;
      }
    }
  }
}

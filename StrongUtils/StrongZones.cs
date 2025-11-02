using System.Collections.Generic;

namespace StrongUtils {
  public static class StrongZones {
    private static List<StrongZone> s_strongZones = new();

    public static void SeedChunkProtectionLevels(
      Dictionary<long, ChunkProtectionLevel> chunkProtectionLevels,
      Dictionary<HashSetLong, ChunkProtectionLevel> groupProtectionLevels,
      Dictionary<long, HashSetLong> groupsByChunkKey) {
      new StrongZoneChunkProtector(chunkProtectionLevels, groupProtectionLevels, groupsByChunkKey)
        .ProtectStrongZones(s_strongZones);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        return;
      }

      var protectedZones = "";
      properties.ParseString("strong_zones_protected_zones", ref protectedZones);
      var zones = protectedZones.Split('|');
      List<StrongZone> strongZones = new List<StrongZone>();
      foreach (var zone in zones) {
        var strongZone = StrongZone.Parse(zone);
        if (strongZone != null) {
          strongZones.Add(strongZone);
        }
      }
      s_strongZones = strongZones;
    }
  }

  public class StrongZone {
    public Vector2i Corner;
    public Vector2i OppositeCorner;

    public StrongZone(Vector2i corner, Vector2i oppositeCorner) {
      Corner = corner;
      OppositeCorner = oppositeCorner;
    }

    public static StrongZone Parse(string s) {
      var corners = s.Split(';');
      if (corners.Length != 2) {
        return null;
      }

      Vector2i? corner = ParseVector(corners[0]);
      if (corner == null) {
        return null;
      }

      Vector2i? oppositeCorner = ParseVector(corners[1]);
      if (oppositeCorner == null) {
        return null;
      }

      return new StrongZone(corner.Value, oppositeCorner.Value);
    }

    private static Vector2i? ParseVector(string s) {
      var trimmed = s.Trim('(', ' ', ')');
      var components = trimmed.Split(',');
      if (components.Length != 2) {
        return null;
      }

      var x = int.Parse(components[0].Trim());
      var y = int.Parse(components[1].Trim());
      return new Vector2i(x, y);
    }
  }

  public class StrongZoneChunkProtector {
    private readonly Dictionary<long, ChunkProtectionLevel> _sChunkProtectionLevels;
    private readonly Dictionary<HashSetLong, ChunkProtectionLevel> _sGroupProtectionLevels;
    private readonly Dictionary<long, HashSetLong> _sGroupsByChunkKey;

    public StrongZoneChunkProtector(
      Dictionary<long, ChunkProtectionLevel> chunkProtectionLevels,
      Dictionary<HashSetLong, ChunkProtectionLevel> groupProtectionLevels,
      Dictionary<long, HashSetLong> groupsByChunkKey) {
      _sChunkProtectionLevels = chunkProtectionLevels;
      _sGroupProtectionLevels = groupProtectionLevels;
      _sGroupsByChunkKey = groupsByChunkKey;
    }

    public void ProtectStrongZones(List<StrongZone> strongZones) {
      if (strongZones is null) {
        return;
      }
      var chunks = 0;
      foreach (var strongZone in strongZones) {
        chunks += ProtectStrongZone(strongZone);
      }
      Log.Out($"[StrongUtils] Protected {chunks} chunks in {strongZones.Count} StrongZones");
    }

    public int ProtectStrongZone(StrongZone strongZone) {
      var x1 = strongZone.Corner.x;
      var z1 = strongZone.Corner.y;
      var x2 = strongZone.OppositeCorner.x;
      var z2 = strongZone.OppositeCorner.y;
      var west = x1 < x2 ? x1 : x2;
      var east = x1 > x2 ? x1 : x2;
      var south = z1 < z2 ? z1 : z2;
      var north = z1 > z2 ? z1 : z2;
      return ProtectStrongZone(west, east, south, north);
    }

    public int ProtectStrongZone(int west, int east, int south, int north) {
      west = World.toChunkXZ(west);
      east = World.toChunkXZ(east);
      south = World.toChunkXZ(south);
      north = World.toChunkXZ(north);
      var chunks = 0;
      for (var x = west; x <= east; x++) {
        for (var y = south; y <= north; y++) {
          chunks++;
          var key = WorldChunkCache.MakeChunkKey(x, y);
          AddProtectionLevel(key, ChunkProtectionLevel.LandClaim);
        }
      }
      return chunks;
    }

    // TODO: figure out how to call this private method instead of replicating it here
    public void AddProtectionLevel(
      long chunkKey,
      ChunkProtectionLevel protectionLevel) {
      if (_sChunkProtectionLevels.TryGetValue(chunkKey, out ChunkProtectionLevel currentChunkProtectionLevel)) {
        if (protectionLevel == (protectionLevel & currentChunkProtectionLevel)) {
          return;
        }

        _sChunkProtectionLevels[chunkKey] = currentChunkProtectionLevel | protectionLevel;
      } else {
        _sChunkProtectionLevels[chunkKey] = protectionLevel;
      }

      if (!_sGroupsByChunkKey.TryGetValue(chunkKey, out HashSetLong key)) {
        return;
      }

      if (_sGroupProtectionLevels.TryGetValue(key, out ChunkProtectionLevel currentGroupProtectionLevel)) {
        _sGroupProtectionLevels[key] = currentGroupProtectionLevel | protectionLevel;
      } else {
        _sGroupProtectionLevels[key] = protectionLevel;
      }
    }
  }
}

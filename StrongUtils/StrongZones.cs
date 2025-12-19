using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;

namespace StrongUtils {
  public class StrongZones {
    public delegate void EnemyStatusCallback(EntityEnemy enemy, StrongZone zone);

    public delegate void PlayerStatusCallback(EntityPlayer player, StrongZone zone);

    private const string SConfigFileName = "strong_zones.xml";
    private const string SDefaultConfig = "<config></config>";

    private static StrongZones s_zones;

    private static readonly List<PlayerStatusCallback> s_playerEnterCallbacks = new();
    private static readonly List<PlayerStatusCallback> s_playerLeaveCallbacks = new();
    private static readonly List<EnemyStatusCallback> s_enemyEnterCallbacks = new();
    private static readonly List<EnemyStatusCallback> s_enemyLeaveCallbacks = new();
    private readonly List<StrongZone> _customZones = new();
    private readonly Dictionary<long, List<StrongZone>> _enemyZonesByChunk = new();
    private readonly Dictionary<long, List<StrongZone>> _playerZonesByChunk = new();

    private readonly List<StrongZone> _prefabZones = new();
    private readonly Dictionary<long, ChunkProtectionLevel> _protectionLevelByChunk = new();

    private StrongZones(List<StrongZone> prefabZones = null, List<StrongZone> customZones = null) {
      _prefabZones = prefabZones ?? _prefabZones;
      _customZones = customZones ?? _customZones;
      InitializeChunkCaches();
      Log.Out(
        $"[StrongZones] Loaded {prefabZones?.Count ?? 0} prefab zones and {customZones?.Count ?? 0} custom zones");
      Log.Out($"[StrongZones] Prefab zones: \n  {string.Join("\n  ", _prefabZones)}");
      Log.Out($"[StrongZones] Custom zones: \n  {string.Join("\n  ", _customZones)}");
    }

    private void InitializeChunkCaches() {
      InitializeChunkCaches(_prefabZones);
      InitializeChunkCaches(_customZones);
    }

    private void InitializeChunkCaches(List<StrongZone> zones) {
      if (zones is null) {
        return;
      }

      foreach (StrongZone z in zones) {
        InitializeChunkCaches(z);
      }
    }

    private void InitializeChunkCaches(StrongZone zone) {
      var minX = World.toChunkXZ(zone.MinX);
      var maxX = World.toChunkXZ(zone.MaxX);
      var minZ = World.toChunkXZ(zone.MinZ);
      var maxZ = World.toChunkXZ(zone.MaxZ);
      HashSetLong keys = new();
      for (var x = minX; x <= maxX; x++) {
        for (var y = minZ; y <= maxZ; y++) {
          var key = WorldChunkCache.MakeChunkKey(x, y);
          if (!keys.Add(key)) {
            continue;
          }

          if (!_playerZonesByChunk.ContainsKey(key)) {
            _playerZonesByChunk.Add(key, new List<StrongZone>());
          }

          _playerZonesByChunk[key].Add(zone);

          if (zone.NoHostiles) {
            if (!_enemyZonesByChunk.ContainsKey(key)) {
              _enemyZonesByChunk.Add(key, new List<StrongZone>());
            }

            _enemyZonesByChunk[key].Add(zone);
          }

          if (zone.NoReset) {
            _protectionLevelByChunk.Add(key, ChunkProtectionLevel.LandClaim);
          }
        }
      }
    }

    public static void Init() {
      StrongSwornOnlyEnforcer.Init();
      NoHostileEnforcer.Init();
      ConfigManager.Instance.RegisterConfigFile(SConfigFileName, SDefaultConfig, UpdateCustomZones);
      s_zones = new StrongZones(GeneratePrefabZones(), GenerateCustomZones());
    }

    public static void RegisterPlayerCallbacks(PlayerStatusCallback onEnter = null,
      PlayerStatusCallback onLeave = null) {
      if (onEnter != null) {
        s_playerEnterCallbacks.Add(onEnter);
      }

      if (onLeave != null) {
        s_playerLeaveCallbacks.Add(onLeave);
      }
    }

    public static void RegisterEnemyCallbacks(EnemyStatusCallback onEnter = null, EnemyStatusCallback onLeave = null) {
      if (onEnter != null) {
        s_enemyEnterCallbacks.Add(onEnter);
      }

      if (onLeave != null) {
        s_enemyLeaveCallbacks.Add(onLeave);
      }
    }

    public static void SeedChunkProtectionLevels(
      Dictionary<long, ChunkProtectionLevel> chunkProtectionLevels,
      Dictionary<HashSetLong, ChunkProtectionLevel> groupProtectionLevels,
      Dictionary<long, HashSetLong> groupsByChunkKey) {
      var protector = new StrongZoneChunkProtector(chunkProtectionLevels, groupProtectionLevels, groupsByChunkKey);
      foreach (KeyValuePair<long, ChunkProtectionLevel> chunk in s_zones._protectionLevelByChunk) {
        protector.AddProtectionLevel(chunk.Key, chunk.Value);
      }
    }

    public static void OnUpdateEntity(Entity entity) {
      Dictionary<long, List<StrongZone>> zonesByChunk;
      switch (entity) {
        case EntityPlayer:
          zonesByChunk = s_zones._playerZonesByChunk;
          break;
        case EntityEnemy:
          zonesByChunk = s_zones._enemyZonesByChunk;
          break;
        default:
          return;
      }

      Vector2i chunk = World.toChunkXZ(entity.position);
      var key = WorldChunkCache.MakeChunkKey(chunk.x, chunk.y);
      List<StrongZone> zones = zonesByChunk.GetValueSafe(key);
      UpdateCurrentZones(entity, zones?.Where(z => z.Contains(entity.position)).ToList());
    }

    private static void UpdateCurrentZones(Entity entity, List<StrongZone> newZones) {
      List<StrongZone> currentZones = entity.GetCurrentZones();
      entity.SetCurrentZones(newZones);
      FindZoneChanges(currentZones, newZones, entity.position, out List<StrongZone> entered,
        out List<StrongZone> left);
      if (entered is not null) {
        foreach (StrongZone zone in entered) {
          Log.Out($"[StrongZones] {entity.name} entered {zone.Name}");
          switch (entity) {
            case EntityPlayer player:
              NotifyPlayerEntered(player, zone);
              break;
            case EntityEnemy enemy:
              NotifyEnemyEntered(enemy, zone);
              break;
          }
        }
      }

      if (left is not null) {
        foreach (StrongZone zone in left) {
          Log.Out($"[StrongZones] {entity.name} left {zone.Name}");
          switch (entity) {
            case EntityPlayer player:
              NotifyPlayerLeft(player, zone);
              break;
            case EntityEnemy enemy:
              NotifyEnemyLeft(enemy, zone);
              break;
          }
        }
      }
    }

    internal static void NotifyPlayerEntered(EntityPlayer player, StrongZone zone) {
      foreach (PlayerStatusCallback callback in s_playerEnterCallbacks) {
        try {
          callback(player, zone);
        } catch (Exception ex) {
          Log.Error($"[StrongZones] Error in enter callback: {ex}");
        }
      }
    }

    internal static void NotifyPlayerLeft(EntityPlayer player, StrongZone zone) {
      foreach (PlayerStatusCallback callback in s_playerLeaveCallbacks) {
        try {
          callback(player, zone);
        } catch (Exception ex) {
          Log.Error($"[StrongZones] Error in leave callback: {ex}");
        }
      }
    }

    internal static void NotifyEnemyEntered(EntityEnemy enemy, StrongZone zone) {
      foreach (EnemyStatusCallback callback in s_enemyEnterCallbacks) {
        try {
          callback(enemy, zone);
        } catch (Exception ex) {
          Log.Error($"[StrongZones] Error in enter callback: {ex}");
        }
      }
    }

    internal static void NotifyEnemyLeft(EntityEnemy enemy, StrongZone zone) {
      foreach (EnemyStatusCallback callback in s_enemyLeaveCallbacks) {
        try {
          callback(enemy, zone);
        } catch (Exception ex) {
          Log.Error($"[StrongZones] Error in leave callback: {ex}");
        }
      }
    }

    private static void FindZoneChanges(
      List<StrongZone> oldZones,
      List<StrongZone> newZones,
      Vector3 pos,
      out List<StrongZone> added,
      out List<StrongZone> removed) {
      added = null;
      removed = null;

      // Find added zones (in filtered new but not in old)
      if (newZones != null) {
        for (var i = 0; i < newZones.Count; i++) {
          StrongZone zone = newZones[i];
          if (!zone.Contains(pos)) {
            continue;
          }

          var foundInOld = false;
          if (oldZones != null) {
            for (var j = 0; j < oldZones.Count; j++) {
              if (ReferenceEquals(zone, oldZones[j])) {
                foundInOld = true;
                break;
              }
            }
          }

          if (!foundInOld) {
            added ??= new List<StrongZone>();
            added.Add(zone);
          }
        }
      }

      // Find removed zones (in old but not in filtered new)
      if (oldZones != null) {
        for (var i = 0; i < oldZones.Count; i++) {
          StrongZone zone = oldZones[i];

          var foundInNew = false;
          if (newZones != null) {
            for (var j = 0; j < newZones.Count; j++) {
              StrongZone newZone = newZones[j];
              if (newZone.Contains(pos) && ReferenceEquals(zone, newZone)) {
                foundInNew = true;
                break;
              }
            }
          }

          if (!foundInNew) {
            removed ??= new List<StrongZone>();
            removed.Add(zone);
          }
        }
      }
    }

    public static void UpdatePrefabZones() {
      s_zones = new StrongZones(GeneratePrefabZones());
    }

    private static List<StrongZone> GeneratePrefabZones(List<PrefabInstance> prefabs = null) {
      prefabs ??= GameManager.Instance.World.ChunkCache.ChunkProvider.GetDynamicPrefabDecorator()?.allPrefabs;
      List<StrongZone> zones = new();
      if (prefabs is null || prefabs.Count == 0) {
        return zones;
      }

      foreach (PrefabInstance prefab in prefabs) {
        if (TryGeneratePrefabZone(prefab, out StrongZone zone)) {
          zones.Add(zone);
        }
      }

      return zones;
    }

    private static bool TryGeneratePrefabZone(PrefabInstance prefabInstance, out StrongZone zone) {
      var shouldHaveStrongZone = prefabInstance.name.ContainsCaseInsensitive("shs3");
      if (shouldHaveStrongZone) {
        Log.Out($"[StrongZones] Trying to generate StrongZone for {prefabInstance.name}");
      }

      zone = null;
      Prefab prefab = prefabInstance.prefab;
      List<string> tags = prefab.GetStrongZoneTags();
      if (tags is null || tags.Count == 0) {
        if (shouldHaveStrongZone) {
          Log.Out($"[StrongZones] Skipping {prefabInstance.name} because it isn't a StrongZone");
        }

        return false;
      }

      var name = prefabInstance.name;
      var cornerXZ = new Vector2i(prefabInstance.boundingBoxPosition.x, prefabInstance.boundingBoxPosition.z);
      Vector2i oppositeCornerXZ =
        cornerXZ + new Vector2i(prefabInstance.boundingBoxSize.x, prefabInstance.boundingBoxSize.z);

      zone = new StrongZone(name, cornerXZ, oppositeCornerXZ, tags);
      Log.Out($"[StrongZones] Generated StrongZone: {zone}");
      return true;
    }

    public static void InitializeStrongZoneExtensions(Prefab prefab) {
      List<string> tags = null;
      if (prefab.properties.Classes.ContainsKey("StrongZones")) {
        Log.Out($"[StrongZones] Prefab {prefab.PrefabName} has StrongZones config");
        DynamicProperties properties = prefab.properties.Classes["StrongZones"];
        tags = properties.Contains("Tags") ? properties.Values["Tags"].Split(',').ToList() : null;
      }

      prefab.SetStrongZoneTags(tags);
    }

    public static void CloneStrongZoneExtensions(Prefab into, Prefab from, bool sharedData = false) {
      List<string> tags = from.GetStrongZoneTags();
      if (!sharedData && tags is not null) {
        tags = new List<string>(tags);
      }

      into.SetStrongZoneTags(tags);
    }

    private static void UpdateCustomZones(XElement element) {
      s_zones = new StrongZones(customZones: GenerateCustomZones(element));
    }

    private static List<StrongZone> GenerateCustomZones(XElement zones = null) {
      zones ??= ConfigManager.Instance.ReadConfigFile(SConfigFileName);
      return zones.Elements("zone").Select(StrongZone.FromXml).ToList();
    }
  }

  public class StrongZone {
    public readonly int MaxX;
    public readonly int MaxZ;
    public readonly int MinX;
    public readonly int MinZ;
    public readonly string Name;
    public readonly List<string> Tags;

    public StrongZone(string name, Vector2i cornerXZ, Vector2i oppositeCornerXZ, List<string> tags = null) {
      Name = name;
      // Sort coordinates to ensure MinX <= MaxX and MinZ <= MaxZ
      MinX = Math.Min(cornerXZ.x, oppositeCornerXZ.x);
      MaxX = Math.Max(cornerXZ.x, oppositeCornerXZ.x);
      MinZ = Math.Min(cornerXZ.y, oppositeCornerXZ.y);
      MaxZ = Math.Max(cornerXZ.y, oppositeCornerXZ.y);
      Tags = tags ?? new List<string>();
      Center = new Vector3(MinX + (MaxX - MinX) / 2f, -1, MinZ + (MaxZ - MinZ) / 2f);
      CornerXZ = new Vector2i(MinX, MinZ);
      OppositeCornerXZ = new Vector2i(MaxX, MaxZ);
      Radius = Vector3.Distance(Center, new Vector3(CornerXZ.x, -1, CornerXZ.y));
      NoReset = Tags.Contains("no_reset");
      NoHostiles = Tags.Contains("no_hostiles");
      StrongSwornOnly = Tags.Contains("strongsworn_only");
    }

    public Vector3 Center { get; }
    public Vector2i CornerXZ { get; }
    public Vector2i OppositeCornerXZ { get; }
    public float Radius { get; }
    public bool NoReset { get; }
    public bool NoHostiles { get; }
    public bool StrongSwornOnly { get; }

    public bool Contains(Vector3 pos) {
      return MinX <= pos.x && MinZ <= pos.z && MaxX >= pos.x && MaxZ >= pos.z;
    }

    public static StrongZone FromXml(XElement zoneElement) {
      var name = zoneElement.Attribute("name")?.Value;
      if (string.IsNullOrEmpty(name)) {
        throw new ArgumentException("Zone element missing name attribute");
      }

      // Parse corner coordinates
      var cornerXZ = zoneElement.Attribute("cornerXZ")?.Value;
      if (string.IsNullOrEmpty(cornerXZ)) {
        throw new ArgumentException("Zone element missing cornerXZ attribute");
      }

      var cornerParts = cornerXZ.Split(',');
      if (cornerParts.Length != 2) {
        throw new ArgumentException("cornerXZ must be in format 'x,z'");
      }

      var corner = new Vector2i(
        int.Parse(cornerParts[0]),
        int.Parse(cornerParts[1])
      );

      // Parse opposite corner coordinates
      var oppositeCornerXZ = zoneElement.Attribute("oppositeCornerXZ")?.Value;
      if (string.IsNullOrEmpty(oppositeCornerXZ)) {
        throw new ArgumentException("Zone element missing oppositeCornerXZ attribute");
      }

      var oppositeCornerParts = oppositeCornerXZ.Split(',');
      if (oppositeCornerParts.Length != 2) {
        throw new ArgumentException("oppositeCornerXZ must be in format 'x,z'");
      }

      var oppositeCorner = new Vector2i(
        int.Parse(oppositeCornerParts[0]),
        int.Parse(oppositeCornerParts[1])
      );

      // Parse tags (comma-separated within the tags element)
      var tags = new List<string>();
      XElement tagsElement = zoneElement.Element("tags");
      if (tagsElement != null && !string.IsNullOrWhiteSpace(tagsElement.Value)) {
        tags = tagsElement.Value
          .Split(',')
          .Select(tag => tag.Trim())
          .Where(tag => !string.IsNullOrEmpty(tag))
          .ToList();
      }

      return new StrongZone(name, corner, oppositeCorner, tags);
    }

    public XElement ToXml() {
      // Create the zone element with the name attribute
      var zoneElement = new XElement("zone",
        new XAttribute("name", Name),
        new XAttribute("cornerXZ", $"{MinX},{MinZ}"),
        new XAttribute("oppositeCornerXZ", $"{MaxX},{MaxZ}")
      );

      // Add tags element if there are any tags
      if (Tags is { Count: > 0 }) {
        var tagsElement = new XElement("tags", string.Join(",", Tags));
        zoneElement.Add(tagsElement);
      }

      return zoneElement;
    }

    public override string ToString() {
      return
        $"StrongZone(name={Name}, cornerXZ={CornerXZ}, oppositeCornerXZ={OppositeCornerXZ}, tags={string.Join(",", Tags)})";
    }
  }

  public static class StrongSwornOnlyEnforcer {
    public static void Init() {
      StrongZones.RegisterPlayerCallbacks(OnPlayerEntered);
    }

    private static void OnPlayerEntered(EntityPlayer player, StrongZone zone) {
      if (!zone.StrongSwornOnly || player.IsStrongSworn()) {
        return;
      }

      player.Buffs.AddBuff("buff_strongsworn_zone_violation");

      if (!TryGetRandomSpawnPositionOutsideZone(zone, out Vector3 newPosition)) {
        Log.Out($"[StrongSwornEnforcer] Could not find random spawn position outside {zone.Name}");
        return;
      }

      Log.Out($"[StrongSwornEnforcer] Teleporting {player.PlayerDisplayName} out of {zone.Name}");
      if (player.isEntityRemote) {
        SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForEntityId(player.entityId)
          .SendPackage(NetPackageManager.GetPackage<NetPackageTeleportPlayer>().Setup(newPosition));
      } else {
        player.Teleport(newPosition);
      }
    }

    private static bool TryGetRandomSpawnPositionOutsideZone(StrongZone zone, out Vector3 position) {
      var minRange = (int)Math.Ceiling(zone.Radius) + 2;
      var maxRange = minRange + 5;
      return GameManager.Instance.World.GetRandomSpawnPositionMinMaxToPosition(zone.Center, minRange, maxRange, 1,
        false, out position);
    }
  }

  public static class NoHostileEnforcer {
    public static void Init() {
      StrongZones.RegisterEnemyCallbacks(OnEnemyEntered);
    }

    private static void OnEnemyEntered(EntityEnemy enemy, StrongZone zone) {
      if (!zone.NoHostiles) {
        return;
      }

      Log.Out($"[NoHostileEnforcer] Despawning enemy {enemy.EntityName} found in {zone.Name}");
      enemy.Despawn();
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

  public static class EntityExtensions {
    private static readonly ConditionalWeakTable<Entity, List<StrongZone>> s_currentZones = new();

    public static List<StrongZone> GetCurrentZones(this Entity entity) {
      // GetOrCreateValue is thread-safe and ensures we always return a valid list
      return s_currentZones.GetOrCreateValue(entity);
    }

    public static void SetCurrentZones(this Entity entity, List<StrongZone> zones) {
      s_currentZones.Remove(entity);
      s_currentZones.Add(entity, zones);
    }
  }

  public static class PrefabExtensions {
    private static readonly ConditionalWeakTable<Prefab, List<string>> s_strongZoneTags = new();

    public static List<string> GetStrongZoneTags(this Prefab prefab) {
      // GetOrCreateValue is thread-safe and ensures we always return a valid list
      return s_strongZoneTags.GetOrCreateValue(prefab);
    }

    public static void SetStrongZoneTags(this Prefab prefab, List<string> newTags) {
      s_strongZoneTags.Remove(prefab);
      s_strongZoneTags.Add(prefab, newTags);
    }
  }
}

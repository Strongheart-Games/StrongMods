using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;

namespace AutoCollectLoot {
  [HarmonyPatch(typeof(EntityClassesFromXml), nameof(EntityClassesFromXml.LoadMain))]
  public static class EntityClassesFromXml_LoadMain_LootCoverageAuditPatch {
    private static void Postfix(XmlFile _xmlFile) {
      LootCoverageAuditRuntime.CaptureDeclarations(_xmlFile.XmlDoc.Root);
    }
  }

  public static class LootCoverageAuditRuntime {
    private const string ModPrefix = "[AutoCollectLoot]";
    private const string Prefix = ModPrefix + " CoverageAudit";
    private static IReadOnlyList<LootContainerDeclaration> s_declarations = Array.Empty<LootContainerDeclaration>();
    private static readonly LootCoverageAuditCoordinator s_coordinator = new(CreateInput, LogReport, LogInconclusive);

    public static void CaptureDeclarations(XElement root) {
      s_declarations = root?.Elements("entity_class").Select(element => new LootContainerDeclaration(
        (string)element.Attribute("name"), (string)element.Attribute("extends"),
        (string)element.Elements("property").FirstOrDefault(property => (string)property.Attribute("name") == "Class")?.Attribute("value"),
        element.Elements("property").Select(property => (string)property.Attribute("name")).Where(name => name is not null))).ToArray()
        ?? Array.Empty<LootContainerDeclaration>();
    }

    public static void OnGameStartDone(ref ModEvents.SGameStartDoneData data) {
      if (!ConnectionManager.Instance.IsClient) {
        s_coordinator.Arm(Time.realtimeSinceStartup);
      }
    }

    public static void OnGameUpdate(ref ModEvents.SGameUpdateData data) {
      if (!ConnectionManager.Instance.IsClient) {
        s_coordinator.Update(Time.realtimeSinceStartup);
      }
    }

    public static void OnGameShutdown(ref ModEvents.SGameShutdownData data) {
      s_coordinator.Reset();
    }

    private static LootCoverageInput CreateInput() {
      if (s_declarations.Count == 0 || EntityClass.list?.Dict is null || ItemClass.nameToItem is null) {
        return null;
      }
      var enemiesByContainer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
      foreach (EntityClass enemy in EntityClass.list.Dict.Values.Where(entity => entity is not null && entity.bIsEnemyEntity)) {
        if (enemy.lootDrops is null) {
          continue;
        }
        foreach (EntityClass.LootDrop drop in enemy.lootDrops) {
          EntityClass container = EntityClass.GetEntityClass(drop.entityClass);
        if (container is null) {
          continue;
        }
        if (!enemiesByContainer.TryGetValue(container.entityClassName, out HashSet<string> enemies)) {
            enemies = new HashSet<string>(StringComparer.Ordinal);
            enemiesByContainer[container.entityClassName] = enemies;
          }
          enemies.Add(enemy.entityClassName);
        }
      }
      var candidates = enemiesByContainer.Select(pair => new LootCoverageCandidate(pair.Key,
        EntityClass.GetEntityClass(EntityClass.FromString(pair.Key)).Properties.GetString("LootList"), pair.Value)).ToArray();
      var substitutes = LootItems.GetSubstituteItems().ToDictionary(pair => pair.Key,
        pair => (IReadOnlyList<LootSubstitute>)pair.Value.Select(item => new LootSubstitute(item.GetItemName(),
          item.Actions[0] is ItemActionOpenLootBundle, (item.Actions[0] as ItemActionOpenLootBundle)?.lootListName)).ToArray(),
        StringComparer.Ordinal);
      return new LootCoverageInput(s_declarations, candidates, substitutes);
    }

    private static void LogReport(LootCoverageReport report) {
      string enemyText = report.AffectedEnemyCount == 1 ? "1 enemy is" : report.AffectedEnemyCount + " enemies are";
      string ending = report.Omissions.Count == 0 ? "all in-scope enemies are fully configured for AutoCollectLoot"
        : enemyText + " not fully configured for AutoCollectLoot";
      Log.Out($"{Prefix}: {report.CandidateCount} containers in scope | {report.ConfiguredCount} configured | {report.Omissions.Count} omitted | {ending}");
      foreach (LootCoverageOmission omission in report.Omissions) {
        string lines = string.Join("\n", new[] { $"{ModPrefix}   Omitted Container: {omission.ContainerName}", $"{ModPrefix}     Reasons:" }
          .Concat(omission.Reasons.Select(reason => $"{ModPrefix}       - {reason}"))
          .Concat(new[] { $"{ModPrefix}     Affected Enemies:" })
          .Concat(omission.EnemyNames.Select(enemy => $"{ModPrefix}       - {enemy}")));
        if (omission.Reasons.All(reason => reason.Token == "excluded-drop-bag-policy")) {
          Log.Out(lines);
        } else {
          Log.Warning(lines);
        }
      }
    }

    private static void LogInconclusive(string observation) {
      Log.Warning($"{Prefix}: Inconclusive\n{ModPrefix}   Reason: startup-state-not-stable (timeout=30s)\n{ModPrefix}   Last Observation: {observation}");
    }
  }

}

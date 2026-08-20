using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoCollectLoot {
  public sealed class LootCoverageAudit {
    public static LootCoverageReport CreateReport(LootCoverageInput input) {
      var declarations = input.Declarations.ToDictionary(declaration => declaration.Name, StringComparer.Ordinal);
      var omissions = new List<LootCoverageOmission>();

      foreach (LootCoverageCandidate candidate in input.Candidates.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)) {
        declarations.TryGetValue(candidate.Name, out LootContainerDeclaration declaration);
        var reasons = FindReasons(candidate, declaration, declarations, input.Substitutes);
        if (reasons.Count != 0) {
          omissions.Add(new LootCoverageOmission(candidate.Name, candidate.EnemyNames, reasons));
        }
      }

      return new LootCoverageReport(input.Candidates.Count, omissions);
    }

    private static List<LootCoverageReason> FindReasons(LootCoverageCandidate candidate,
      LootContainerDeclaration declaration, IReadOnlyDictionary<string, LootContainerDeclaration> declarations,
      IReadOnlyDictionary<string, IReadOnlyList<LootSubstitute>> substitutes) {
      var chain = GetDeclarationChain(declaration, declarations);
      LootContainerDeclaration directContainer = chain.FirstOrDefault(item => item.Class == "EntityLootContainer");
      var reasons = new List<LootCoverageReason>();
      if (candidate.LootList == "cntDropBag") {
        reasons.Add(new LootCoverageReason("excluded-drop-bag-policy"));
      }

      AddRequiredDataReasons(declaration, chain, reasons);
      if (directContainer is null) {
        reasons.Add(new LootCoverageReason("unsupported-drop-entity-class"));
      } else {
        int depth = chain.IndexOf(directContainer);
        if (depth > 1) {
          reasons.Add(new LootCoverageReason("unsupported-inheritance-depth", "depth=" + depth));
        }
      }

      if (IsGeneratorEligible(candidate, declaration, chain, directContainer)) {
        if (!substitutes.TryGetValue(candidate.Name, out IReadOnlyList<LootSubstitute> mapped)) {
          reasons.Add(new LootCoverageReason("missing-substitute-item"));
        } else if (mapped.Count != 1 || !mapped[0].IsOpenLootBundle || mapped[0].LootList != candidate.LootList) {
          reasons.Add(new LootCoverageReason("invalid-substitute-item"));
        }
      }
      return reasons;
    }

    private static List<LootContainerDeclaration> GetDeclarationChain(LootContainerDeclaration declaration,
      IReadOnlyDictionary<string, LootContainerDeclaration> declarations) {
      var chain = new List<LootContainerDeclaration>();
      var seen = new HashSet<string>(StringComparer.Ordinal);
      while (declaration is not null && seen.Add(declaration.Name)) {
        chain.Add(declaration);
        declarations.TryGetValue(declaration.Extends ?? "", out declaration);
      }
      return chain;
    }

    private static void AddRequiredDataReasons(LootContainerDeclaration declaration,
      IReadOnlyCollection<LootContainerDeclaration> chain, ICollection<LootCoverageReason> reasons) {
      var missing = new List<string>();
      var inherited = new List<string>();
      foreach (string property in new[] { "Mesh", "LootList" }) {
        if (!chain.Any(item => item.HasProperty(property))) {
          missing.Add(property);
        } else if (declaration is null || !declaration.HasProperty(property)) {
          inherited.Add(property);
        }
      }
      if (missing.Count != 0) {
        reasons.Add(new LootCoverageReason("missing-required-data", "properties=" + string.Join(",", missing)));
      }
      if (inherited.Count != 0) {
        reasons.Add(new LootCoverageReason("inherited-required-data", "properties=" + string.Join(",", inherited)));
      }
    }

    private static bool IsGeneratorEligible(LootCoverageCandidate candidate, LootContainerDeclaration declaration,
      IReadOnlyList<LootContainerDeclaration> chain, LootContainerDeclaration directContainer) {
      return candidate.LootList != "cntDropBag" && declaration is not null && declaration.HasProperty("Mesh") &&
        declaration.HasProperty("LootList") && directContainer is not null &&
        chain.TakeWhile(item => item != directContainer).Count() <= 1;
    }
  }

  public sealed class LootCoverageInput {
    public LootCoverageInput(IReadOnlyList<LootContainerDeclaration> declarations,
      IReadOnlyList<LootCoverageCandidate> candidates, IReadOnlyDictionary<string, IReadOnlyList<LootSubstitute>> substitutes) {
      Declarations = declarations;
      Candidates = candidates;
      Substitutes = substitutes;
    }

    public IReadOnlyList<LootContainerDeclaration> Declarations { get; }
    public IReadOnlyList<LootCoverageCandidate> Candidates { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LootSubstitute>> Substitutes { get; }

    public string Signature => string.Join("\n", Candidates.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
      .Select(candidate => candidate.Name + "|" + candidate.LootList + "|" + string.Join(",", candidate.EnemyNames.OrderBy(name => name, StringComparer.Ordinal)))
      .Concat(Substitutes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "|" + string.Join(",",
        pair.Value.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => item.Name + ":" + item.IsOpenLootBundle + ":" + item.LootList)))));
  }

  public sealed class LootContainerDeclaration {
    public LootContainerDeclaration(string name, string extends, string @class, IEnumerable<string> properties) {
      Name = name;
      Extends = extends;
      Class = @class;
      Properties = new HashSet<string>(properties, StringComparer.Ordinal);
    }

    public string Name { get; }
    public string Extends { get; }
    public string Class { get; }
    public IReadOnlyCollection<string> Properties { get; }
    public bool HasProperty(string name) => Properties.Contains(name);
  }

  public sealed class LootCoverageCandidate {
    public LootCoverageCandidate(string name, string lootList, IEnumerable<string> enemyNames) {
      Name = name;
      LootList = lootList;
      EnemyNames = enemyNames.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    public string Name { get; }
    public string LootList { get; }
    public IReadOnlyList<string> EnemyNames { get; }
  }

  public sealed class LootSubstitute {
    public LootSubstitute(string name, bool isOpenLootBundle, string lootList) {
      Name = name;
      IsOpenLootBundle = isOpenLootBundle;
      LootList = lootList;
    }

    public string Name { get; }
    public bool IsOpenLootBundle { get; }
    public string LootList { get; }
  }

  public sealed class LootCoverageReason {
    public LootCoverageReason(string token, string evidence = null) {
      Token = token;
      Evidence = evidence;
    }

    public string Token { get; }
    public string Evidence { get; }
    public override string ToString() => Evidence is null ? Token : Token + " (" + Evidence + ")";
  }

  public sealed class LootCoverageOmission {
    public LootCoverageOmission(string containerName, IReadOnlyList<string> enemyNames, IReadOnlyList<LootCoverageReason> reasons) {
      ContainerName = containerName;
      EnemyNames = enemyNames;
      Reasons = reasons;
    }

    public string ContainerName { get; }
    public IReadOnlyList<string> EnemyNames { get; }
    public IReadOnlyList<LootCoverageReason> Reasons { get; }
  }

  public sealed class LootCoverageReport {
    public LootCoverageReport(int candidateCount, IReadOnlyList<LootCoverageOmission> omissions) {
      CandidateCount = candidateCount;
      Omissions = omissions;
    }

    public int CandidateCount { get; }
    public IReadOnlyList<LootCoverageOmission> Omissions { get; }
    public int ConfiguredCount => CandidateCount - Omissions.Count;
    public int AffectedEnemyCount => Omissions.SelectMany(omission => omission.EnemyNames).Distinct(StringComparer.Ordinal).Count();
  }

  public sealed class LootCoverageAuditCoordinator {
    private const float SampleInterval = 0.25f;
    private const float StableFor = 5f;
    private const float Timeout = 30f;
    private readonly Func<LootCoverageInput> inputFactory;
    private readonly Action<LootCoverageReport> reportLogger;
    private readonly Action<string> inconclusiveLogger;
    private bool armed;
    private bool complete;
    private float startedAt;
    private float lastSampleAt;
    private float stableSince;
    private string signature;
    private string observation = "prerequisites unavailable";
    private int stateChanges;

    public LootCoverageAuditCoordinator(Func<LootCoverageInput> inputFactory, Action<LootCoverageReport> reportLogger,
      Action<string> inconclusiveLogger) {
      this.inputFactory = inputFactory;
      this.reportLogger = reportLogger;
      this.inconclusiveLogger = inconclusiveLogger;
    }

    public void Arm(float now) {
      armed = true;
      complete = false;
      startedAt = now;
      lastSampleAt = now - SampleInterval;
      stableSince = now;
      signature = null;
      observation = "prerequisites unavailable";
      stateChanges = 0;
    }

    public void Reset() {
      armed = false;
      complete = false;
      signature = null;
    }

    public void Update(float now) {
      if (!armed || complete) {
        return;
      }
      if (now - startedAt >= Timeout) {
        complete = true;
        inconclusiveLogger(observation);
        return;
      }
      if (now - lastSampleAt < SampleInterval) {
        return;
      }
      lastSampleAt = now;
      LootCoverageInput input = inputFactory();
      string nextSignature = input?.Signature;
      if (nextSignature != signature) {
        if (signature is not null) {
          stateChanges++;
        }
        signature = nextSignature;
        stableSince = now;
      }
      observation = input is null ? "prerequisites unavailable" : input.Candidates.Count + " containers in scope | " +
        input.Substitutes.Sum(pair => pair.Value.Count) + " substitute mappings | " + stateChanges + " relevant state changes";
      if (input is not null && now - stableSince >= StableFor) {
        complete = true;
        reportLogger(LootCoverageAudit.CreateReport(input));
      }
    }
  }
}

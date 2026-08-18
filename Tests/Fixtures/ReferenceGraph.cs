using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Tests.Fixtures;

/// <summary>One cross-document reference: who points, at what, under which rule.</summary>
public sealed record XmlReference(string Rule, string Owner, string Target) {
  public override string ToString() => $"{Rule}: {Owner} -> {Target}";
}

/// <summary>
///   The name graph of the game's config XML — which documents DEFINE names, and which attributes REFERENCE
///   them (#50 gap S3). Nothing here re-implements the game's loaders; each rule is a pairing of an
///   attribute that holds a name with the documents that can define it, and every rule was admitted only
///   after being measured against the declared tree's own vanilla XML.
///   <para>
///     Two conventions are excluded from every rule, because they are not names of anything the config
///     defines: a value containing ':' (vanilla's shape-variant helpers such as
///     <c>woodShapes:VariantHelper</c>) and a value starting with '@' (an asset path). A value may be a
///     comma-separated list, which is split.
///   </para>
/// </summary>
public sealed class ReferenceGraph {
  /// <summary>The documents a name may be defined in, per kind. Order is irrelevant; membership is not.</summary>
  private static readonly (string Kind, (string File, string Element)[] Definitions)[] Universes = {
    ("block", new[] { ("blocks", "block") }),
    ("item", new[] { ("items", "item") }),
    ("buff", new[] { ("buffs", "buff") }),
    ("lootgroup", new[] { ("loot", "lootgroup") }),
    // A recipe's output and a loot entry name each reach three documents: item modifiers are items to a
    // recipe, and blocks are craftable in their own right.
    ("craftable", new[] { ("items", "item"), ("blocks", "block"), ("item_modifiers", "item_modifier") })
  };

  private readonly Dictionary<string, HashSet<string>> universes = new(StringComparer.Ordinal);
  private readonly Dictionary<string, XElement> roots;

  private ReferenceGraph(Dictionary<string, XElement> roots) {
    this.roots = roots;
    foreach ((var kind, (string File, string Element)[] definitions) in Universes) {
      var names = new HashSet<string>(StringComparer.Ordinal);
      foreach ((var file, var element) in definitions) {
        if (roots.TryGetValue(file, out XElement root)) {
          names.UnionWith(root.Descendants(element).Select(e => (string)e.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n)));
        }
      }

      universes[kind] = names;
    }
  }

  /// <summary>The merged documents a <see cref="PatchPipeline" /> produced — vanilla plus every mod.</summary>
  public static ReferenceGraph OfMerged(PatcherHost host, PatchPipeline pipeline) {
    var roots = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
    foreach ((var name, object document) in pipeline.Documents) {
      XElement root = XDocument.Parse(host.XmlOf(document)).Root;
      if (root != null) {
        roots[name] = root;
      }
    }

    return new ReferenceGraph(roots);
  }

  /// <summary>
  ///   The unit's untouched vanilla XML, read straight off disk. This is the BASELINE: vanilla ships
  ///   references that resolve to nothing (measured, ten of them in V3.1.0 b14), and those are the game's,
  ///   not the repo's. Only a dangling reference absent from this baseline is a finding.
  /// </summary>
  public static ReferenceGraph OfVanilla(PatcherHost host, IEnumerable<string> entryPoints) {
    var roots = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in entryPoints) {
      var path = Path.Combine(host.VanillaConfigDir,
        name.Replace('/', Path.DirectorySeparatorChar) + ".xml");
      if (!File.Exists(path)) {
        continue;
      }

      XElement root = XDocument.Load(path).Root;
      if (root != null) {
        roots[name] = root;
      }
    }

    return new ReferenceGraph(roots);
  }

  /// <summary>How many references the rules collected — the guard against a rule silently matching nothing.</summary>
  public int Total => All().Count();

  /// <summary>Every reference this graph holds that resolves to no defined name.</summary>
  public IReadOnlyList<XmlReference> Dangling() {
    var dangling = new List<XmlReference>();
    foreach (XmlReference reference in All()) {
      var kind = reference.Rule.Split(' ')[0];
      if (!universes[kind].Contains(reference.Target)) {
        dangling.Add(reference);
      }
    }

    return dangling;
  }

  /// <summary>
  ///   Every reference, under every rule. The rule name starts with the KIND its target must be, which is
  ///   what <see cref="Dangling" /> resolves against.
  /// </summary>
  private IEnumerable<XmlReference> All() {
    // A block or item extends one of its own kind; the game resolves this chain at load, so a break here
    // is fatal rather than cosmetic.
    foreach (XmlReference r in FromProperty("blocks", "block", "Extends", "block Extends")) yield return r;
    foreach (XmlReference r in FromProperty("items", "item", "Extends", "item Extends")) yield return r;

    // Block-to-block transitions: what a plant grows into, what a damaged block falls back to.
    foreach (XmlReference r in FromProperty("blocks", "block", "Next", "block Next")) yield return r;
    foreach (XmlReference r in FromProperty("blocks", "block", "DowngradeBlock", "block DowngradeBlock")) {
      yield return r;
    }

    // What a recipe makes, and what it consumes.
    foreach (XmlReference r in FromAttribute("recipes", "recipe", "name", "craftable recipe")) yield return r;
    foreach (XmlReference r in FromAttribute("recipes", "ingredient", "name", "craftable ingredient")) {
      yield return r;
    }

    // Loot: the group a slot draws from, and the thing it drops.
    foreach (XmlReference r in FromAttribute("loot", "item", "group", "lootgroup loot")) yield return r;
    foreach (XmlReference r in FromAttribute("loot", "item", "name", "craftable loot")) yield return r;

    // Every buff a triggered effect adds or removes, wherever the effect is declared.
    foreach (var file in new[] { "buffs", "items", "blocks", "progression", "entityclasses" }) {
      foreach (XmlReference r in FromAttribute(file, "triggered_effect", "buff", "buff " + file)) {
        yield return r;
      }
    }
  }

  private IEnumerable<XmlReference> FromProperty(string file, string owner, string property, string rule) {
    if (!roots.TryGetValue(file, out XElement root)) {
      yield break;
    }

    foreach (XElement element in root.Descendants(owner)) {
      var name = (string)element.Attribute("name") ?? "?";
      foreach (XElement p in element.Descendants("property")) {
        if ((string)p.Attribute("name") != property) {
          continue;
        }

        foreach (var target in Targets((string)p.Attribute("value"))) {
          yield return new XmlReference(rule, name, target);
        }
      }
    }
  }

  private IEnumerable<XmlReference> FromAttribute(string file, string element, string attribute, string rule) {
    if (!roots.TryGetValue(file, out XElement root)) {
      yield break;
    }

    foreach (XElement e in root.Descendants(element)) {
      foreach (var target in Targets((string)e.Attribute(attribute))) {
        yield return new XmlReference(rule, NearestName(e), target);
      }
    }
  }

  private static IEnumerable<string> Targets(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      yield break;
    }

    foreach (var part in value.Split(',')) {
      var target = part.Trim();
      if (target.Length > 0 && !target.Contains(':') && !target.StartsWith("@")) {
        yield return target;
      }
    }
  }

  /// <summary>The nearest enclosing element that has a name — who to blame in the failure message.</summary>
  private static string NearestName(XElement element) {
    for (XElement e = element; e != null; e = e.Parent) {
      var name = (string)e.Attribute("name");
      if (!string.IsNullOrEmpty(name)) {
        return name;
      }
    }

    return "?";
  }
}

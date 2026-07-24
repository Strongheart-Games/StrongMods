using System.Collections.Generic;
using System.Xml.Linq;

namespace StrongMods {
  /// <summary>A single valid &lt;Mod&gt; declaration inside a &lt;Dependencies&gt; block.</summary>
  public sealed class ModDependency {
    public ModDependency(string name, VersionConstraint constraint, bool optional) {
      Name = name;
      Constraint = constraint;
      Optional = optional;
    }

    public string Name { get; }
    public VersionConstraint Constraint { get; } // null means any version satisfies
    public bool Optional { get; }
  }

  /// <summary>
  ///   The parsed &lt;Dependencies&gt; block of a ModInfo v2 document (spec sections 2-3). Authoring errors (section 6)
  ///   are collected rather than thrown: each error message is attributed to the declaring mod by the evaluator and
  ///   renders the block unsatisfied, because silently passing a constraint that could not be parsed is prohibited.
  ///   Unknown attributes and unknown child elements are ignored for forward compatibility (section 7).
  /// </summary>
  public sealed class ModDependencies {
    public VersionConstraint GameConstraint { get; private set; }
    public List<ModDependency> Mods { get; } = new();
    public List<string> AuthoringErrors { get; } = new();

    /// <summary>
    ///   Parses the &lt;Dependencies&gt; child of <paramref name="modInfoRoot" /> (a ModInfo v2 root element).
    ///   Returns null when no &lt;Dependencies&gt; element is present — such a mod declares no constraints.
    /// </summary>
    public static ModDependencies Parse(XElement modInfoRoot) {
      List<XElement> dependencyBlocks = new();
      foreach (XElement child in modInfoRoot.Elements("Dependencies")) {
        dependencyBlocks.Add(child);
      }

      if (dependencyBlocks.Count == 0) {
        return null;
      }

      ModDependencies result = new();
      if (dependencyBlocks.Count > 1) {
        result.AuthoringErrors.Add(
          $"declares {dependencyBlocks.Count} <Dependencies> elements; at most one is allowed");
      }

      var seenGame = false;
      HashSet<string> seenNames = new();
      foreach (XElement element in dependencyBlocks[0].Elements()) {
        switch (element.Name.LocalName) {
          case "Game":
            result.ParseGame(element, ref seenGame);
            break;
          case "Mod":
            result.ParseMod(element, seenNames);
            break;
          // Unknown child elements are reserved for future versions of the specification
        }
      }

      return result;
    }

    private void ParseGame(XElement element, ref bool seenGame) {
      if (seenGame) {
        AuthoringErrors.Add("declares more than one <Game> dependency; at most one is allowed");
        return;
      }

      seenGame = true;
      var versionText = element.Attribute("version")?.Value;
      if (versionText is null) {
        AuthoringErrors.Add("declares a <Game> dependency without the required version attribute");
        return;
      }

      if (!VersionConstraint.TryParse(versionText, out VersionConstraint constraint, out var error)) {
        AuthoringErrors.Add($"declares a malformed <Game> version constraint: {error}");
        return;
      }

      GameConstraint = constraint;
    }

    private void ParseMod(XElement element, HashSet<string> seenNames) {
      var name = element.Attribute("name")?.Value;
      if (string.IsNullOrEmpty(name)) {
        AuthoringErrors.Add("declares a <Mod> dependency without the required name attribute");
        return;
      }

      if (!seenNames.Add(name)) {
        AuthoringErrors.Add($"declares a dependency on '{name}' more than once");
        return;
      }

      VersionConstraint constraint = null;
      var versionText = element.Attribute("version")?.Value;
      if (versionText != null &&
          !VersionConstraint.TryParse(versionText, out constraint, out var error)) {
        AuthoringErrors.Add($"declares a malformed version constraint for '{name}': {error}");
        return;
      }

      var optional = false;
      var optionalText = element.Attribute("optional")?.Value;
      if (optionalText != null && !bool.TryParse(optionalText, out optional)) {
        AuthoringErrors.Add($"declares an invalid optional value '{optionalText}' for '{name}'; use true or false");
        return;
      }

      Mods.Add(new ModDependency(name, constraint, optional));
    }
  }
}

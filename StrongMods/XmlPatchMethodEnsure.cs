using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace StrongMods {
  /// <summary>
  ///   The <c>&lt;ensure&gt;</c> XML patch command. Registered with the game's patcher by <see cref="ModApi" />
  ///   at mod init and called by <c>XmlPatcher.singlePatch</c> once per <c>&lt;ensure&gt;</c> element in a mod's
  ///   Config file; never called directly.
  ///   <c>&lt;ensure xpath="..."&gt;</c> makes each of its template children exist, with the given attributes, on
  ///   every node <c>xpath</c> matches — adding the child where it is absent and merging attributes into it where
  ///   it is present. That replaces the paired <c>setattribute</c>+<c>append</c> idiom, in which one command
  ///   always misses and logs "did not apply" even though the patch did exactly what its author intended.
  ///   A template child is identified among its siblings by tag name plus its <c>name</c> and/or <c>class</c>
  ///   attributes, or by the attributes a reserved <c>ensure-key</c> names (stripped before the element is
  ///   written). Ambiguity is never resolved by guessing: a parent holding two children that match the key is
  ///   warned about and left alone. Ensuring never removes an element, and applying the same block twice leaves
  ///   the document exactly as applying it once did.
  ///   <c>StrongMods/Docs/ensure.md</c> is the specification; <c>Tests/Ensure/</c> asserts it clause by clause.
  /// </summary>
  public static class XmlPatchMethodEnsure {
    /// <summary>
    ///   Reserved attribute naming the attributes that identify a template child among its siblings, as a
    ///   comma-separated list. Removed from the element before it is written into the target document.
    /// </summary>
    private const string KeyAttribute = "ensure-key";

    private const string PositionAttribute = "position";
    private const string AppendPosition = "append";
    private const string PrependPosition = "prepend";

    public static int Ensure(
      XmlFile targetFile,
      string xpath,
      XElement patchSourceElement,
      XmlFile patchFile,
      Mod patchingMod = null) {
      var context = DescribeContext(patchSourceElement, patchFile, patchingMod);

      if (!TryResolvePosition(context, patchSourceElement, out var prepend)) {
        return 0;
      }

      // Templates and their keys resolve before the xpath runs, so a malformed block is reported even when
      // nothing matches — it would fail identically for every parent, which is what makes it an error rather
      // than a per-parent warning.
      var templates = new List<Template>();
      foreach (XElement child in patchSourceElement.Elements()) {
        if (!TryResolveTemplate(context, child, out Template template)) {
          return 0;
        }

        templates.Add(template);
      }

      if (templates.Count == 0) {
        Log.Error($"{context}: ensure has no template children, so there is nothing to ensure.");
        return 0;
      }

      if (!targetFile.GetXpathResults(xpath, out List<XObject> matchList)) {
        return 0; // No parents matched; vanilla logs the one "did not apply" warning this earns.
      }

      // Snapshot and release before mutating: the results list is a field on the XmlFile we are about to edit.
      var matches = new List<XObject>(matchList);
      targetFile.ClearXpathResults();

      var applied = 0;
      foreach (XObject match in matches) {
        if (match is not XElement parent) {
          Log.Warning($"{context}: xpath matched {DescribeMatch(match)}, which is not an element and so cannot " +
                      "hold children; skipped.");
          continue;
        }

        EnsureChildren(context, parent, templates, prepend);
        applied++;
      }

      return applied;
    }

    /// <summary>
    ///   Applies one level of templates to one parent. Recursion happens through <see cref="Merge" />: a
    ///   template that was cloned in whole already carries its children, so only a merge into an element that
    ///   already existed has to descend.
    /// </summary>
    private static void EnsureChildren(
      string context, XElement parent, IReadOnlyList<Template> templates, bool prepend) {
      // Prepending each new child at the front in turn would reverse them, so successive insertions chain off
      // the previous one and document order is preserved.
      XElement anchor = null;
      foreach (Template template in templates) {
        List<XElement> existing = MatchingChildren(parent, template);
        if (existing.Count > 1) {
          Log.Warning($"{context}: {DescribeElement(parent)} holds {existing.Count} children matching " +
                      $"<{template.Source.Name.LocalName} {DescribeKey(template)}>, so which one to update is " +
                      $"ambiguous; left alone. Identify one with {KeyAttribute}=\"...\".");
          continue;
        }

        if (existing.Count == 1) {
          Merge(context, existing[0], template, prepend);
          continue;
        }

        XElement created = CleanClone(template);
        if (!prepend) {
          parent.Add(created);
        } else if (anchor == null) {
          parent.AddFirst(created);
        } else {
          anchor.AddAfterSelf(created);
        }

        anchor = created;
      }
    }

    /// <summary>
    ///   Brings an element that already exists up to what the template declares: attributes the template names
    ///   are set, attributes it does not name are left alone, and the element stays where it is.
    /// </summary>
    private static void Merge(string context, XElement existing, Template template, bool prepend) {
      foreach (XAttribute attribute in template.Source.Attributes()) {
        if (attribute.Name.LocalName != KeyAttribute) {
          existing.SetAttributeValue(attribute.Name, attribute.Value);
        }
      }

      if (template.Children.Count > 0) {
        EnsureChildren(context, existing, template.Children, prepend);
        return;
      }

      if (template.Text == null) {
        return;
      }

      // Replace text without disturbing elements: ensure never removes one.
      foreach (XText stale in existing.Nodes().OfType<XText>().ToList()) {
        stale.Remove();
      }

      existing.Add(new XText(template.Text));
    }

    /// <summary>
    ///   The template as it should appear in the target document: <see cref="KeyAttribute" /> stripped
    ///   throughout, and leaf text normalized to exactly what a merge would set, so creating and then merging
    ///   agree and a block stays idempotent.
    /// </summary>
    private static XElement CleanClone(Template template) {
      var clone = new XElement(template.Source);
      foreach (XAttribute key in clone.DescendantsAndSelf().Attributes(KeyAttribute).ToList()) {
        key.Remove();
      }

      if (template.Text != null) {
        clone.Value = template.Text;
      }

      return clone;
    }

    private static List<XElement> MatchingChildren(XElement parent, Template template) {
      var matches = new List<XElement>();
      foreach (XElement candidate in parent.Elements(template.Source.Name)) {
        if (template.KeyAttributes.All(
              name => (string)candidate.Attribute(name) == (string)template.Source.Attribute(name))) {
          matches.Add(candidate);
        }
      }

      return matches;
    }

    private static bool TryResolveTemplate(string context, XElement source, out Template template) {
      template = null;
      if (!TryResolveKeyAttributes(context, source, out List<string> keyAttributes)) {
        return false;
      }

      var children = new List<Template>();
      foreach (XElement child in source.Elements()) {
        if (!TryResolveTemplate(context, child, out Template resolved)) {
          return false;
        }

        children.Add(resolved);
      }

      template = new Template(source, keyAttributes, children);
      return true;
    }

    private static bool TryResolveKeyAttributes(string context, XElement source, out List<string> keyAttributes) {
      keyAttributes = null;
      XAttribute declared = source.Attribute(KeyAttribute);
      if (declared == null) {
        // Default identity: whichever of name and class the template carries, both when it carries both.
        var defaults = new List<string>(2);
        if (source.Attribute("name") != null) {
          defaults.Add("name");
        }

        if (source.Attribute("class") != null) {
          defaults.Add("class");
        }

        if (defaults.Count == 0) {
          Log.Error($"{context}: <{source.Name.LocalName}> has neither a name nor a class attribute, so it " +
                    $"cannot be identified among its siblings; add {KeyAttribute}=\"...\" naming the attributes " +
                    "that identify it.");
          return false;
        }

        keyAttributes = defaults;
        return true;
      }

      var names = declared.Value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToList();
      if (names.Count == 0) {
        Log.Error($"{context}: <{source.Name.LocalName}> has an empty {KeyAttribute}; name at least one " +
                  "attribute, or drop it to identify the element by name or class.");
        return false;
      }

      List<string> absent = names.Where(name => source.Attribute(name) == null).ToList();
      if (absent.Count > 0) {
        Log.Error($"{context}: <{source.Name.LocalName}> declares {KeyAttribute}=\"{declared.Value}\" but " +
                  $"carries no {string.Join(", ", absent)} attribute, so that key cannot be evaluated.");
        return false;
      }

      keyAttributes = names;
      return true;
    }

    private static bool TryResolvePosition(string context, XElement patchSourceElement, out bool prepend) {
      prepend = false;
      XAttribute declared = patchSourceElement.Attribute(PositionAttribute);
      if (declared == null) {
        return true;
      }

      switch (declared.Value) {
        case AppendPosition:
          return true;
        case PrependPosition:
          prepend = true;
          return true;
        default:
          Log.Error($"{context}: {PositionAttribute}=\"{declared.Value}\" is not a valid position; use " +
                    $"\"{AppendPosition}\" (the default) or \"{PrependPosition}\".");
          return false;
      }
    }

    private static string DescribeContext(XElement element, XmlFile patchFile, Mod mod) {
      var modName = mod?.Name ?? "unknown mod";
      var fileName = patchFile?.Filename ?? "unknown file";
      var lineInfo = (IXmlLineInfo)element;
      var line = lineInfo != null && lineInfo.HasLineInfo()
        ? $", line {lineInfo.LineNumber}"
        : string.Empty;
      return $"XML patch ensure ({modName}, {fileName}{line})";
    }

    private static string DescribeElement(XElement element) {
      XAttribute identifier = element.Attribute("name") ?? element.Attribute("class");
      return identifier != null
        ? $"<{element.Name.LocalName} {identifier.Name.LocalName}=\"{identifier.Value}\">"
        : $"<{element.Name.LocalName}>";
    }

    private static string DescribeKey(Template template) {
      return string.Join(" ", template.KeyAttributes
        .Select(name => $"{name}=\"{(string)template.Source.Attribute(name)}\""));
    }

    private static string DescribeMatch(XObject match) {
      return match is XAttribute attribute
        ? $"@{attribute.Name.LocalName}=\"{attribute.Value}\""
        : match.NodeType.ToString();
    }

    /// <summary>
    ///   One template child with everything that would otherwise be recomputed per parent resolved once: which
    ///   attributes identify it, what text it sets, and the same for its own children.
    /// </summary>
    private sealed class Template {
      internal Template(XElement source, List<string> keyAttributes, List<Template> children) {
        Source = source;
        KeyAttributes = keyAttributes;
        Children = children;
        Text = ResolveText(source);
      }

      internal XElement Source { get; }
      internal IReadOnlyList<string> KeyAttributes { get; }
      internal IReadOnlyList<Template> Children { get; }

      /// <summary>The text this template sets, or null when it declares none — see Docs/ensure.md §Text.</summary>
      internal string Text { get; }

      /// <summary>
      ///   Only a childless template can carry text: whitespace between child elements is formatting, present
      ///   throughout the game's config, and must never be mistaken for content.
      /// </summary>
      private static string ResolveText(XElement source) {
        if (source.HasElements) {
          return null;
        }

        var text = string.Concat(source.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
        return text.Length == 0 ? null : text;
      }
    }
  }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace StrongMods {
  /// <summary>
  ///   Custom XML patch commands for the Stronghold breadth-first patcher.
  ///   <c>&lt;foreach source="..." xpath="..." as="name"&gt;</c> iterates the nodes matched by
  ///   <c>xpath</c> in <c>source</c> (default: the patch's own target document) in document order,
  ///   binding each to <c>name</c>, and applies the body commands once per node. Body text,
  ///   attribute values, and command xpaths may interpolate <c>{name}</c> (string-value of the
  ///   bound node) or <c>{name/relative/xpath}</c> (XPath 1.0, evaluated with the bound node as
  ///   context, must resolve to exactly one node). <c>{{</c> and <c>}}</c> are literal braces.
  ///   A resolution that matches zero or multiple nodes skips the current iteration with a
  ///   warning; unbound names, malformed xpaths, bad attributes, and unknown sources fail the
  ///   whole foreach with an error, matching how vanilla drops a bad command.
  ///   Because XML forbids '{' in tag names, dynamic element names use the reserved
  ///   <see cref="DynamicNameAttribute" /> attribute instead; see its doc comment.
  /// </summary>
  [XmlPatchMethodsClass]
  public static class XmlPatchMethodForeach {
    /// <summary>
    ///   XML cannot represent '{' in a tag name, so dynamic element names are expressed with this
    ///   reserved attribute: <c>&lt;placeholder foreach-name="item_{loot/@name}"/&gt;</c>. After
    ///   substitution, the attribute's value replaces the element name and the attribute itself is
    ///   dropped from the output. A substituted name that is not a valid XML name skips the
    ///   current iteration.
    /// </summary>
    private const string DynamicNameAttribute = "foreach-name";

    private const string ForeachName = "foreach";
    private const int MaxDepth = 16;

    private static readonly Regex s_bindingNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Active bindings, outermost first. World XML patching is single threaded at load; if that
    // ever changes, this must become [ThreadStatic].
    private static readonly List<Binding> s_scope = new();

    [XmlPatchMethod("foreach")]
    public static int Foreach(
      XmlFile targetFile,
      string xpath,
      XElement patchSourceElement,
      XmlFile patchFile,
      Mod patchingMod = null) {
      var context = DescribeContext(patchSourceElement, patchFile, patchingMod);

      if (s_scope.Count >= MaxDepth) {
        Log.Error($"{context}: foreach nesting exceeds {MaxDepth} levels; aborting.");
        return 0;
      }

      // Our own attribute values may still contain '{{'/'}}' escapes and, when this is a nested
      // foreach, references to enclosing bindings; both are handled by a normal substitution
      // pass against the current scope.
      if (!TrySubstituteOwnAttribute(context, "xpath", xpath ?? string.Empty, out xpath)) {
        return 0;
      }

      if (xpath.Length == 0) {
        Log.Error($"{context}: missing or empty xpath attribute.");
        return 0;
      }

      XAttribute asAttribute = patchSourceElement.Attribute("as");
      if (asAttribute == null) {
        Log.Error($"{context}: missing required as attribute.");
        return 0;
      }

      if (!TrySubstituteOwnAttribute(context, "as", asAttribute.Value, out var asName)) {
        return 0;
      }

      if (!s_bindingNameRegex.IsMatch(asName)) {
        Log.Error(
          $"{context}: as=\"{asName}\" is not a valid binding name ([A-Za-z_][A-Za-z0-9_]*).");
        return 0;
      }

      foreach (Binding t in s_scope) {
        if (t.Name == asName) {
          Log.Error($"{context}: as=\"{asName}\" shadows an enclosing foreach binding.");
          return 0;
        }
      }

      XmlFile sourceFile = targetFile;
      XAttribute sourceAttribute = patchSourceElement.Attribute("source");
      if (sourceAttribute != null) {
        if (!TrySubstituteOwnAttribute(context, "source", sourceAttribute.Value, out var sourceName)) {
          return 0;
        }

        if (sourceName.Length == 0) {
          Log.Error($"{context}: empty source attribute.");
          return 0;
        }

        if (!BreadthFirstXmlPatcher.TryGetPatchedFile(sourceName, out sourceFile)) {
          Log.Error($"{context}: unknown source document \"{sourceName}\".");
          return 0;
        }
      }

      if (!sourceFile.GetXpathResults(xpath, out List<XObject> matchList)) {
        return 0;
      }

      // Snapshot the results and release the cache immediately: body commands dispatched below run
      // their own GetXpathResults/ClearXpathResults cycles against this same XmlFile whenever
      // source is omitted, and the body may also mutate the source document when source == target.
      var matches = new List<XObject>(matchList);
      sourceFile.ClearXpathResults();

      var body = patchSourceElement.Elements().ToList();
      if (body.Count == 0) {
        Log.Warning($"{context}: foreach has an empty body.");
        return 0;
      }

      if (matches.Count == 0) {
        Log.Out($"{context}: xpath \"{xpath}\" matched no nodes.");
        return 0;
      }

      var applied = 0;
      for (var i = 0; i < matches.Count; i++) {
        XObject match = matches[i];
        s_scope.Add(new Binding(asName, match));
        try {
          // Materialize every non-foreach command up front so a resolution failure skips the
          // iteration atomically (nothing is applied for this node). Nested foreach commands
          // are dispatched as-is: their bodies resolve against the live scope stack — which
          // includes this binding — when they themselves execute.
          var commands = new List<XElement>(body.Count);
          SubstitutionFailure failure = default;
          var failed = false;
          foreach (XElement command in body) {
            if (IsForeach(command)) {
              commands.Add(command);
              continue;
            }

            if (!TryCloneWithSubstitution(command, out XElement clone, out failure)) {
              failed = true;
              break;
            }

            commands.Add(clone);
          }

          if (failed) {
            if (failure.Fatal) {
              Log.Error($"{context}: {failure.Message}; aborting foreach.");
              return applied;
            }

            Log.Warning($"{context}: iteration {i + 1} of {matches.Count} " +
                        $"({DescribeMatch(match)}) skipped — {failure.Message}.");
            continue;
          }

          // singlePatch dispatches synchronously, so a nested foreach routed back through it still
          // sees this binding on the scope stack. It throws on XPath/XML errors in a materialized
          // command; contain those so a bad iteration aborts this foreach, not the whole patch file.
          foreach (XElement command in commands) {
            try {
              if (XmlPatcher.singlePatch(targetFile, command, patchFile, patchingMod)) {
                applied++;
              } else {
                Log.Warning($"{context}: iteration {i + 1} of {matches.Count} ({DescribeMatch(match)}): " +
                            $"<{command.Name.LocalName}> did not apply.");
              }
            } catch (Exception e) when (e is XPathException or XmlException) {
              Log.Error($"{context}: iteration {i + 1} of {matches.Count} ({DescribeMatch(match)}): " +
                        $"<{command.Name.LocalName}> failed: {e.Message}; aborting foreach.");
              return applied;
            }
          }
        } finally {
          s_scope.RemoveAt(s_scope.Count - 1);
        }
      }

      return applied;
    }

    /// <summary>
    ///   Deep-clones a body command, applying interpolation to attribute values and text nodes,
    ///   and applying <see cref="DynamicNameAttribute" /> renames. Nested foreach elements are
    ///   copied verbatim so their bodies are resolved later, against the scope in effect when
    ///   they run.
    /// </summary>
    private static bool TryCloneWithSubstitution(XElement source, out XElement clone, out SubstitutionFailure failure) {
      clone = null;
      failure = default;

      var name = source.Name.LocalName;
      XAttribute dynamicName = source.Attribute(DynamicNameAttribute);
      if (dynamicName != null) {
        if (!TrySubstitute(dynamicName.Value, out name, out failure)) {
          return false;
        }

        try {
          XmlConvert.VerifyName(name);
        } catch (XmlException) {
          failure = SubstitutionFailure.SkipIteration(
            $"{DynamicNameAttribute}=\"{dynamicName.Value}\" produced invalid element name " +
            $"\"{name}\"");
          return false;
        }
      }

      clone = new XElement(name);
      foreach (XAttribute attribute in source.Attributes()) {
        if (ReferenceEquals(attribute, dynamicName)) {
          continue;
        }

        if (!TrySubstitute(attribute.Value, out var value, out failure)) {
          return false;
        }

        clone.SetAttributeValue(attribute.Name, value);
      }

      foreach (XNode node in source.Nodes()) {
        switch (node) {
          case XElement element when IsForeach(element):
            clone.Add(new XElement(element));
            break;
          case XElement element: {
            if (!TryCloneWithSubstitution(element, out XElement childClone, out failure)) {
              return false;
            }

            clone.Add(childClone);
            break;
          }
          case XCData cdata: {
            // Must precede XText: XCData derives from it.
            if (!TrySubstitute(cdata.Value, out var text, out failure)) {
              return false;
            }

            clone.Add(new XCData(text));
            break;
          }
          case XText text: {
            if (!TrySubstitute(text.Value, out var value, out failure)) {
              return false;
            }

            clone.Add(new XText(value));
            break;
          }
          case XComment comment:
            clone.Add(new XComment(comment.Value));
            break;
          case XProcessingInstruction pi:
            clone.Add(new XProcessingInstruction(pi.Target, pi.Data));
            break;
        }
      }

      return true;
    }

    /// <summary>
    ///   Expands '{{', '}}', and {binding[/relative/xpath]} expressions in a string. A stray
    ///   unescaped '}' is treated literally for leniency; an unterminated '{' is a fatal error.
    /// </summary>
    private static bool TrySubstitute(string input, out string result, out SubstitutionFailure failure) {
      result = null;
      failure = default;
      if (input.IndexOf('{') < 0 && input.IndexOf('}') < 0) {
        result = input;
        return true;
      }

      var builder = new StringBuilder(input.Length);
      for (var i = 0; i < input.Length; i++) {
        var c = input[i];
        if (c == '{') {
          if (i + 1 < input.Length && input[i + 1] == '{') {
            builder.Append('{');
            i++;
            continue;
          }

          var close = input.IndexOf('}', i + 1);
          if (close < 0) {
            failure = SubstitutionFailure.Fail($"unterminated '{{' in \"{input}\"");
            return false;
          }

          var expression = input.Substring(i + 1, close - i - 1);
          if (!TryResolveExpression(expression, out var value, out failure)) {
            return false;
          }

          builder.Append(value);
          i = close;
        } else if (c == '}') {
          if (i + 1 < input.Length && input[i + 1] == '}') {
            builder.Append('}');
            i++;
            continue;
          }

          builder.Append('}');
        } else {
          builder.Append(c);
        }
      }

      result = builder.ToString();
      return true;
    }

    /// <summary>
    ///   Resolves "binding" or "binding/relative/xpath" against the scope stack. Node-set results
    ///   must contain exactly one node (zero or multiple skips the iteration); scalar XPath
    ///   results (string(), count(), boolean tests) are accepted as-is.
    /// </summary>
    private static bool TryResolveExpression(string expression, out string value, out SubstitutionFailure failure) {
      value = null;
      failure = default;

      var slash = expression.IndexOf('/');
      var name = slash < 0 ? expression : expression.Substring(0, slash);
      var relativePath = slash < 0 ? null : expression.Substring(slash + 1);

      Binding binding = null;
      for (var i = s_scope.Count - 1; i >= 0; i--) {
        if (s_scope[i].Name == name) {
          binding = s_scope[i];
          break;
        }
      }

      if (binding == null) {
        failure = SubstitutionFailure.Fail(
          $"{{{expression}}} references unbound name \"{name}\"");
        return false;
      }

      if (relativePath == null) {
        value = StringValue(binding.Node);
        return true;
      }

      if (relativePath.Length == 0) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} has an empty relative path");
        return false;
      }

      if (!(binding.Node is XElement element)) {
        failure = SubstitutionFailure.SkipIteration(
          $"{{{expression}}} matched 0 nodes, expected 1 (binding \"{name}\" is not an element)");
        return false;
      }

      object result;
      try {
        result = element.XPathEvaluate(relativePath);
      } catch (XPathException e) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} has a malformed relative path: {e.Message}");
        return false;
      }

      switch (result) {
        case string text:
          value = text;
          return true;
        case double number:
          value = FormatXPathNumber(number);
          return true;
        case bool flag:
          value = flag ? "true" : "false";
          return true;
        case IEnumerable nodes: {
          var list = nodes.Cast<XObject>().ToList();
          if (list.Count != 1) {
            failure = SubstitutionFailure.SkipIteration($"{{{expression}}} matched {list.Count} nodes, expected 1");
            return false;
          }

          value = StringValue(list[0]);
          return true;
        }
        default:
          failure = SubstitutionFailure.Fail(
            $"{{{expression}}} evaluated to unsupported type {result.GetType().Name}");
          return false;
      }
    }

    /// <summary>Substitutes one of foreach's own attributes, logging on failure.</summary>
    private static bool TrySubstituteOwnAttribute(string context, string attributeName, string raw, out string result) {
      if (TrySubstitute(raw, out result, out SubstitutionFailure failure)) {
        return true;
      }

      if (failure.Fatal) {
        Log.Error($"{context}: bad {attributeName} attribute: {failure.Message}.");
      } else {
        Log.Warning($"{context}: skipped — {attributeName} attribute: {failure.Message}.");
      }

      return false;
    }

    /// <summary>XPath 1.0 string-value of a node.</summary>
    private static string StringValue(XObject node) {
      switch (node) {
        case XElement element:
          return element.Value;
        case XAttribute attribute:
          return attribute.Value;
        case XText text:
          return text.Value;
        case XComment comment:
          return comment.Value;
        case XProcessingInstruction pi:
          return pi.Data;
        case XDocument document:
          return document.Root?.Value ?? string.Empty;
        default:
          return node?.ToString() ?? string.Empty;
      }
    }

    /// <summary>XPath 1.0 number-to-string: integral values render without a decimal point.</summary>
    private static string FormatXPathNumber(double number) {
      if (double.IsNaN(number)) {
        return "NaN";
      }

      if (double.IsPositiveInfinity(number)) {
        return "Infinity";
      }

      if (double.IsNegativeInfinity(number)) {
        return "-Infinity";
      }

      if (number == Math.Floor(number) && Math.Abs(number) < 1e15) {
        return ((long)number).ToString(CultureInfo.InvariantCulture);
      }

      return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool IsForeach(XElement element) {
      return element.Name.LocalName == ForeachName;
    }

    private static string DescribeContext(XElement element, XmlFile patchFile, Mod mod) {
      var modName = mod?.Name ?? "unknown mod";
      var fileName = patchFile?.Filename ?? "unknown file";
      var lineInfo = (IXmlLineInfo)element;
      var line = lineInfo != null && lineInfo.HasLineInfo()
        ? $", line {lineInfo.LineNumber}"
        : string.Empty;
      return $"XML patch foreach ({modName}, {fileName}{line})";
    }

    private static string DescribeMatch(XObject match) {
      switch (match) {
        case XElement element: {
          var name = (string)element.Attribute("name");
          return name != null
            ? $"<{element.Name.LocalName} name=\"{name}\">"
            : $"<{element.Name.LocalName}>";
        }
        case XAttribute attribute:
          return $"@{attribute.Name.LocalName}=\"{attribute.Value}\"";
        default:
          return match.NodeType.ToString();
      }
    }

    /// <summary>One active foreach binding: a name and the node it is bound to.</summary>
    private sealed class Binding {
      public readonly string Name;
      public readonly XObject Node;

      public Binding(string name, XObject node) {
        Name = name;
        Node = node;
      }
    }

    /// <summary>
    ///   A failed substitution. Fatal failures (unbound names, malformed xpaths) abort the whole
    ///   foreach; non-fatal failures (zero/multiple matches, invalid dynamic names) skip the
    ///   current iteration with a warning.
    /// </summary>
    private readonly struct SubstitutionFailure {
      public readonly bool Fatal;
      public readonly string Message;

      private SubstitutionFailure(bool fatal, string message) {
        Fatal = fatal;
        Message = message;
      }

      public static SubstitutionFailure Fail(string message) {
        return new SubstitutionFailure(true, message);
      }

      public static SubstitutionFailure SkipIteration(string message) {
        return new SubstitutionFailure(false, message);
      }
    }
  }
}

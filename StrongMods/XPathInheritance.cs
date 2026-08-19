using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace StrongMods {
  /// <summary>
  ///   Resolves 7 Days to Die's XML inheritance from XPath extension functions. See
  ///   <c>Docs/inheritance.md</c> for the author-facing contract.
  /// </summary>
  public static class XPathInheritance {
    public const string Category = "XPathInheritance";
    public const string FunctionPrefix = "sm:";
    internal const int MaxChainDepth = 64;

    [ThreadStatic]
    private static EvaluationScope s_currentScope;

    /// <summary>True when an XPath might use this feature's extension-function prefix.</summary>
    public static bool HasFunctionMarker(string xpath) => xpath?.IndexOf(FunctionPrefix, StringComparison.Ordinal) >= 0;

    /// <summary>
    ///   Evaluates a marked patch XPath and translates its node-set result back to LINQ-to-XML objects. This is the
    ///   public engine seam used by the Harmony adapter and by end-to-end test fixtures.
    /// </summary>
    public static bool TryGetMatches(XDocument document, string xpath, List<XObject> matches) {
      matches.Clear();
      try {
        using (BeginEvaluation(Log.Warning)) {
          var expression = XPathExpression.Compile(xpath);
          expression.SetContext(new XPathInheritanceContext());
          object result = document.CreateNavigator().Evaluate(expression);
          if (!(result is XPathNodeIterator iterator)) {
            Log.Error($"[StrongMods] XPath inheritance: \"{xpath}\" returned a {result?.GetType().Name ?? "null"}, " +
                      "but an XML patch xpath must return nodes.");
            return false;
          }

          while (iterator.MoveNext()) {
            if (iterator.Current?.UnderlyingObject is XObject node) {
              matches.Add(node);
            }
          }

          return matches.Count > 0;
        }
      } catch (Exception e) when (e is XPathException or ArgumentException) {
        Log.Error($"[StrongMods] XPath inheritance: \"{xpath}\" failed to evaluate: {e.Message}");
        return false;
      }
    }

    internal static IDisposable BeginEvaluation(Action<string> warning) {
      var previous = s_currentScope;
      s_currentScope = new EvaluationScope(previous, warning);
      return s_currentScope;
    }

    internal static object SnapshotNodeSet(object result) {
      if (!(result is XPathNodeIterator iterator)) {
        return result;
      }

      var nodes = new List<XPathNavigator>();
      while (iterator.MoveNext()) {
        nodes.Add(iterator.Current.Clone());
      }

      return new XPathInheritanceIterator(nodes);
    }

    internal static IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes) {
      if (!string.Equals(prefix, "sm", StringComparison.Ordinal)) {
        return null;
      }

      return name switch {
        "chain" => new InheritanceFunction(InheritanceOperation.Chain),
        "inherited" => new InheritanceFunction(InheritanceOperation.Inherited),
        _ => throw new XPathException($"unknown XPath inheritance function \"{prefix}:{name}()\"")
      };
    }

    private sealed class EvaluationScope : IDisposable {
      private readonly EvaluationScope previous;
      private readonly Dictionary<IndexKey, KeyIndex> indexes = new();
      private readonly Dictionary<string, XPathExpression> expressions = new();
      private readonly Action<string> warning;

      public EvaluationScope(EvaluationScope previous, Action<string> warning) {
        this.previous = previous;
        this.warning = warning;
      }

      public KeyIndex IndexFor(XPathNavigator node, string population, string key, XPathExpression keyExpression,
        XPathExpression populationExpression) {
        XObject objectNode = node.UnderlyingObject as XObject;
        XContainer parent = objectNode?.Parent;
        var indexKey = new IndexKey(parent, objectNode is XElement element ? element.Name : default, population, key);
        if (indexes.TryGetValue(indexKey, out KeyIndex index)) {
          return index;
        }

        var candidates = population == null
          ? DefaultPopulation(parent, objectNode is XElement named ? named.Name : default)
          : EvaluateNodes(node, populationExpression);
        index = new KeyIndex();
        foreach (XPathNavigator candidate in candidates) {
          foreach (XPathNavigator identity in EvaluateNodes(candidate, keyExpression)) {
            index.Add(identity.Value, candidate);
          }
        }

        indexes.Add(indexKey, index);
        return index;
      }

      public XPathExpression CompileRelative(string xpath, string argumentName) {
        if (xpath.StartsWith("/", StringComparison.Ordinal)) {
          throw new XPathException($"XPath inheritance {argumentName} argument must be relative: \"{xpath}\"");
        }

        return Compile(xpath);
      }

      public XPathExpression CompilePopulation(string xpath) {
        if (!xpath.StartsWith("/", StringComparison.Ordinal)) {
          throw new XPathException($"XPath inheritance population argument must be absolute: \"{xpath}\"");
        }

        return Compile(xpath);
      }

      private XPathExpression Compile(string xpath) {
        if (!expressions.TryGetValue(xpath, out XPathExpression expression)) {
          expression = XPathExpression.Compile(xpath);
          expressions.Add(xpath, expression);
        }

        return expression;
      }

      public void Warn(string message) => warning?.Invoke($"[StrongMods] XPath inheritance: {message}");

      public void Dispose() {
        s_currentScope = previous;
      }
    }

    private sealed class IndexKey : IEquatable<IndexKey> {
      private readonly XContainer parent;
      private readonly XName elementName;
      private readonly string population;
      private readonly string key;

      public IndexKey(XContainer parent, XName elementName, string population, string key) {
        this.parent = parent;
        this.elementName = elementName;
        this.population = population;
        this.key = key;
      }

      public bool Equals(IndexKey other) => other != null && ReferenceEquals(parent, other.parent) &&
        elementName == other.elementName && population == other.population && key == other.key;

      public override bool Equals(object obj) => Equals(obj as IndexKey);

      public override int GetHashCode() {
        unchecked {
          var hash = parent?.GetHashCode() ?? 0;
          hash = (hash * 397) ^ (elementName?.GetHashCode() ?? 0);
          hash = (hash * 397) ^ (population?.GetHashCode() ?? 0);
          return (hash * 397) ^ (key?.GetHashCode() ?? 0);
        }
      }
    }

    private sealed class KeyIndex {
      private readonly Dictionary<string, XPathNavigator> values = new();
      private readonly HashSet<string> duplicates = new();

      public void Add(string value, XPathNavigator node) {
        if (values.ContainsKey(value)) {
          duplicates.Add(value);
          return;
        }

        values.Add(value, node.Clone());
      }

      public bool TryGet(string value, out XPathNavigator node, out bool duplicate) {
        duplicate = duplicates.Contains(value);
        return values.TryGetValue(value, out node);
      }
    }

    private sealed class InheritanceFunction : IXsltContextFunction {
      private readonly InheritanceOperation operation;

      public InheritanceFunction(InheritanceOperation operation) {
        this.operation = operation;
      }

      public XPathResultType[] ArgTypes => null;
      public int Maxargs => operation == InheritanceOperation.Chain ? 4 : 5;
      public int Minargs => operation == InheritanceOperation.Chain ? 3 : 4;
      public XPathResultType ReturnType => XPathResultType.NodeSet;

      public object Invoke(XsltContext context, object[] args, XPathNavigator docContext) {
        XPathNavigator start = ExactlyOneNode(args[0]);
        if (start == null) {
          return new XPathInheritanceIterator(new List<XPathNavigator>());
        }

        EvaluationScope scope = s_currentScope ?? throw new XPathException("XPath inheritance evaluated outside a scope");
        var select = operation == InheritanceOperation.Inherited ? StringArgument(args[1], "select") : null;
        var link = StringArgument(args[operation == InheritanceOperation.Chain ? 1 : 2], "link");
        var key = StringArgument(args[operation == InheritanceOperation.Chain ? 2 : 3], "key");
        var populationIndex = operation == InheritanceOperation.Chain ? 3 : 4;
        string population = args.Length > populationIndex ? StringArgument(args[populationIndex], "population") : null;
        var linkExpression = scope.CompileRelative(ExpandPropertyShorthand(link), "link");
        var keyExpression = scope.CompileRelative(key, "key");
        var selectExpression = select == null ? null : scope.CompileRelative(ExpandPropertyShorthand(select), "select");
        var populationExpression = population == null ? null : scope.CompilePopulation(population);
        var chain = Walk(start, link, key, population, linkExpression, keyExpression, populationExpression).ToList();

        if (operation == InheritanceOperation.Chain) {
          return new XPathInheritanceIterator(chain);
        }

        foreach (XPathNavigator member in chain) {
          List<XPathNavigator> selected = EvaluateNodes(member, selectExpression);
          if (selected.Count > 0) {
            return new XPathInheritanceIterator(selected);
          }
        }

        return new XPathInheritanceIterator(new List<XPathNavigator>());
      }

      private static IEnumerable<XPathNavigator> Walk(XPathNavigator start, string link, string key, string population,
        XPathExpression linkExpression, XPathExpression keyExpression, XPathExpression populationExpression) {
        EvaluationScope scope = s_currentScope ?? throw new XPathException("XPath inheritance evaluated outside a scope");
        var current = start.Clone();
        var visited = new HashSet<XObject>();
        for (var depth = 0; depth < MaxChainDepth; depth++) {
          XObject currentObject = current.UnderlyingObject as XObject;
          if (currentObject == null || !visited.Add(currentObject)) {
            scope.Warn($"cycle while resolving {DescribeScheme(link, key, population)}.");
            yield break;
          }

          yield return current;
          List<XPathNavigator> links = EvaluateNodes(current, linkExpression);
          if (links.Count == 0) {
            yield break;
          }

          if (links.Count != 1) {
            scope.Warn($"{DescribeNode(current)} has {links.Count} parent links while resolving " +
                       $"{DescribeScheme(link, key, population)}; stopping its chain.");
            yield break;
          }

          var index = scope.IndexFor(current, population, key, keyExpression, populationExpression);
          var parentKey = links[0].Value;
          if (!index.TryGet(parentKey, out XPathNavigator parent, out bool duplicate)) {
            scope.Warn($"{DescribeNode(current)} names missing parent key \"{parentKey}\" while resolving " +
                       $"{DescribeScheme(link, key, population)}; stopping its chain.");
            yield break;
          }

          if (duplicate) {
            scope.Warn($"parent key \"{parentKey}\" is duplicated while resolving " +
                       $"{DescribeScheme(link, key, population)}; stopping its chain.");
            yield break;
          }

          current = parent;
        }

        scope.Warn($"{DescribeNode(start)} exceeded {MaxChainDepth} inheritance links while resolving " +
                   $"{DescribeScheme(link, key, population)}; stopping its chain.");
      }
    }

    private static XPathNavigator ExactlyOneNode(object value) {
      if (!(value is XPathNodeIterator iterator)) {
        throw new XPathException("XPath inheritance node argument must be a node-set");
      }

      if (!iterator.MoveNext()) {
        return null;
      }

      XPathNavigator node = iterator.Current.Clone();
      if (iterator.MoveNext()) {
        throw new XPathException("XPath inheritance node argument must contain exactly one node");
      }

      return node;
    }

    private static string StringArgument(object value, string argumentName) {
      if (!(value is string text)) {
        throw new XPathException($"XPath inheritance {argumentName} argument must be a string literal");
      }

      return text;
    }

    private static string ExpandPropertyShorthand(string xpath) {
      if (!xpath.StartsWith("#", StringComparison.Ordinal)) {
        return xpath;
      }

      return "property[@name=" + XPathStringLiteral(xpath.Substring(1)) + "]/@value";
    }

    private static string XPathStringLiteral(string value) {
      if (!value.Contains("'")) {
        return "'" + value + "'";
      }

      if (!value.Contains("\"")) {
        return "\"" + value + "\"";
      }

      return "concat('" + value.Replace("'", "', \"'\", '") + "')";
    }

    private static List<XPathNavigator> DefaultPopulation(XContainer parent, XName name) {
      if (parent == null || name == null) {
        return new List<XPathNavigator>();
      }

      return parent.Elements(name).Select(e => e.CreateNavigator()).ToList();
    }

    private static List<XPathNavigator> EvaluateNodes(XPathNavigator navigator, XPathExpression expression) {
      object result = navigator.Evaluate(expression);
      if (!(result is XPathNodeIterator iterator)) {
        throw new XPathException($"XPath inheritance expression \"{expression.Expression}\" must select nodes");
      }

      var nodes = new List<XPathNavigator>();
      while (iterator.MoveNext()) {
        nodes.Add(iterator.Current.Clone());
      }

      return nodes;
    }

    private static string DescribeNode(XPathNavigator navigator) {
      if (!(navigator.UnderlyingObject is XElement element)) {
        return navigator.Name;
      }

      string name = (string)element.Attribute("name");
      return name == null ? $"<{element.Name.LocalName}>" : $"<{element.Name.LocalName} name=\"{name}\">";
    }

    private static string DescribeScheme(string link, string key, string population) {
      return $"link \"{link}\", key \"{key}\"{(population == null ? string.Empty : $", population \"{population}\"")}";
    }

    /// <summary>A fixed, cloning iterator for a function-returned XPath node-set.</summary>
    internal sealed class XPathInheritanceIterator : XPathNodeIterator {
      private readonly List<XPathNavigator> navigators;
      private XPathNavigator current;
      private int position;

      public XPathInheritanceIterator(List<XPathNavigator> navigators) {
        this.navigators = navigators;
      }

      private XPathInheritanceIterator(XPathInheritanceIterator other) {
        navigators = other.navigators;
        current = other.current?.Clone();
        position = other.position;
      }

      public override int Count => navigators.Count;
      public override XPathNavigator Current => current;
      public override int CurrentPosition => position;
      public override XPathNodeIterator Clone() => new XPathInheritanceIterator(this);

      public override bool MoveNext() {
        if (position >= navigators.Count) {
          return false;
        }

        current = navigators[position++].Clone();
        return true;
      }
    }

    private enum InheritanceOperation {
      Chain,
      Inherited
    }
  }
}

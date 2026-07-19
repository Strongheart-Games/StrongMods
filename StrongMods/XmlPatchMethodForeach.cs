using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace StrongMods {
  /// <summary>
  ///   Custom XML patch commands for the Stronghold breadth-first patcher.
  ///   <c>&lt;foreach source="..." xpath="..." as="name"&gt;</c> iterates the nodes matched by
  ///   <c>xpath</c> in <c>source</c> (default: the patch's own target document) in document order,
  ///   binding each to <c>name</c>, and applies the body commands once per node.
  ///   Body text, attribute values, and command xpaths may interpolate <c>{...}</c> expressions: any
  ///   XPath 1.0 expression in which every in-scope binding is available as an XPath variable, e.g.
  ///   <c>{$lootContainer/@name}</c>. An expression must resolve to exactly one node or an XPath
  ///   scalar. <c>{left ?: right}</c> evaluates <c>right</c> only when <c>left</c> selects no nodes
  ///   (or a called function returns null). <c>{{</c> and <c>}}</c> are literal braces.
  ///   Two kinds of direct-child declarations extend a foreach's scope, both resolved once per
  ///   foreach and popped with it. <c>&lt;bind name="table" source="..." xpath="..." /&gt;</c> (or
  ///   with inline element children instead of source/xpath) binds a constant node-set usable as
  ///   <c>$table</c>. <c>&lt;function name="tint" method="[namespace.]Class.Method, [mod]" /&gt;</c>
  ///   declares a custom function callable as <c>{tint($x)}</c>; arguments are XPath expressions,
  ///   nested calls are not supported, and the target must carry
  ///   <see cref="XmlPatchFunctionAttribute" /> (see it for the signature and null contract).
  ///   A resolution that selects zero nodes without a <c>?:</c> fallback skips the current iteration
  ///   with a warning, as do multiple matches (on either side of <c>?:</c>), a function returning
  ///   null without a fallback, or a function throwing. Unbound names, malformed expressions, bad
  ///   attributes, unknown sources, and any bad declaration fail the whole foreach with an error,
  ///   matching how vanilla drops a bad command.
  ///   Because XML forbids '{' in tag names, dynamic element names use the reserved
  ///   <see cref="DynamicNameAttribute" /> attribute instead; see its doc comment.
  /// </summary>
  public static class XmlPatchMethodForeach {
    /// <summary>
    ///   XML cannot represent '{' in a tag name, so dynamic element names are expressed with this
    ///   reserved attribute: <c>&lt;placeholder foreach-name="item_{$loot/@name}"/&gt;</c>. After
    ///   substitution, the attribute's value replaces the element name and the attribute itself is
    ///   dropped from the output. A substituted name that is not a valid XML name skips the
    ///   current iteration.
    /// </summary>
    private const string DynamicNameAttribute = "foreach-name";

    private const string BindName = "bind";
    private const string ForeachName = "foreach";
    private const string FunctionName = "function";
    private const int MaxDepth = 16;

    private static readonly Regex s_bindingNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly ScopeXsltContext s_xsltContext = new();

    // Active scopes, outermost first, and the navigator expressions evaluate against. World XML
    // patching is single threaded at load; if that ever changes, these must become [ThreadStatic].
    private static readonly List<ScopeFrame> s_scope = new();
    private static XPathNavigator s_contextNavigator;

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

      // Expressions evaluate against the target document. Save and restore around the body so a
      // nested foreach — same target, dispatched synchronously — composes, and so the navigator
      // never outlives its foreach.
      XPathNavigator previousContext = s_contextNavigator;
      s_contextNavigator = targetFile.XmlDoc.CreateNavigator();
      try {
        return ForeachScoped(context, targetFile, xpath, patchSourceElement, patchFile, patchingMod);
      } finally {
        s_contextNavigator = previousContext;
      }
    }

    private static int ForeachScoped(string context, XmlFile targetFile, string rawXpath,
      XElement patchSourceElement, XmlFile patchFile, Mod patchingMod) {
      // Our own attribute values may still contain '{{'/'}}' escapes and, when this is a nested
      // foreach, expressions over enclosing bindings; both are handled by a normal substitution
      // pass against the current scope.
      if (!TrySubstituteOwnAttribute(context, "xpath", rawXpath ?? string.Empty, out var xpath)) {
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

      if (IsNameInScope(asName)) {
        Log.Error($"{context}: as=\"{asName}\" shadows an enclosing binding, bind, or function.");
        return 0;
      }

      XmlFile sourceFile = targetFile;
      XAttribute sourceAttribute = patchSourceElement.Attribute("source");
      if (sourceAttribute != null) {
        if (!TrySubstituteOwnAttribute(context, "source", sourceAttribute.Value, out var sourceName)) {
          return 0;
        }

        if (!TryResolveSourceFile(context, sourceName, out sourceFile)) {
          return 0;
        }
      }

      // Only direct children are declarations; a <function> or <bind> nested deeper is ordinary
      // content.
      var body = new List<XElement>();
      var functionDeclarations = new List<XElement>();
      var bindDeclarations = new List<XElement>();
      foreach (XElement child in patchSourceElement.Elements()) {
        switch (child.Name.LocalName) {
          case FunctionName:
            functionDeclarations.Add(child);
            break;
          case BindName:
            bindDeclarations.Add(child);
            break;
          default:
            body.Add(child);
            break;
        }
      }

      if (body.Count == 0) {
        Log.Warning($"{context}: foreach has an empty body.");
        return 0;
      }

      // Declarations resolve once per foreach rather than per iteration, and before the xpath
      // runs, so a bad declaration is reported even when nothing matches.
      if (!TryResolveFunctions(context, functionDeclarations, asName, patchingMod,
            out Dictionary<string, MethodInfo> functions)) {
        return 0;
      }

      if (!TryResolveBinds(context, bindDeclarations, asName, functions, targetFile,
            out Dictionary<string, List<XObject>> binds)) {
        return 0;
      }

      if (!sourceFile.GetXpathResults(xpath, out List<XObject> matchList)) {
        return 0;
      }

      // Snapshot the results and release the cache immediately: body commands dispatched below run
      // their own GetXpathResults/ClearXpathResults cycles against this same XmlFile whenever
      // source is omitted, and the body may also mutate the source document when source == target.
      var matches = new List<XObject>(matchList);
      sourceFile.ClearXpathResults();

      if (matches.Count == 0) {
        Log.Out($"{context}: xpath \"{xpath}\" matched no nodes.");
        return 0;
      }

      var applied = 0;
      for (var i = 0; i < matches.Count; i++) {
        XObject match = matches[i];
        s_scope.Add(new ScopeFrame(asName, match, functions, binds));
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
    private static bool TryCloneWithSubstitution(
      XElement source, out XElement clone, out SubstitutionFailure failure) {
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

      var result = new XElement(name);
      foreach (XAttribute attribute in source.Attributes()) {
        if (ReferenceEquals(attribute, dynamicName)) {
          continue;
        }

        if (!TrySubstitute(attribute.Value, out var value, out failure)) {
          return false;
        }

        result.SetAttributeValue(attribute.Name, value);
      }

      foreach (XNode node in source.Nodes()) {
        switch (node) {
          case XElement element when IsForeach(element):
            result.Add(new XElement(element));
            break;
          case XElement element: {
            if (!TryCloneWithSubstitution(element, out XElement childClone, out failure)) {
              return false;
            }

            result.Add(childClone);
            break;
          }
          case XCData cdata: {
            // Must precede XText: XCData derives from it.
            if (!TrySubstitute(cdata.Value, out var text, out failure)) {
              return false;
            }

            result.Add(new XCData(text));
            break;
          }
          case XText text: {
            if (!TrySubstitute(text.Value, out var value, out failure)) {
              return false;
            }

            result.Add(new XText(value));
            break;
          }
          case XComment comment:
            result.Add(new XComment(comment.Value));
            break;
          case XProcessingInstruction pi:
            result.Add(new XProcessingInstruction(pi.Target, pi.Data));
            break;
        }
      }

      clone = result;
      return true;
    }

    /// <summary>
    ///   Expands '{{', '}}', and {expression} in a string. A stray unescaped '}' is treated
    ///   literally for leniency; an unterminated '{' is a fatal error. The closing brace scan is
    ///   quote-aware so a literal '}' inside an XPath string does not end the expression early.
    /// </summary>
    private static bool TrySubstitute(
      string input, out string result, out SubstitutionFailure failure) {
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

          var close = FindExpressionEnd(input, i + 1);
          if (close < 0) {
            failure = SubstitutionFailure.Fail($"unterminated '{{' in \"{input}\"");
            return false;
          }

          var expression = input.Substring(i + 1, close - i - 1).Trim();
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

    /// <summary>Finds the first '}' at or after start that is not inside an XPath string literal.</summary>
    private static int FindExpressionEnd(string input, int start) {
      var quote = '\0';
      for (var i = start; i < input.Length; i++) {
        var c = input[i];
        if (quote != '\0') {
          if (c == quote) {
            quote = '\0';
          }
        } else if (c == '\'' || c == '"') {
          quote = c;
        } else if (c == '}') {
          return i;
        }
      }

      return -1;
    }

    /// <summary>
    ///   Resolves the contents of a {...} expression: "left" or "left ?: right", where each side is
    ///   either a declared-function call or an XPath 1.0 expression over the in-scope bindings. The
    ///   right side runs only when the left selects no nodes or a called function returns null.
    /// </summary>
    private static bool TryResolveExpression(string expression, out string value, out SubstitutionFailure failure) {
      value = null;
      if (!TrySplitCoalesce(expression, out var left, out var right, out failure)) {
        return false;
      }

      OperandResult result = EvaluateOperand(left, out value, out failure);
      if (result == OperandResult.Failed) {
        return false;
      }

      if (result == OperandResult.Success) {
        return true;
      }

      if (right == null) {
        // The failure already carries the skip-kind "no value" message for the left side.
        return false;
      }

      result = EvaluateOperand(right, out value, out failure);
      if (result == OperandResult.Failed) {
        return false;
      }

      if (result == OperandResult.Success) {
        return true;
      }

      failure = SubstitutionFailure.SkipIteration(
        $"{{{expression}}} produced no value on either side of '?:' ({failure.Message})");
      return false;
    }

    /// <summary>
    ///   Splits an expression on a single top-level '?:', tracking bracket depth and quotes so the
    ///   operator is never found inside a predicate or a string. More than one top-level '?:' is an
    ///   error; chaining can be relaxed later if a real need appears.
    /// </summary>
    private static bool TrySplitCoalesce(string expression, out string left, out string right,
      out SubstitutionFailure failure) {
      left = expression;
      right = null;
      failure = default;

      var depth = 0;
      var quote = '\0';
      var split = -1;
      for (var i = 0; i < expression.Length; i++) {
        var c = expression[i];
        if (quote != '\0') {
          if (c == quote) {
            quote = '\0';
          }

          continue;
        }

        switch (c) {
          case '\'':
          case '"':
            quote = c;
            break;
          case '(':
          case '[':
            depth++;
            break;
          case ')':
          case ']':
            depth--;
            if (depth < 0) {
              failure = SubstitutionFailure.Fail($"{{{expression}}} has unbalanced brackets");
              return false;
            }

            break;
          case '?' when depth == 0 && i + 1 < expression.Length && expression[i + 1] == ':':
            if (split >= 0) {
              failure = SubstitutionFailure.Fail($"{{{expression}}} chains '?:' more than once");
              return false;
            }

            split = i;
            i++;
            break;
        }
      }

      if (quote != '\0') {
        failure = SubstitutionFailure.Fail($"{{{expression}}} has an unterminated quote");
        return false;
      }

      if (depth != 0) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} has unbalanced brackets");
        return false;
      }

      if (split < 0) {
        return true;
      }

      left = expression.Substring(0, split).Trim();
      right = expression.Substring(split + 2).Trim();
      if (left.Length == 0 || right.Length == 0) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} is missing a side of '?:'");
        return false;
      }

      return true;
    }

    /// <summary>
    ///   Evaluates one side of an expression. A leading name that matches a declared function makes
    ///   it a call; anything else, including XPath built-ins like string() and count(), compiles as
    ///   XPath. Empty means the operand selected no nodes (or a function returned null) and carries
    ///   a skip-kind failure to use when there is no '?:' fallback. Multiple matches are Failed, not
    ///   Empty: an ambiguous lookup must not silently fall through to the default.
    /// </summary>
    private static OperandResult EvaluateOperand(string operand, out string value, out SubstitutionFailure failure) {
      value = null;
      failure = default;

      if (operand.Length == 0) {
        failure = SubstitutionFailure.Fail("empty expression");
        return OperandResult.Failed;
      }

      var nameLength = NameLength(operand);
      if (nameLength > 0 && nameLength < operand.Length && operand[nameLength] == '(' &&
          LookupFunction(operand.Substring(0, nameLength)) != null) {
        if (operand[operand.Length - 1] != ')') {
          failure = SubstitutionFailure.Fail(
            $"\"{operand}\" calls a declared function but is not a complete call; a call must be " +
            "the entire side of an expression");
          return OperandResult.Failed;
        }

        var name = operand.Substring(0, nameLength);
        var arguments = operand.Substring(nameLength + 1, operand.Length - nameLength - 2);
        return EvaluateCall(name, arguments, operand, out value, out failure);
      }

      if (!TryEvaluateXPath(operand, out var result, out failure)) {
        return OperandResult.Failed;
      }

      switch (result) {
        case XPathNodeIterator iterator: {
          var found = new List<XPathNavigator>();
          while (iterator.MoveNext()) {
            found.Add(iterator.Current.Clone());
          }

          if (found.Count == 0) {
            failure = SubstitutionFailure.SkipIteration($"\"{operand}\" matched 0 nodes, expected 1");
            return OperandResult.Empty;
          }

          if (found.Count > 1) {
            failure = SubstitutionFailure.SkipIteration(
              $"\"{operand}\" matched {found.Count} nodes, expected 1");
            return OperandResult.Failed;
          }

          value = found[0].Value;
          return OperandResult.Success;
        }
        case string text:
          value = text;
          return OperandResult.Success;
        case double number:
          value = FormatXPathNumber(number);
          return OperandResult.Success;
        case bool flag:
          value = flag ? "true" : "false";
          return OperandResult.Success;
        default:
          failure = SubstitutionFailure.Fail(
            $"\"{operand}\" evaluated to unsupported type {result?.GetType().Name ?? "null"}");
          return OperandResult.Failed;
      }
    }

    /// <summary>
    ///   Compiles and evaluates one XPath expression against the current target document, with every
    ///   in-scope binding exposed as an XPath variable via <see cref="ScopeXsltContext" />.
    /// </summary>
    private static bool TryEvaluateXPath(string text, out object result, out SubstitutionFailure failure) {
      result = null;
      failure = default;

      if (s_contextNavigator == null) {
        failure = SubstitutionFailure.Fail("no evaluation context; expression evaluated outside a foreach");
        return false;
      }

      try {
        var compiled = XPathExpression.Compile(text);
        compiled.SetContext(s_xsltContext);
        result = s_contextNavigator.Evaluate(compiled);
        return true;
      } catch (Exception e) when (e is XPathException or ArgumentException) {
        failure = SubstitutionFailure.Fail($"\"{text}\" failed to evaluate: {e.Message}");
        return false;
      }
    }

    /// <summary>
    ///   Invokes a declared function. Arguments are XPath expressions, each subject to the
    ///   exactly-one rule; an argument matching no nodes skips the iteration before the function is
    ///   invoked. A null return is coalescable emptiness; a thrown exception skips the iteration.
    /// </summary>
    private static OperandResult EvaluateCall(string name, string arguments, string operand, out string value,
      out SubstitutionFailure failure) {
      value = null;
      failure = default;

      MethodInfo method = LookupFunction(name);
      if (!TrySplitArguments(arguments, out List<string> parts)) {
        failure = SubstitutionFailure.Fail($"{{{operand}}} has an unbalanced argument list");
        return OperandResult.Failed;
      }

      ParameterInfo[] parameters = method.GetParameters();
      if (parts.Count != parameters.Length) {
        failure = SubstitutionFailure.Fail($"{{{operand}}} passes {parts.Count} argument(s) to \"{name}\", " +
                                           $"which takes {parameters.Length}");
        return OperandResult.Failed;
      }

      var args = new object[parts.Count];
      for (var i = 0; i < parts.Count; i++) {
        var part = parts[i];
        if (part.Length == 0) {
          failure = SubstitutionFailure.Fail($"{{{operand}}} has an empty argument in position {i + 1}");
          return OperandResult.Failed;
        }

        var partNameLength = NameLength(part);
        if (partNameLength > 0 && partNameLength < part.Length && part[partNameLength] == '(' &&
            LookupFunction(part.Substring(0, partNameLength)) != null) {
          failure = SubstitutionFailure.Fail($"{{{operand}}} nests a call to " +
                                             $"\"{part.Substring(0, partNameLength)}\"; nested calls are not " +
                                             "supported");
          return OperandResult.Failed;
        }

        OperandResult argResult = EvaluateOperand(part, out var arg, out failure);
        if (argResult != OperandResult.Success) {
          // An absent argument is a skip, not a coalesce; the failure already carries the message.
          return OperandResult.Failed;
        }

        args[i] = arg;
      }

      object result;
      try {
        result = method.Invoke(null, args);
      } catch (TargetInvocationException e) {
        Exception inner = e.InnerException ?? e;
        failure = SubstitutionFailure.SkipIteration($"{{{operand}}} threw {inner.GetType().Name}: " +
                                                    $"{inner.Message}");
        return OperandResult.Failed;
      }

      if (result == null) {
        failure = SubstitutionFailure.SkipIteration($"{{{operand}}} returned null");
        return OperandResult.Empty;
      }

      value = (string)result;
      return OperandResult.Success;
    }

    /// <summary>
    ///   Splits an argument list on commas at bracket depth zero and outside quotes, so commas
    ///   inside predicates, XPath function calls, and string literals survive. An empty list yields
    ///   no arguments.
    /// </summary>
    private static bool TrySplitArguments(string arguments, out List<string> parts) {
      parts = new List<string>();
      if (arguments.Trim().Length == 0) {
        return true;
      }

      var depth = 0;
      var quote = '\0';
      var start = 0;
      for (var i = 0; i < arguments.Length; i++) {
        var c = arguments[i];
        if (quote != '\0') {
          if (c == quote) {
            quote = '\0';
          }

          continue;
        }

        switch (c) {
          case '\'':
          case '"':
            quote = c;
            break;
          case '(':
          case '[':
            depth++;
            break;
          case ')':
          case ']':
            depth--;
            if (depth < 0) {
              return false;
            }

            break;
          case ',' when depth == 0:
            parts.Add(arguments.Substring(start, i - start).Trim());
            start = i + 1;
            break;
        }
      }

      if (depth != 0 || quote != '\0') {
        return false;
      }

      parts.Add(arguments.Substring(start).Trim());
      return true;
    }

    /// <summary>
    ///   Resolves the &lt;function&gt; declarations of one foreach into a name to method table.
    ///   Every failure here is fatal: a declaration is a static promise, so a broken one is a mod
    ///   bug rather than a per-node data condition. Attribute values are substituted against the
    ///   enclosing scope, which does not yet include this foreach's own binding.
    /// </summary>
    private static bool TryResolveFunctions(string context, List<XElement> declarations, string bindingName,
      Mod patchingMod, out Dictionary<string, MethodInfo> functions) {
      functions = new Dictionary<string, MethodInfo>();
      foreach (XElement declaration in declarations) {
        XAttribute nameAttribute = declaration.Attribute("name");
        if (nameAttribute == null) {
          Log.Error($"{context}: <function> is missing a required name attribute.");
          return false;
        }

        if (!TrySubstituteOwnAttribute(context, "function name", nameAttribute.Value, out var name)) {
          return false;
        }

        if (!s_bindingNameRegex.IsMatch(name)) {
          Log.Error($"{context}: <function name=\"{name}\"> is not a valid name " +
                    "([A-Za-z_][A-Za-z0-9_]*).");
          return false;
        }

        if (name == bindingName || functions.ContainsKey(name) || IsNameInScope(name)) {
          Log.Error($"{context}: <function name=\"{name}\"> collides with a binding, bind, or " +
                    "function already in scope.");
          return false;
        }

        XAttribute methodAttribute = declaration.Attribute("method");
        if (methodAttribute == null) {
          Log.Error($"{context}: <function name=\"{name}\"> is missing a required method attribute.");
          return false;
        }

        if (!TrySubstituteOwnAttribute(context, "function method", methodAttribute.Value, out var reference)) {
          return false;
        }

        if (!TryResolveMethod(context, name, reference, patchingMod, out MethodInfo method)) {
          return false;
        }

        functions.Add(name, method);
      }

      return true;
    }

    /// <summary>
    ///   Resolves the &lt;bind&gt; declarations of one foreach into a name to node-set table. A bind
    ///   holds either its inline element children or the nodes its xpath selects in a source
    ///   document, resolved once and constant across iterations. Every failure here is fatal. An
    ///   empty set is allowed: each use then matches zero nodes and skips or coalesces normally.
    /// </summary>
    private static bool TryResolveBinds(string context, List<XElement> declarations, string bindingName,
      Dictionary<string, MethodInfo> functions, XmlFile targetFile, out Dictionary<string, List<XObject>> binds) {
      binds = new Dictionary<string, List<XObject>>();
      foreach (XElement declaration in declarations) {
        XAttribute nameAttribute = declaration.Attribute("name");
        if (nameAttribute == null) {
          Log.Error($"{context}: <bind> is missing a required name attribute.");
          return false;
        }

        if (!TrySubstituteOwnAttribute(context, "bind name", nameAttribute.Value, out var name)) {
          return false;
        }

        if (!s_bindingNameRegex.IsMatch(name)) {
          Log.Error($"{context}: <bind name=\"{name}\"> is not a valid name ([A-Za-z_][A-Za-z0-9_]*).");
          return false;
        }

        if (name == bindingName || functions.ContainsKey(name) || binds.ContainsKey(name) ||
            IsNameInScope(name)) {
          Log.Error($"{context}: <bind name=\"{name}\"> collides with a binding, bind, or function " +
                    "already in scope.");
          return false;
        }

        var inline = declaration.Elements().ToList();
        XAttribute xpathAttribute = declaration.Attribute("xpath");
        XAttribute sourceAttribute = declaration.Attribute("source");
        if (inline.Count > 0 && (xpathAttribute != null || sourceAttribute != null)) {
          Log.Error($"{context}: <bind name=\"{name}\"> has both inline content and source/xpath; " +
                    "use one or the other.");
          return false;
        }

        if (inline.Count > 0) {
          // References, not clones: the patch document is never mutated, and keeping the originals
          // preserves line info and same-document identity for the XPath engine.
          binds.Add(name, inline.Cast<XObject>().ToList());
          continue;
        }

        if (xpathAttribute == null) {
          Log.Error($"{context}: <bind name=\"{name}\"> has neither inline content nor an xpath " +
                    "attribute.");
          return false;
        }

        if (!TrySubstituteOwnAttribute(context, "bind xpath", xpathAttribute.Value, out var bindXpath)) {
          return false;
        }

        XmlFile sourceFile = targetFile;
        if (sourceAttribute != null) {
          if (!TrySubstituteOwnAttribute(context, "bind source", sourceAttribute.Value, out var sourceName)) {
            return false;
          }

          if (!TryResolveSourceFile(context, sourceName, out sourceFile)) {
            return false;
          }
        }

        if (!sourceFile.GetXpathResults(bindXpath, out List<XObject> matchList)) {
          Log.Out($"{context}: <bind name=\"{name}\"> xpath \"{bindXpath}\" matched no nodes.");
          binds.Add(name, new List<XObject>());
          continue;
        }

        var nodes = new List<XObject>(matchList);
        sourceFile.ClearXpathResults();
        if (nodes.Count == 0) {
          Log.Out($"{context}: <bind name=\"{name}\"> xpath \"{bindXpath}\" matched no nodes.");
        }

        binds.Add(name, nodes);
      }

      return true;
    }

    /// <summary>
    ///   Resolves a source attribute value to its patched document. The patched-file dictionary is
    ///   keyed without the extension, so a ".xml" suffix — the most likely reason a lookup misses —
    ///   gets an error that points straight at the fix.
    /// </summary>
    private static bool TryResolveSourceFile(string context, string sourceName, out XmlFile sourceFile) {
      sourceFile = null;
      if (sourceName.Length == 0) {
        Log.Error($"{context}: empty source attribute.");
        return false;
      }

      if (BreadthFirstXmlPatcher.TryGetPatchedFile(sourceName, out sourceFile)) {
        return true;
      }

      if (sourceName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
        var stripped = sourceName.Substring(0, sourceName.Length - ".xml".Length);
        Log.Error($"{context}: unknown source document \"{sourceName}\"; name the file without " +
                  $"its extension, i.e. source=\"{stripped}\".");
      } else {
        Log.Error($"{context}: unknown source document \"{sourceName}\".");
      }

      return false;
    }

    /// <summary>
    ///   Resolves "[namespace.]Class.Method, [mod]" to a validated patch function. Both the mod and
    ///   its comma are optional; without a mod the type is sought in Assembly-CSharp, matching how
    ///   vanilla resolves bare class references such as ServerClass.
    /// </summary>
    private static bool TryResolveMethod(string context, string name, string reference, Mod patchingMod,
      out MethodInfo method) {
      method = null;
      var prefix = $"{context}: <function name=\"{name}\" method=\"{reference}\">";

      var comma = reference.IndexOf(',');
      var methodPath = (comma < 0 ? reference : reference.Substring(0, comma)).Trim();
      var modName = comma < 0 ? string.Empty : reference.Substring(comma + 1).Trim();

      var dot = methodPath.LastIndexOf('.');
      if (dot <= 0 || dot == methodPath.Length - 1) {
        Log.Error($"{prefix} is not of the form \"[namespace.]Class.Method, [mod]\".");
        return false;
      }

      var typeName = methodPath.Substring(0, dot);
      var methodName = methodPath.Substring(dot + 1);

      List<Assembly> assemblies;
      if (modName.Length == 0) {
        // Any type in Assembly-CSharp will do to name the game assembly; XmlPatcher is already a
        // dependency of this file.
        assemblies = new List<Assembly> { typeof(XmlPatcher).Assembly };
      } else {
        Mod mod = ModManager.GetMod(modName);
        if (mod == null) {
          Log.Error($"{prefix} refers to mod \"{modName}\", which is not loaded.");
          return false;
        }

        assemblies = mod.allAssemblies;
        if (assemblies == null || assemblies.Count == 0) {
          Log.Error($"{prefix} refers to mod \"{modName}\", which has no assemblies.");
          return false;
        }
      }

      Type type = null;
      foreach (Assembly assembly in assemblies) {
        type = assembly.GetType(typeName, false);
        if (type != null) {
          break;
        }
      }

      if (type == null) {
        var where = modName.Length == 0 ? "Assembly-CSharp" : $"mod \"{modName}\"";
        Log.Error($"{prefix} names type \"{typeName}\", which was not found in {where}.");
        return false;
      }

      try {
        method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
      } catch (AmbiguousMatchException) {
        Log.Error($"{prefix} names an overloaded method; patch functions must have a single signature.");
        return false;
      }

      if (method == null) {
        Log.Error($"{prefix} names method \"{methodName}\", which is not a static method of " +
                  $"\"{typeName}\".");
        return false;
      }

      if (!TryValidateSignature(prefix, method)) {
        method = null;
        return false;
      }

      return true;
    }

    /// <summary>
    ///   Enforces the patch function contract: tagged, public, non-generic, string return, all
    ///   string parameters passed by value.
    /// </summary>
    private static bool TryValidateSignature(string prefix, MethodInfo method) {
      if (method.GetCustomAttribute<XmlPatchFunctionAttribute>() == null) {
        Log.Error($"{prefix} names a method that is not tagged [{nameof(XmlPatchFunctionAttribute)}]; " +
                  "patch functions must opt in.");
        return false;
      }

      if (!method.IsPublic) {
        Log.Error($"{prefix} names a non-public method; patch functions must be public static.");
        return false;
      }

      if (method.IsGenericMethod || method.IsGenericMethodDefinition) {
        Log.Error($"{prefix} names a generic method; patch functions must be non-generic.");
        return false;
      }

      if (method.ReturnType != typeof(string)) {
        Log.Error($"{prefix} returns {method.ReturnType.Name}; patch functions must return string.");
        return false;
      }

      foreach (ParameterInfo parameter in method.GetParameters()) {
        if (parameter.ParameterType != typeof(string)) {
          Log.Error($"{prefix} has parameter \"{parameter.Name}\" of type {parameter.ParameterType.Name}; " +
                    "patch functions must take only string parameters passed by value.");
          return false;
        }
      }

      return true;
    }

    /// <summary>Substitutes one of a declaration's own attributes, logging on failure.</summary>
    private static bool TrySubstituteOwnAttribute(
      string context, string attributeName, string raw, out string result) {
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

    /// <summary>Finds a declared function by name, innermost scope first.</summary>
    private static MethodInfo LookupFunction(string name) {
      for (var i = s_scope.Count - 1; i >= 0; i--) {
        if (s_scope[i].Functions.TryGetValue(name, out MethodInfo method)) {
          return method;
        }
      }

      return null;
    }

    /// <summary>Finds the node-set a name is bound to, innermost scope first, or null if unbound.</summary>
    private static List<XObject> LookupVariable(string name) {
      for (var i = s_scope.Count - 1; i >= 0; i--) {
        ScopeFrame frame = s_scope[i];
        if (frame.BindingName == name) {
          return new List<XObject> { frame.Node };
        }

        if (frame.Binds.TryGetValue(name, out List<XObject> nodes)) {
          return nodes;
        }
      }

      return null;
    }

    /// <summary>
    ///   Whether a name is already taken by a binding, a bind, or a function anywhere in the active
    ///   scope. All three share one namespace: syntax could disambiguate them, but a reader should
    ///   not have to squint at punctuation to know what a name means.
    /// </summary>
    private static bool IsNameInScope(string name) {
      foreach (ScopeFrame frame in s_scope) {
        if (frame.BindingName == name || frame.Functions.ContainsKey(name) ||
            frame.Binds.ContainsKey(name)) {
          return true;
        }
      }

      return false;
    }

    /// <summary>Length of the leading [A-Za-z_][A-Za-z0-9_]* run of a string, or 0 if there is none.</summary>
    private static int NameLength(string text) {
      if (text.Length == 0 || !IsNameStart(text[0])) {
        return 0;
      }

      var i = 1;
      while (i < text.Length && (IsNameStart(text[i]) || (text[i] >= '0' && text[i] <= '9'))) {
        i++;
      }

      return i;
    }

    private static bool IsNameStart(char c) {
      return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
    }

    /// <summary>
    ///   Positions a navigator on an XObject. XAttribute is not an XNode and has no navigator of its
    ///   own, so attribute bindings navigate from their parent element.
    /// </summary>
    private static XPathNavigator CreateNavigatorFor(XObject node) {
      switch (node) {
        case XAttribute attribute: {
          XPathNavigator navigator = attribute.Parent?.CreateNavigator();
          if (navigator != null &&
              navigator.MoveToAttribute(attribute.Name.LocalName, attribute.Name.NamespaceName)) {
            return navigator;
          }

          return null;
        }
        case XNode xmlNode:
          return xmlNode.CreateNavigator();
        default:
          return null;
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

    /// <summary>The three outcomes of one side of an expression: a value, coalescable emptiness, or failure.</summary>
    private enum OperandResult {
      Success,
      Empty,
      Failed
    }

    /// <summary>
    ///   One active foreach scope: the bound node and its name, plus the binds and functions
    ///   declared by that foreach. Binds and functions are resolved once and shared by every
    ///   iteration.
    /// </summary>
    private sealed class ScopeFrame {
      public readonly string BindingName;
      public readonly Dictionary<string, List<XObject>> Binds;
      public readonly Dictionary<string, MethodInfo> Functions;
      public readonly XObject Node;

      public ScopeFrame(string bindingName, XObject node, Dictionary<string, MethodInfo> functions,
        Dictionary<string, List<XObject>> binds) {
        BindingName = bindingName;
        Node = node;
        Functions = functions;
        Binds = binds;
      }
    }

    /// <summary>
    ///   A failed substitution. Fatal failures (unbound names, malformed expressions) abort the
    ///   whole foreach; non-fatal failures (zero/multiple matches, null returns, invalid dynamic
    ///   names) skip the current iteration with a warning.
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

    /// <summary>
    ///   Exposes the live scope stack to compiled XPath expressions: every binding and bind is a
    ///   variable, resolved at evaluation time. Custom functions are deliberately not routed through
    ///   XPath; they are parsed and invoked by EvaluateCall so their null-skip contract survives.
    /// </summary>
    private sealed class ScopeXsltContext : XsltContext {
      public ScopeXsltContext() : base(new NameTable()) {
      }

      public override bool Whitespace => false;

      public override int CompareDocument(string baseUri, string nextbaseUri) {
        return 0;
      }

      public override bool PreserveWhitespace(XPathNavigator node) {
        return false;
      }

      public override IXsltContextFunction ResolveFunction(string prefix, string name,
        XPathResultType[] argTypes) {
        throw new XPathException($"unknown XPath function \"{name}()\"; a declared patch function " +
                                 "must be the entire side of an expression, not part of one");
      }

      public override IXsltContextVariable ResolveVariable(string prefix, string name) {
        if (!string.IsNullOrEmpty(prefix)) {
          throw new XPathException($"namespaced variable \"${prefix}:{name}\" is not supported");
        }

        List<XObject> nodes = LookupVariable(name);
        if (nodes == null) {
          throw new XPathException($"${name} is not a bound name in the current foreach scope");
        }

        return new ScopeVariable(nodes);
      }
    }

    /// <summary>An XPath variable whose value is a node-set drawn from the scope stack.</summary>
    private sealed class ScopeVariable : IXsltContextVariable {
      private readonly List<XObject> _nodes;

      public ScopeVariable(List<XObject> nodes) {
        _nodes = nodes;
      }

      public bool IsLocal => false;

      public bool IsParam => false;

      public XPathResultType VariableType => XPathResultType.NodeSet;

      public object Evaluate(XsltContext context) {
        var navigators = new List<XPathNavigator>(_nodes.Count);
        foreach (XObject node in _nodes) {
          XPathNavigator navigator = CreateNavigatorFor(node);
          if (navigator != null) {
            navigators.Add(navigator);
          }
        }

        return new NavigatorListIterator(navigators);
      }
    }

    /// <summary>
    ///   A node-set iterator over a fixed list of navigators. Current is a clone made at MoveNext,
    ///   so an engine that moves the returned navigator cannot corrupt the underlying list.
    /// </summary>
    private sealed class NavigatorListIterator : XPathNodeIterator {
      private readonly List<XPathNavigator> _navigators;
      private XPathNavigator _current;
      private int _position;

      public NavigatorListIterator(List<XPathNavigator> navigators) {
        _navigators = navigators;
      }

      private NavigatorListIterator(NavigatorListIterator other) {
        _navigators = other._navigators;
        _position = other._position;
        _current = other._current?.Clone();
      }

      public override int Count => _navigators.Count;

      public override XPathNavigator Current => _current;

      public override int CurrentPosition => _position;

      public override XPathNodeIterator Clone() {
        return new NavigatorListIterator(this);
      }

      public override bool MoveNext() {
        if (_position >= _navigators.Count) {
          return false;
        }

        _current = _navigators[_position].Clone();
        _position++;
        return true;
      }
    }
  }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
  ///   A direct <c>&lt;function name="tint" method="[namespace.]Class.Method, [mod]" /&gt;</c> child
  ///   declares a custom function for the duration of that foreach, callable as
  ///   <c>{tint(arg, arg)}</c> wherever a binding is. Each argument is itself a binding reference;
  ///   nested calls and string literals are not supported. The target must carry
  ///   <see cref="XmlPatchFunctionAttribute" />; see it for the required signature and null contract.
  ///   A resolution that matches zero or multiple nodes skips the current iteration with a
  ///   warning, as does a function returning null or throwing; unbound names, malformed xpaths, bad
  ///   attributes, unknown sources, and any bad function declaration or call site fail the whole
  ///   foreach with an error, matching how vanilla drops a bad command.
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
    private const string FunctionName = "function";
    private const int MaxDepth = 16;

    private static readonly Regex s_bindingNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Active scopes, outermost first. World XML patching is single threaded at load; if that ever changes, this must
    // become [ThreadStatic].
    private static readonly List<ScopeFrame> s_scope = new();

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

      // Our own attribute values may still contain '{{'/'}}' escapes and, when this is a nested foreach, references to
      // enclosing bindings; both are handled by a normal substitution pass against the current scope.
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

      if (IsNameInScope(asName)) {
        Log.Error($"{context}: as=\"{asName}\" shadows an enclosing binding or function.");
        return 0;
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
          // The patched-file dictionary is keyed without the extension. A ".xml" suffix is the most likely reason a
          // lookup misses, so point straight at the fix rather than just reporting the name back.
          if (sourceName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
            var stripped = sourceName[..^".xml".Length];
            Log.Error(
              $"{context}: unknown source document \"{sourceName}\"; name the file without its extension, i.e. source=\"{stripped}\".");
          } else {
            Log.Error($"{context}: unknown source document \"{sourceName}\".");
          }

          return 0;
        }
      }

      // Only direct children are declarations; a <function> nested deeper is ordinary content.
      var body = new List<XElement>();
      var declarations = new List<XElement>();
      foreach (XElement child in patchSourceElement.Elements()) {
        if (child.Name.LocalName == FunctionName) {
          declarations.Add(child);
        } else {
          body.Add(child);
        }
      }

      if (body.Count == 0) {
        Log.Warning($"{context}: foreach has an empty body.");
        return 0;
      }

      // Resolved once per foreach rather than per iteration, and before the xpath runs, so that a
      // bad declaration is reported even when nothing matches.
      if (!TryResolveFunctions(context, declarations, asName, patchingMod,
            out Dictionary<string, MethodInfo> functions)) {
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
        s_scope.Add(new ScopeFrame(asName, match, functions));
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
    ///   Resolves the contents of a {...} expression: either a call, "name(arg, arg)", or a binding
    ///   reference. The two are told apart by the character following the leading name, so an xpath
    ///   function in a relative path ("loot/string(@name)") is never mistaken for a call.
    /// </summary>
    private static bool TryResolveExpression(string expression, out string value, out SubstitutionFailure failure) {
      var nameLength = NameLength(expression);
      if (nameLength == 0 || nameLength == expression.Length || expression[nameLength] != '(') {
        return TryResolveBinding(expression, out value, out failure);
      }

      value = null;
      if (expression[^1] != ')') {
        failure = SubstitutionFailure.Fail($"{{{expression}}} is missing its closing ')'");
        return false;
      }

      var name = expression.Substring(0, nameLength);
      var arguments = expression.Substring(nameLength + 1, expression.Length - nameLength - 2);
      return TryResolveCall(name, arguments, expression, out value, out failure);
    }

    /// <summary>
    ///   Invokes a declared function. Argument count is checked against the reflected signature at the
    ///   call site rather than at declaration, since the body is only walked per iteration; a mismatch
    ///   is fatal either way. A null return or a thrown exception skips the iteration.
    /// </summary>
    private static bool TryResolveCall(string name, string arguments, string expression, out string value,
      out SubstitutionFailure failure) {
      value = null;
      failure = default;

      MethodInfo method = LookupFunction(name);
      if (method == null) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} calls unbound function \"{name}\"");
        return false;
      }

      if (!TrySplitArguments(arguments, out List<string> parts)) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} has an unbalanced argument list");
        return false;
      }

      ParameterInfo[] parameters = method.GetParameters();
      if (parts.Count != parameters.Length) {
        failure = SubstitutionFailure.Fail($"{{{expression}}} passes {parts.Count} argument(s) to \"{name}\", " +
                                           $"which takes {parameters.Length}");
        return false;
      }

      var args = new object[parts.Count];
      for (var i = 0; i < parts.Count; i++) {
        var part = parts[i];
        if (part.Length == 0) {
          failure = SubstitutionFailure.Fail($"{{{expression}}} has an empty argument in position {i + 1}");
          return false;
        }

        var partNameLength = NameLength(part);
        if (partNameLength > 0 && partNameLength < part.Length && part[partNameLength] == '(') {
          failure = SubstitutionFailure.Fail($"{{{expression}}} nests a call to " +
                                             $"\"{part.Substring(0, partNameLength)}\"; nested calls are not " +
                                             "supported");
          return false;
        }

        if (!TryResolveBinding(part, out var arg, out failure)) {
          return false;
        }

        args[i] = arg;
      }

      object result;
      try {
        result = method.Invoke(null, args);
      } catch (TargetInvocationException e) {
        Exception inner = e.InnerException ?? e;
        failure = SubstitutionFailure.SkipIteration($"{{{expression}}} threw {inner.GetType().Name}: " +
                                                    $"{inner.Message}");
        return false;
      }

      if (result == null) {
        failure = SubstitutionFailure.SkipIteration($"{{{expression}}} returned null");
        return false;
      }

      value = (string)result;
      return true;
    }

    /// <summary>
    ///   Splits an argument list on commas at bracket depth zero, so that commas inside xpath
    ///   predicates and xpath function calls survive. An empty list yields no arguments.
    /// </summary>
    private static bool TrySplitArguments(string arguments, out List<string> parts) {
      parts = new List<string>();
      if (arguments.Trim().Length == 0) {
        return true;
      }

      var depth = 0;
      var start = 0;
      for (var i = 0; i < arguments.Length; i++) {
        switch (arguments[i]) {
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

      if (depth != 0) {
        return false;
      }

      parts.Add(arguments.Substring(start).Trim());
      return true;
    }

    /// <summary>
    ///   Resolves "binding" or "binding/relative/xpath" against the scope stack. Node-set results
    ///   must contain exactly one node (zero or multiple skips the iteration); scalar XPath
    ///   results (string(), count(), boolean tests) are accepted as-is.
    /// </summary>
    private static bool TryResolveBinding(string expression, out string value, out SubstitutionFailure failure) {
      value = null;
      failure = default;

      var slash = expression.IndexOf('/');
      var name = slash < 0 ? expression : expression.Substring(0, slash);
      var relativePath = slash < 0 ? null : expression.Substring(slash + 1);

      ScopeFrame binding = null;
      for (var i = s_scope.Count - 1; i >= 0; i--) {
        if (s_scope[i].BindingName == name) {
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

    /// <summary>
    ///   Resolves the &lt;function&gt; declarations of one foreach into a name to method table. Every
    ///   failure here is fatal: a declaration is a static promise, so a broken one is a mod bug rather
    ///   than a per-node data condition. Attribute values are substituted against the enclosing scope,
    ///   which does not yet include this foreach's own binding.
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
          Log.Error($"{context}: <function name=\"{name}\"> collides with a binding or function " +
                    "already in scope.");
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
    ///   Resolves "[namespace.]Class.Method, [mod]" to a validated patch function. Both the mod and its
    ///   comma are optional; without a mod the type is sought in Assembly-CSharp, matching how vanilla
    ///   resolves bare class references such as ServerClass.
    /// </summary>
    private static bool TryResolveMethod(string context, string name, string reference, Mod patchingMod,
      out MethodInfo method) {
      method = null;
      var prefix = $"{context}: <function name=\"{name}\" method=\"{reference}\">";

      var comma = reference.IndexOf(',');
      var methodPath = (comma < 0 ? reference : reference[..comma]).Trim();
      var modName = comma < 0 ? string.Empty : reference[(comma + 1)..].Trim();

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
    ///   Enforces the patch function contract: tagged, public, non-generic, string return, all string
    ///   parameters passed by value.
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

    /// <summary>Finds a declared function by name, innermost scope first.</summary>
    private static MethodInfo LookupFunction(string name) {
      for (var i = s_scope.Count - 1; i >= 0; i--) {
        if (s_scope[i].Functions.TryGetValue(name, out MethodInfo method)) {
          return method;
        }
      }

      return null;
    }

    /// <summary>
    ///   Whether a name is already taken by a binding or a function anywhere in the active scope.
    ///   Bindings and functions share one namespace: parentheses would disambiguate them, but a reader
    ///   should not have to squint at punctuation to know what a name means.
    /// </summary>
    private static bool IsNameInScope(string name) {
      foreach (ScopeFrame frame in s_scope) {
        if (frame.BindingName == name || frame.Functions.ContainsKey(name)) {
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

    /// <summary>
    ///   One active foreach scope: the bound node, its name, and the functions declared by that
    ///   foreach. The function table is resolved once and shared by every iteration.
    /// </summary>
    private sealed class ScopeFrame {
      public readonly string BindingName;
      public readonly Dictionary<string, MethodInfo> Functions;
      public readonly XObject Node;

      public ScopeFrame(string bindingName, XObject node, Dictionary<string, MethodInfo> functions) {
        BindingName = bindingName;
        Node = node;
        Functions = functions;
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

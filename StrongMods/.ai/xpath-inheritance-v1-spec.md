# XPath inheritance functions (v1) — spec

Proposal for resolving 7 Days to Die XML inheritance inside XPath predicates, so that a selector can
match nodes that inherit a property rather than only those that declare it.

Semantics (§3–§5) and every §8 decision are ratified. §6–§7 were re-planned 2026-08-03 against the
#14/#43 test infrastructure, which postdates the first draft — §8.7–§8.8 record that cycle.

## 1. The problem

XPath has no notion of the game's inheritance, so a selector written against a declared property
silently misses every node that inherits it:

```xml
<item name="foo">
  <property name="Class" value="LootContainer" />
</item>
<item name="bar">
  <property name="Extends" value="foo" />
</item>
```

`/items/item[property[@name='Class'][@value='LootContainer']]` matches `foo` and not `bar`, even
though the game treats both as loot containers. `foreach.md`'s **Gotchas** section currently tells
authors to widen the predicate by hand — which only ever covers one level, and only the levels the
author happened to think of.

## 2. Rejected alternative: a propagation pass

The obvious fix is a command that walks the hierarchy and copies inherited values onto descendants,
so ordinary XPath then finds them. Rejected for two reasons:

1. **It breaks file locality.** To be useful the marks must land in the file being *selected from*,
   which is frequently not the file being patched (a `recipes.xml` patch selecting items by
   inherited `Class`). Every existing patch command mutates only its own target document, and that
   rule is what lets a reader find the patch responsible for a change by opening one file. A
   command that reaches sideways costs more than it buys.
2. **It freezes the child.** Writing a real `<property name="Class">` onto a node that previously
   inherited it converts a live link into a snapshot. A later mod retuning the parent no longer
   reaches the child, and nothing in the log says why. Inert marker attributes plus a
   strip-before-parse pass would dodge this, at the price of a reserved namespace in every patched
   document and a new phase in the patcher.

A read-only query has neither problem: it mutates nothing, and it resolves against the document as
it stands at the moment the command runs.

## 3. Design

Two XPath extension functions, usable in any patch xpath, that answer questions about a node's
inheritance chain. **Inheritance is never inferred.** The caller states how the hierarchy is wired,
every time, as ordinary XPath fragments passed as string arguments.

```xml
<!-- Every item whose effective Class is LootContainer, declared or inherited, at any depth. -->
<set xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']
            /property[@name='Stacknumber']">1</set>
```

An inheritance scheme is three things. The first two are always explicit:

| Part           | Meaning                                                            | Examples                                             |
|----------------|--------------------------------------------------------------------|------------------------------------------------------|
| **link**       | Relative XPath on a child yielding its parent's identity           | `@extends`, `property[@name='Extends']/@value`       |
| **key**        | Relative XPath on a candidate yielding its own identity            | `@name`, `@id`                                       |
| **population** | Where candidate parents live. Optional; defaults to §5.3           | `/items/item`                                        |

Because the walk is transitive and each hop must re-evaluate `link` and `key` against a *different*
node, these cannot be passed as live XPath — XPath 1.0 evaluates arguments eagerly, so
`sm:inherited(., @extends, ...)` would pass the string value of the context node's `@extends` and
the walk could never take a second step. They are passed as **string literals** and compiled by us.

### 3.1 `sm:chain(node, link, key [, population])` → node-set

The node itself followed by its ancestors, nearest first. The escape hatch: anything the other
function does not cover is expressible over this set.

```xml
<!-- Is, or descends from, ammoBase — at any depth. -->
xpath="/items/item[sm:chain(., '#Extends', '@name')[@name='ammoBase']]"

<!-- How deep is this block's hierarchy? -->
value="{count(sm:chain($block, '@extends', '@name'))}"
```

`sm:chain` includes the node itself, so this reads "is or descends from". A self-excluding variant
is deliberately not in v1 (§8.4).

### 3.2 `sm:inherited(node, select, link, key [, population])` → node-set

The `select` result from the **nearest** member of the chain that has one, starting with the node
itself. This is "the effective value", and it is not the same as filtering `sm:chain`: a child that
overrides `Class` to something else must not match its parent's value, but

```xml
sm:chain(...)/property[@name='Class']/@value = 'LootContainer'
```

matches if *any* member of the chain says `LootContainer`, because `=` against a node-set means
"some node". `sm:inherited` resolves the override correctly because the walk stops at the first
member that defines the thing.

Returning a node-set rather than a string is deliberate: `= 'LootContainer'` still works by XPath's
node-set-to-string rule, and `not(sm:inherited(...))` additionally distinguishes "nothing in the
chain defines it" from "defined as the empty string".

## 4. Reference

| Argument     | Type      | Notes                                                                              |
|--------------|-----------|------------------------------------------------------------------------------------|
| `node`       | node-set  | The child to resolve from. `.` in a predicate, `$binding` in a `{...}` expression. |
| `select`     | string    | Relative XPath or `#Name` (§4.1); what to fetch once the defining member is found. |
| `link`       | string    | Relative XPath or `#Name` (§4.1); a child's pointer at its parent.                 |
| `key`        | string    | Relative XPath; a candidate's own identity.                                         |
| `population` | string    | Optional. Absolute XPath selecting candidate parents. Default per §5.3.             |

### 4.1 The `#Name` shorthand

In `select` and `link`, an argument beginning with `#` names a property in the game's flat
name/value form:

```
'#Class'  ⇒  property[@name='Class']/@value
```

That is the entire rule. It exists because the general form needs three levels of quoting (XML
attribute, XPath literal, inner predicate literal) against two available quote characters, so at
least one `&quot;` per argument is unavoidable:

```xml
<!-- These two are identical. -->
sm:inherited(., '#Class', '#Extends', '@name')
sm:inherited(., 'property[@name=&quot;Class&quot;]/@value', 'property[@name=&quot;Extends&quot;]/@value', '@name')
```

The shorthand elides only the game's structural boilerplate — the modder still names the property
and the key. Anything not matching that shape uses the general form, which is always accepted.
`key` takes no shorthand: identities are attributes (`@name`, `@id`), which already need no
escaping.

Valid anywhere an xpath is evaluated: every vanilla command's `xpath`, `<foreach xpath>`,
`<bind xpath>`, and inside `{...}` expressions. The `sm` prefix needs no declaration in the patch
file — it is bound by our evaluation context, not by the document.

**A mod using `sm:` hard-depends on StrongMods.** Without it the expression reaches vanilla's
`XPathEvaluate`, which throws on the unbound prefix and fails the command. Declare the dependency in
`ModInfo.xml` (see `modinfo-dependencies-v1-spec.md`); a missing dependency is a startup error,
which is a better failure than a patch that quietly does nothing.

## 5. Semantics

### 5.1 The walk

From `node`: yield it, evaluate `link` on it, and if that produces a value, look the value up in the
key index (§6.2) to find the parent. Repeat. Stop on the first hop that produces nothing.

| Condition                                | Behavior                                                              |
|------------------------------------------|-----------------------------------------------------------------------|
| `link` selects no nodes                  | Chain ends here. Silent — a root node is the normal case.             |
| `link` selects 2+ nodes                  | Chain ends here, one warning. Ambiguity does not guess.               |
| `link` value matches no node in the key index | Chain ends here, one warning naming the missing key.             |
| `link` value matches 2+ nodes            | Chain ends here, one warning. Duplicate keys are a data error.        |
| Chain revisits a node                    | Chain ends at the repeat, one warning naming the cycle.               |
| Chain exceeds `MaxChainDepth` (64)       | Chain ends, one warning. Backstop for a cycle the visited set missed. |

Warnings carry the document, the node, and the scheme arguments, in the style of the foreach skip
messages. Every one of these truncates the chain rather than failing the command: a broken link in
one node's ancestry is a data condition, and the other 2,000 nodes still have answers.

### 5.2 `node` cardinality

Empty node-set in, empty node-set out. Two or more nodes is an error (`XPathException`) — there is
no defensible way to pick, and in the predicate position the argument is always exactly one node.

### 5.3 Default population

Absent an explicit `population`, candidate parents are **the siblings of `node` that share its
element name** — for `/items/item`, the other `item` children of `/items`. This holds for every
inheriting config the game ships (`items`, `blocks`, `entity_classes`, `progression`, …) and it
keeps the common call to three arguments. It is a default, not an assumption: pass `population` when
the hierarchy lives somewhere else.

Lookups are always **within the document the expression is already reading**. There is no
cross-document form and none is planned — inheritance in the game's XML is intra-file, and adding
reach here would give the functions more visibility than the commands that use them.

### 5.4 Return-order caveat

`sm:chain` builds its result nearest-first, but .NET is free to re-sort a function-returned node-set
into document order when a location step or positional predicate is applied to it. **Do not rely on
`[1]` or `[last()]` to mean "nearest" or "root".** Use `sm:inherited` when nearest-wins matters;
that walk happens in our code, where the order is ours.

## 6. Implementation

### 6.1 Interception

`XmlFile.GetXpathResultsInList(string _xpath, List<XObject> _matchList)` is the single funnel. Its
IL is `_matchList.Clear()` then `AddRange(XmlDoc.XPathEvaluate(_xpath).Cast<XObject>())`;
`GetXpathResults` does nothing but allocate and delegate to it. One Harmony prefix therefore covers
every consumer: all eleven vanilla commands (one shared delegate signature, dispatched through
`XmlPatcher.XpathPatchMethods`), `<foreach>` and `<bind>` xpaths, `<conditional>`'s NCalc `xpath()`
function (it calls `GetXpathResultsInList` directly), and any third-party patch command that
evaluates xpaths through `XmlFile`. All facts in this section re-verified against the live install
(V3.1.0-b14 line) on 2026-08-03 via Cecil dumps; the smoke suite re-proves the target's existence
per unit from here on (see the registration paragraph below).

```csharp
[HarmonyPatchCategory(XPathInheritance.Category)]
[HarmonyPatch(typeof(XmlFile), nameof(XmlFile.GetXpathResultsInList))]
public static class XmlFileGetXpathResultsInListPatch {
  public static bool Prefix(XmlFile __instance, string _xpath, List<XObject> _matchList, ref bool __result) {
    // Fast path: no marker, vanilla handles it. A false positive is harmless — our engine is a
    // superset of vanilla's for expressions that do not use variables.
    if (_xpath == null || _xpath.IndexOf(FunctionPrefix, StringComparison.Ordinal) < 0) {
      return true;
    }
    ...
  }
}
```

The replacement compiles with `XPathExpression.Compile` + `SetContext`, evaluates from
`__instance.XmlDoc.CreateNavigator()` (same context node as vanilla: the document root), maps each
`XPathNavigator` back through `.UnderlyingObject` to its `XObject`, fills `_matchList`, sets
`__result = _matchList.Count > 0`, and returns `false`.

A non-node-set result (string, number, boolean) is an error: log it and produce zero matches.
Vanilla throws `InvalidCastException` from `Cast<XObject>` in that case; a logged error and a
command that does not apply is a better outcome than an exception crossing `singlePatch`.

Registration follows the existing pattern in `ModApi.cs` — `Config.XPathInheritanceEnabled`, then
`harmony.PatchCategory(XPathInheritance.Category)` from a new `InitXPathInheritance`; the `Category`
constant lives on the feature class, per the categorized-patches convention. The prefix body is a
thin adapter over a public engine seam, so tests can also probe the engine without going through
dispatch. Because the patch is attribute-declared, the existing smoke suite verifies the target
resolves on both units automatically, and no `[PatchTargetManifest]` is needed — nothing calls
`harmony.Patch(...)` programmatically.

### 6.2 The key index is mandatory, not an optimization

A predicate over `/items/item` invokes the function once per item. Vanilla `items.xml` is ~2,500
items; resolving a parent by scanning the population and evaluating `key` on each candidate would
cost 2,500 candidates × ~3 hops × 2,500 items ≈ 19M XPath evaluations for one selector. Unusable.

So each `(document, population, key)` triple gets a `Dictionary<string, XObject>` built once — and
**cache lifetime is one xpath evaluation**. The document cannot change during an evaluation (a
command applies only after its xpath resolves), so no invalidation logic is needed, and mutations
between commands are picked up because the next command rebuilds. Set up and tear down in a
`try`/`finally` around the evaluation, in both the interception path and the `{...}` path. With the
index, that same selector costs one index build plus ~7,500 dictionary lookups.

Compiled `select`/`link`/`key`/`population` expressions are cached by string for the same lifetime.

Duplicate keys in the index are the §5.1 "2+ nodes" case: record the collision, warn once, and let
the lookup fail rather than choosing a winner.

### 6.3 Two contexts, one function table

`XmlPatchMethodForeach.ScopeXsltContext` already resolves `$bindings` and currently throws from
`ResolveFunction`. It should consult the shared function table first and keep its existing message
for unknown names.

The interception path gets a **separate** context that resolves the same functions but still throws
on variables. This preserves the rule `foreach.md` documents today — body command xpaths are
vanilla XPath, variables only exist inside `{...}` — which would otherwise change for exactly those
xpaths that happen to contain `sm:`.

No conflict with the existing `<function>` mechanism: `EvaluateOperand` scans a leading
`[A-Za-z_][A-Za-z0-9_]*` run followed by `(`, and `sm:inherited(` breaks that scan at the colon, so
it falls through to XPath compilation. The two mechanisms also differ usefully — a declared
`<function>` must be an entire side of an expression, while `sm:` functions are real XPath functions
and compose anywhere.

### 6.4 New files

In StrongMods:

- `XPathInheritance.cs` — the walk, the key index, the compiled-expression cache, the `Category`
  constant, the public engine seam.
- `XPathFunctionContext.cs` — the function table, the `IXsltContextFunction` implementations, the
  variable-free `XsltContext`, and the Harmony prefix.

`XmlPatchMethodForeach.cs` changes by a few lines in `ScopeXsltContext.ResolveFunction` only.
`NavigatorListIterator` there is exactly the node-set return type these functions need and should be
lifted to internal rather than duplicated.

In Tests:

- `Tests.csproj` — `MonoMod.RuntimeDetour` (≥ 25.3.6) joins the existing Backports/Utils references,
  per the S1b correction's pin rule (foreach-conformance-plan §2).
- `Fixtures/GameRoom.cs` — applies the `XPathInheritance` category to its loaded StrongMods assembly
  at room construction, beside the existing foreach registration. `PatchPipeline` becomes sm:-aware
  through the same room with no change of its own.
- `Inheritance/` — the conformance test classes (§7).

## 7. Staging and verification

Re-planned 2026-08-03: the original §7 predated the #14/#43 test infrastructure and verified by
"compilation plus a live run". The bar now is the one foreach set — **every clause of the shipped
doc covered by a conformance test, with the D3 divergence rule** (a spec/implementation mismatch is
a finding to raise, never silently encoded; `Tests/.ai/foreach-conformance-plan.md`) — and the
mechanism is testable end to end since the S1b correction: rooms can apply Harmony patches on
.NET 10, proven by a rerun whose test case was exactly this feature's prefix intercepting a marked
xpath inside a real `singlePatch` dispatch.

Iterations, each gated, each inside the size limits:

1. **`Docs/inheritance.md` first.** Conformance tests are doc-clause-driven, so the doc is the test
   inventory and precedes the code it specifies. Derived from §3–§5; says "declared or inherited"
   wherever it describes `sm:inherited` (§8.3). Also swaps `foreach.md`'s **Gotchas**
   widen-your-predicate advice for a pointer here.
2. **Engine + prefix in StrongMods.** The walk, key index, `#Name` desugaring, both contexts, the
   prefix over the public seam, `Config.XPathInheritanceEnabled` + `InitXPathInheritance`, and the
   `ScopeXsltContext.ResolveFunction` hook. Verified by compilation plus the existing smoke suite,
   which picks up the attribute-declared target on both units with no new test code.
3. **Harness integration + first conformance wave.** The §6.4 Tests changes, then
   `Inheritance/` opens with chain and effective-value basics: declared, inherited at depth,
   overridden mid-chain, both link forms spelled explicitly, `#Name` versus the general form,
   `@id` keys, population default and explicit.
4. **Conformance waves to closure.** Failure modes — §5.1's table row by row with warning texts
   asserted, §5.2 cardinality, the non-node-set error; interop — `sm:` inside `<foreach>`/`<bind>`
   xpaths and `{...}` expressions, cross-file `source=` (the chain resolves in the source
   document), unmarked-xpath fast-path fidelity, and variables still rejected in plain command
   xpaths; scale — a chain query over the room's real vanilla `items.xml` (`VanillaConfigDir`);
   and a test pinning §5.4's return-order caveat so the doc's warning stays true.

Ongoing and free: `PatchApplicationTests` exercises any repo mod that adopts `sm:` against real
vanilla config on both units in CI, under its declared-expectations regime.

Risks: **(a)** detours on ubuntu CI are unproven — iteration 3's first CI run is the Linux proof,
the same posture as #14's R1, which resolved positive; **(b)** a game update raising 0Harmony's
MonoMod references above the Tests pin fails loudly (`FileLoadException`) and is fixed by bumping
the pin — the S1b correction records the rule.

## 8. Decisions

All open questions are settled — 1–4 as asked (2026-07-26, each going to the recommendation), 5–6
recorded unprompted, 7–8 from the 2026-08-03 re-plan. Rationale is kept because the rejected side
of several is a standing temptation to revisit.

1. **Property shorthand: include `#Name`** (§4.1). The alternative — general form only, one syntax
   to learn — loses to the quoting arithmetic: three nesting levels against two quote characters
   makes at least one `&quot;` per argument unavoidable, so "no sugar" is not a choice between two
   syntaxes but between one readable line and four unreadable ones. The desugaring rule is a single
   substitution and the general form is never taken away.
2. **Population: default to same-name siblings** (§5.3). Requiring it would be more consistent with
   `link` and `key`, but the fourth argument would be `/items/item` copied from the selector that
   already said it — explicitness that carries no information. It remains a default, not an
   assumption: pass `population` when the hierarchy lives elsewhere.
3. **Naming: `sm:inherited` and `sm:chain`.** `sm:effective` is strictly more accurate, since the
   walk is self-first and a node that declares the property matches its own value. It lost on how
   the two read inside a predicate. **Docs must therefore say "declared or inherited" wherever they
   describe `sm:inherited`** — that phrase is carrying the precision the name gave up.
4. **No self-excluding variant in v1.** `sm:chain` includes the node, so descent tests read "is or
   descends from", which is the usual intent. Neither `sm:ancestors` nor a self-inclusion flag ships
   until something real needs strict descent; the flag form was additionally rejected for adding
   arity ambiguity beside the already-optional `population`.
5. **Function prefix: `sm:`**, bound in our evaluation context and invisible to patch authors.
   Hyphenated bare names (`sm-inherited`) are legal XPath 1.0 and would need no namespace machinery,
   but `sm:` is the conventional spelling for an extension function and keeps the marker scan
   distinctive.
6. **Marker scan: accept `IndexOf("sm:")`** (§6.1). It can false-positive on an unrelated `sm:`
   inside a string literal, which only changes which engine compiles the expression — and ours is a
   superset for variable-free expressions. A real tokenizer on every patch xpath in the game costs
   more than it protects. *(Recorded without being asked; raise it if you disagree.)*
7. **Mechanism: the §6.1 Harmony prefix, re-ratified 2026-08-03 after a challenge cycle.** The
   review against the new test infrastructure found the then-recorded S1b fact — "no Harmony
   patching on .NET 10" — made the prefix untestable end to end, and designed a registry-wrapper
   alternative (re-register the eleven vanilla commands through `XpathPatchMethods`, rewrite marked
   xpaths into position-path unions). The owner challenged S1b itself; the rerun overturned it
   (foreach-conformance-plan §2 S1b), and the wrapper died with its only advantage. What remains on
   the ledger for the prefix: no rewrite machinery — it hands vanilla the exact node list, so the
   union-scale and position-staleness questions never exist — and uniform coverage of every
   `GetXpathResultsInList` consumer, including `<conditional>`'s `xpath()` and third-party
   commands, which no wrapper set could reach.
8. **Doc-first staging with the foreach-parity test bar** (§7). `inheritance.md` precedes
   implementation because the conformance suite is doc-clause-driven; the bar is every clause
   covered under the D3 divergence rule, with failure messages that teach.

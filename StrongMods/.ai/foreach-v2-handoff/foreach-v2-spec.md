# StrongMods `<foreach>` v2 — frozen design spec

Handoff document for implementation. Everything under **Ratified** was explicitly agreed; items under
**Open micro-decisions** are recommendations the implementer should confirm before coding around
them. Existing artifacts affected: `XmlPatchMethodForeach.cs`, `XmlPatchFunctionAttribute.cs`
(unchanged), `foreach.md`, and the AutoLoot `items.xml` / `LootItems.cs` pair.

## 1. Ratified decisions

1. **`$` everywhere.** Every binding reference is an XPath 1.0 variable: `{$lootContainer/@name}`,
   never `{lootContainer/@name}`. The hand-rolled "split on first `/`, look up name, evaluate
   relative path" resolver is deleted. A `{...}` expression is an XPath expression, full stop,
   evaluated with every in-scope binding exposed as a variable of the same name. The bare-name
   string-value special case (`{lootContainer}`) disappears; `{$lootContainer}` is an ordinary
   expression whose node result is coerced to string-value by the existing rules. Breaking change
   accepted — nothing has shipped.
2. **`<bind>` binds a node-set, not one node.** Set of any size; one node is a set of size one. No
   child-rows convention: what the xpath selects (or what the inline children are) *is* the set.
3. **Exactly-one enforced at use time, not bind time.** `$loot` may hold 5 rows; the expression
   using it must land on exactly one node. 0 → skip iteration (or coalesce, below); 2+ → skip with
   warning naming the count. Duplicate table keys therefore surface at the ambiguous lookup, not at
   declaration.
4. **Coalesce `?:` — both sides are expressions.** `{left ?: right}`: evaluate left; if it selects
   an **empty node-set**, evaluate right instead. No string literals anywhere in the expression
   grammar. Literal-style defaults are expressed as data: a `<row default="true" .../>` in the bind,
   addressed by `$loot[@default='true']/@icon` on the RHS. An attribute that exists but is empty
   (`icon=""`) is one node and does **not** coalesce — `""` is a legitimate value, consistent with
   the function `null`-vs-`""` contract.
5. **Inline XOR source** for `<bind>` in v1; no merge.
6. **Case-sensitive matching only.** No `ignoreCase` flag; modders use `translate()` if they need it.
7. **CSV is a fast-follow via a row-shaped shim** (no existing CSV representation). Nothing in the
   core may assume XML-only sources in a way that blocks it; see §7.
8. All previously ratified v1 behavior not touched above stands: skip-vs-fatal model, `foreach-name`,
   `{{`/`}}` escapes, `source` without `.xml` plus the hint guard, `GetXpathResults` /
   `ClearXpathResults` with snapshot-and-clear, `singlePatch` dispatch with exception containment,
   scope stack with shadowing errors, `[XmlPatchFunction]` contract (public static, string in/out,
   `null` = skip, throw = skip), document order, no caching, `MaxDepth`.

## 2. Expression language v2

Grammar of the content of `{...}`, after `{{`/`}}` unescaping:

```
expression   := coalesce
coalesce     := operand [ '?:' operand ]        # split once, at top nesting depth only
operand      := call | xpath
call         := NAME '(' args ')'               # NAME matches [A-Za-z_][A-Za-z0-9_]* and next char is '('
xpath        := any XPath 1.0 expression; in-scope bindings available as $name variables
args         := xpath (',' xpath)*              # split on commas at bracket depth 0, as today
```

- The `?:` split happens **before** XPath compilation, at quote-aware top-level depth (`(`/`[`
  tracking; single quotes are legal inside XPath predicates and must not confuse the splitter).
  `?:` inside function argument lists is **not** supported in v1 (the top-level splitter won't see
  it; document as a limit).
- Result coercion per operand: node-set → exactly-one rule (§1.3) then string-value; XPath scalars
  (string/number/boolean) → existing conversions (`FormatXPathNumber`, `true`/`false`). A scalar
  operand is never "empty" and therefore never coalesces.
- Unbound `$name` → fatal (fails the foreach), matching the current unbound-name rule.
- Function **arguments** are full v2 xpath operands (so `tint($lootContainer/@name)`), each subject
  to the exactly-one rule; an argument matching 0 nodes skips the iteration before the function is
  invoked, exactly as v1 argument resolution did.

## 3. `<bind>`

Direct child of `<foreach>`, sibling of `<function>`, stripped from the command body the same way.

| Attribute | Required | Meaning |
|---|---|---|
| `name` | yes | Same regex and shared namespace as `as`/`<function name>`; any collision with anything in scope is fatal. |
| `source` | no | Patched-file name **without `.xml`**; same `TryGetPatchedFile` lookup and `.xml`-suffix hint guard as `<foreach source>`. |
| `xpath` | with `source` | Evaluated once against the source document; the resulting node-set is the binding's value. |

- **Inline form:** no `source`/`xpath`; the element children of `<bind>` are the node-set. Mixing
  inline children with `source`/`xpath` is fatal.
- Resolved **once per foreach**, before iteration starts (like `<function>`), and constant across
  iterations. Bind attribute values go through the own-attribute substitution pass against the
  *enclosing* scope, like every other declaration attribute.
- Binds are ordinary scope entries: visible to nested foreach bodies, popped with the frame,
  exposed as `$name` XPath variables like iteration bindings. `ScopeFrame` grows a bind table (or
  binds become frames-without-iteration — implementer's choice, but lookup must be uniform).
- Bind xpath selecting an **empty set** is allowed (see Open micro-decisions for the logging
  recommendation); every use then 0-matches and skips/coalesces normally.

## 4. Functions under v2

Contract unchanged (`[XmlPatchFunction]`, resolution, signature validation, `null` = skip,
throw = skip, arity checked at call site). Only the argument syntax changes: arguments are v2
expressions, so all binding references inside them carry `$`. The existing call detection
(leading NAME + `(`), bracket-aware argument splitter, and no-nested-calls rejection carry over
unless the nested-call micro-decision below is taken.

## 5. Failure model (delta only)

| Condition | Result |
|---|---|
| Expression selects 0 nodes, no `?:` | Skip iteration |
| Expression selects 0 nodes, `?:` present | Evaluate RHS; RHS then follows the same rules (0 nodes on RHS too → skip) |
| Expression selects 2+ nodes (either side) | Skip iteration, warn with count |
| Unbound `$name` | Fatal |
| Malformed XPath in either operand | Fatal |
| `<bind>` with both inline children and `source`/`xpath`, unknown source, bad name, collision | Fatal |

Everything else per v1.

## 6. Implementation notes (pointers, not mandates)

- Variables require moving off `XNode.XPathEvaluate(string)` to compiled expressions:
  `XPathExpression.Compile(expr)` + `SetContext(customContext)` + `navigator.Evaluate/Select`,
  where the custom context is an `XsltContext` subclass whose `ResolveVariable` returns an
  `IXsltContextVariable` reading the live scope stack. Variables holding node-sets return an
  `XPathNodeIterator`; nodes get navigators via `XObject.CreateNavigator()` (nodes from a
  *different document* than the evaluation context are legal in XPath 1.0 variables — that's the
  whole point of `source` binds).
- Since every expression now goes through this path, the old
  `TryResolveBinding` relative-evaluation branch is deleted, not kept as a fallback. One engine.
- Evaluation needs *some* context navigator even for expressions that only touch variables; the
  target document root is a reasonable constant choice.
- Inline bind children live inside the patch document; snapshot them (clone or hold references —
  they're read-only) at resolution time so later patching of the patch file's target can't alias.
- The AutoLoot expressions are the stress test for the `?:` splitter: quotes, nested brackets, and
  `@`-heavy predicates on both sides.

## 7. CSV fast-follow constraints (do not paint over these)

- A loaded `.csv` becomes a row-shaped document — recommended shape: `<rows><row colA="…"
  colB="…"/></rows>`, header row → attribute names (sanitized to valid XML names), one `<row>` per
  line — registered in the **same** patched-files dictionary so `bind source="loot_tints"
  xpath="/rows/row"` works with zero core changes.
- Therefore: nothing in `<bind>` or the expression engine may special-case "real" config files,
  and the source lookup must stay a plain name→`XmlFile` dictionary hit.

## 8. Breaking-change checklist for existing artifacts

1. `XmlPatchMethodForeach.cs`: replace the expression engine per §2/§6; add `<bind>` per §3; update
   the class doc comment (syntax examples all gain `$`; add bind and `?:`).
2. `foreach.md`: every `{binding...}` gains `$`; new **Binding extra data: `<bind>`** section with
   the row-table pattern and `<row default="true">`; coalesce documented in "Filling in values" and
   the reference tables; the interpolation reference table gains `?:` and `$` rows; Example 3
   replaced by the ratified version below; the `string()` escape-hatch paragraph updated to note
   `?:` as the usual tool and `string()` as the "empty not skip, no data row" variant.
3. AutoLoot `items.xml`: replace with §9.
4. `LootItems.cs`: delete `CustomIcon`, `CustomTint`, `s_meshesToTints`, `IconConfig`, and the
   mesh/icon/tint constants; keep `TryGetLootItem` + `InitEntitiesToItems` (+
   `PropAutoLootSubstituteFor`). No `[XmlPatchFunction]` or `StrongMods` using remains.

## 9. Acceptance example (ratified)

```xml
<foreach source="entityclasses"
         xpath="/entity_classes/entity_class[property[@name='Class'][@value='EntityLootContainer']]"
         as="lootContainer">
  <bind name="loot">
    <row mesh="@:Entities/LootContainers/zpackPrefab.prefab"     icon="cntSportsBag02White" tint="#78783A" />
    <row mesh="@:Entities/LootContainers/zpackBluePrefab.prefab" icon="cntSportsBag02White" tint="#276182" />
    <row mesh="@:Entities/LootContainers/zpackRedPrefab.prefab"  icon="cntSportsBag02White" tint="#793B3E" />
    <row mesh="@:Entities/LootContainers/zpackGoldPrefab.prefab" icon="cntSportsBag02White" tint="#D34B08" />
    <row mesh="@:Entities/LootContainers/duffle01Prefab.prefab"  icon="cntDuffle01"         tint="#FFFFFF" />
    <row default="true"                                          icon="cntSportsBag02White" tint="#4B5320" />
  </bind>
  <append xpath="/items">
    <item name="AutoLoot_{$lootContainer/@name}">
      <property name="Extends" value="AutoLoot_Base" />
      <property name="CreativeMode" value="Player" />
      <property name="AutoLootSubstituteFor" value="{$lootContainer/@name}" />
      <property name="CustomIcon"
                value="{$loot[@mesh = $lootContainer/property[@name='Mesh']/@value]/@icon ?: $loot[@default='true']/@icon}" />
      <property name="CustomIconTint"
                value="{$loot[@mesh = $lootContainer/property[@name='Mesh']/@value]/@tint ?: $loot[@default='true']/@tint}" />
      <property class="Action0">
        <property name="Class" value="OpenLootBundle" />
        <property name="Delay" value="0" />
        <property name="Sound_start" value="close_garbage" />
        <property name="LootList" value="{$lootContainer/property[@name='LootList']/@value}" />
      </property>
    </item>
  </append>
</foreach>
```

Expected behavior: a container whose `Mesh` matches a row uses that row's icon/tint; a container
with a `Mesh` not in the table falls through `?:` to the `default="true"` row; a container with no
`LootList` property skips (warning) at the `LootList` interpolation; the `default` row never
matches `[@mesh = ...]` because it has no `mesh` attribute.

## 10. Open micro-decisions (recommendations — confirm before coding around them)

1. **Nested function calls.** V1 rejects `{a(b(x))}` via an explicit check in the argument loop.
   Under v2 the check still works (arguments are parsed by us before evaluation). *Recommendation:
   keep rejecting in v1* — nothing ratified changes it, and lifting it is trivial later.
2. **Function `null` with `?:` present.** Ratified contract: `null` return skips the iteration. If
   a call appears as the LHS of `?:`, should `null` coalesce instead of skip? *Recommendation:
   coalesce* — it's the function-world analog of "0 matches → default", and a function author who
   wants a hard skip can still be called without `?:`. If declined, document that `?:` never
   rescues a `null`.
3. **Empty bind set.** Allowed per §3. *Recommendation:* `Log.Out` (info, not warning) at
   resolution, mirroring the empty-foreach-match message, so a typo'd bind xpath is discoverable
   without being noisy.
4. **`?:` chaining** (`a ?: b ?: c`). Not discussed. *Recommendation: reject* more than one `?:`
   per expression in v1 with a clear error; trivially relaxable.

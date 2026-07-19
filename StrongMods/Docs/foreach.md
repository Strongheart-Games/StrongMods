# Looping in XML patches: `<foreach>`

`<foreach>` runs a block of patch commands once for every node an XPath expression matches. It turns
repetitive patches into a template, and it lets you generate content from XML you didn't write —
including XML in a different config file, added by a different mod.

Instead of forty near-identical items:

```xml
<append xpath="/items">
  <item name="AutoLoot_EntityLootContainerNurse"> ... </item>
  <item name="AutoLoot_EntityLootContainerSoldier"> ... </item>
  <item name="AutoLoot_EntityLootContainerBiker"> ... </item>
  <!-- ...thirty-seven more, by hand, forever out of date -->
</append>
```

...you write one:

```xml
<foreach source="entityclasses"
         xpath="/entity_classes/entity_class[property[@name='Class'][@value='EntityLootContainer']]"
         as="lootContainer">
  <append xpath="/items">
    <item name="AutoLoot_{$lootContainer/@name}"> ... </item>
  </append>
</foreach>
```

This guide assumes you already write vanilla XPath patches — `append`, `set`, `xpath="/items/item[@name='x']"`.
It only covers what's new.

## Requirements

Your mod needs **StrongMods** loaded. Add it to your `ModInfo.xml` dependencies. Without it the game
doesn't recognize `<foreach>`, logs a warning, and skips the whole block — your mod loads, it just
does nothing.

### Why `source` can be trusted

StrongMods replaces the vanilla patcher with a **breadth-first** one. Vanilla walks files on the
outside and mods on the inside: it finishes `items.xml` for every mod before it starts
`entityclasses.xml` for any mod. That makes cross-file reads a coin flip.

The breadth-first patcher inverts the loops. It finishes **every file for one mod** before starting
the next mod in load order. So when your `<foreach>` runs:

|                                   | State when your loop runs            |
|-----------------------------------|--------------------------------------|
| Vanilla XML                       | Fully loaded                         |
| Mods **before** you in load order | Fully applied, every file            |
| Mods **after** you in load order  | Not applied — invisible to your loop |

That last row is not a bug, it's arithmetic: a mod that hasn't run yet hasn't added anything to see.
If you need to see another mod's content, load after it.

## Writing a loop

```xml
<foreach source="entityclasses" xpath="..." as="lootContainer">
  <!-- body -->
</foreach>
```

| Attribute | Required | What it does                                                                                                                                                       |
|-----------|----------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `xpath`   | yes      | Selects the nodes to loop over. Runs once, in document order.                                                                                                      |
| `as`      | yes      | Names the current node so expressions can refer to it as `$name`. Letters, digits, underscores; must not start with a digit.                                       |
| `source`  | no       | Which config file to select from, named **without the `.xml`** — `source="items"`, not `source="items.xml"`. Defaults to the file your patch is already targeting. |

The body is **ordinary patch commands** — `append`, `set`, `setattribute`, `remove`, `insertBefore`,
`insertAfter`, and `<foreach>` itself. Anything you can write in a patch file, you can write here.

### Body XPaths target the file, not the node

This is the one that trips everyone. `as` binds a node for *reading values out of*. It does not
change what your commands target.

```xml
<foreach xpath="/items/item[starts-with(@name, 'strong_')]" as="item">
  <append xpath="$item">        <!-- WRONG: command xpaths are vanilla XPath, no variables -->
  <append xpath="/items/item[@name='{$item/@name}']">   <!-- right -->
```

Body XPaths are absolute against your target document, exactly as they are outside a loop. To aim a
command at the node you're looping over, interpolate your way back to it with a `{...}` expression.

Nesting works, and inner loops can read outer bindings. Reusing a name that's already in scope is an
error, not a shadow.

## Filling in values

A `{...}` anywhere in the body is an **XPath expression**, and every name in scope is available as
an XPath variable:

```xml
value="{$lootContainer/@name}"
value="{$lootContainer/property[@name='LootList']/@value}"
value="{count($lootContainer/property)}"
```

Interpolation works in four places: **attribute values**, **element text**, **body command XPaths**,
and element names. Element names need a workaround, since XML won't let you type `{` in a tag —
use the reserved `foreach-name` attribute, and the tag renames itself:

```xml
<placeholder foreach-name="{$lootContainer/@name}_extra" />
```

For a literal brace, double it: `{{` and `}}`.

### Exactly one node, or the iteration is skipped

Every `{...}` must resolve to **exactly one node**. Zero matches and the patcher skips that
iteration entirely — no half-written item — and logs a warning saying which node and which
expression. Two or more matches skips too: an ambiguous lookup should never guess.

That's usually what you want: an entity class with no `LootList` shouldn't produce a broken item.
Scalar XPath results (`count()`, `string()`, `string-length()`, boolean tests) always produce
exactly one value and never skip.

### Defaults with `?:`

When zero matches should mean "use a fallback" instead of "skip", write both sides:

```xml
value="{$lootContainer/property[@name='Tier']/@value ?: $lootContainer/@name}"
```

The right side only runs when the left selects **no nodes**. Both sides are ordinary expressions —
there are no string literals in this language, so a fixed default lives in your data, where anyone
can patch it (see `<bind>` below). Note the boundary cases: an attribute that exists but is empty
(`tier=""`) is one node with the value `""` and does *not* fall through; two or more matches skip
the iteration rather than falling through; and if both sides come up empty, the iteration skips.
One `?:` per expression.

---

## Example 1: kill the boilerplate

Six properties on two items, without typing them twice. Nothing here reaches outside `items.xml`,
so `source` is omitted.

```xml
<config>
  <append xpath="/items">
    <item name="example_item_1" />
    <item name="example_item_2" />
  </append>

  <foreach xpath="/items/item[starts-with(@name, 'example_item')]" as="item">
    <append xpath="/items/item[@name='{$item/@name}']">
      <property name="p1" value="v1" />
      <property name="p2" value="v2" />
      <property name="p3" value="v3" />
      <property name="p4" value="v4" />
      <property name="p5" value="v5" />
      <property name="p6" value="v6" />
    </append>
  </foreach>
</config>
```

Patch commands run top to bottom, so both items exist by the time the loop selects them. A loop with
no `source` reads the file as it stands *right now*, mid-patch — including things you added four
lines ago.

Add `example_item_3` to the `<append>` and it gets all six properties for free.

## Example 2: generate one file from another

Items describe themselves; recipes derive from them. Put the cost on the item, and let the recipe
file read it — one source of truth, no drift.

In `items.xml`, your tools declare what they cost:

```xml
<item name="StrongHammer">
  <property name="Extends" value="StrongTool_Base" />
  <property name="StrongSteelCost" value="8" />
</item>
```

In `recipes.xml`, one loop builds every recipe:

```xml
<config>
  <foreach source="items"
           xpath="/items/item[property[@name='Extends'][@value='StrongTool_Base']]"
           as="tool">
    <append xpath="/recipes">
      <recipe name="{$tool/@name}" count="1" craft_area="workbench" tags="learnable">
        <ingredient name="resourceForgedSteel" count="{$tool/property[@name='StrongSteelCost']/@value}" />
        <ingredient name="resourceDuctTape" count="2" />
      </recipe>
    </append>
  </foreach>
</config>
```

`source="items"` reads across files; `xpath` in the body still targets `/recipes` in the file this
patch belongs to. A tool that forgets `StrongSteelCost` gets no recipe and a warning in the log,
rather than a recipe costing nothing.

---

## Tables: `<bind>`

A `<bind>` names extra data your expressions can use — most often a lookup table. It's a **direct
child** of `<foreach>`, resolved **once** before the loop starts, and constant across iterations.
The name joins the same scope as `as` names (collisions are errors) and appears in expressions as a
`$variable`, exactly like the loop binding.

Two forms:

```xml
<!-- Inline: the element children are the data. You choose the element names. -->
<bind name="loot">
  <row mesh="..." icon="..." tint="..." />
  <row mesh="..." icon="..." tint="..." />
</bind>

<!-- Source: an xpath selects the data from any config file (again, no .xml suffix). -->
<bind name="loot" source="loot_tables" xpath="/loot_tables/autoloot/row" />
```

One form or the other — inline children plus `source`/`xpath` on the same bind is an error.

A bind holds a **set** of nodes, and that's the point: `$loot` *is* the rows. Look one up by
filtering the variable with a predicate, then take the column you want:

```xml
value="{$loot[@mesh = $lootContainer/property[@name='Mesh']/@value]/@icon}"
```

The exactly-one rule applies at the lookup: no matching row skips the iteration (or coalesces, see
below), and **two rows with the same key** skip it too — check the log if a table misbehaves.

### The default row

Combine `<bind>` with `?:` to give a table a fallback. Mark one row as the default and point the
right side at it:

```xml
<bind name="loot">
  <row mesh="@:Entities/LootContainers/duffle01Prefab.prefab" icon="cntDuffle01" tint="#FFFFFF" />
  <row default="true" icon="cntSportsBag02White" tint="#4B5320" />
</bind>

value="{$loot[@mesh = $lootContainer/property[@name='Mesh']/@value]/@icon ?: $loot[@default='true']/@icon}"
```

The default row has no `mesh` attribute, so the key predicate can never match it — it's only
reachable through the `?:` fallback. This is the idiom for "look it up, or use the default."

## Example 3: AutoLoot — a generated item per loot container

Everything above in one real patch: cross-file `source`, a `<bind>` table with a default row, and
`?:` lookups. No C# anywhere.

```xml
<config>
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
</config>
```

Per loot container: a `Mesh` in the table uses that row's icon and tint; an unknown `Mesh` falls
through `?:` to the default row; a container with no `LootList` at all skips with a warning — that
expression has no fallback on purpose, because an auto-loot item that opens nothing is worse than no
item.

---

## Custom functions

**Requires a C# mod.** If your mod is XML-only, skip this section — `<bind>` covers table lookups
without code. Reach for a function when the *logic* can't be expressed in XPath: hashing, real
string manipulation, reading game state.

```xml
<foreach source="entityclasses" xpath="..." as="lootContainer">
  <function name="tint" method="StrongAutoLoot.Tints.FromName, StrongAutoLoot" />
  <append xpath="/items">
    <item name="...">
      <property name="CustomIconTint" value="{tint($lootContainer/@name)}" />
    </item>
  </append>
</foreach>
```

`<function>` must be a **direct child** of `<foreach>`, and its name lives for that loop only, in
the same scope as `as` names and binds. Arguments are ordinary XPath expressions — anything you
could write inside `{...}`, minus `?:` and calls to other functions.

### The `method` reference

```
[namespace.]Class.Method, [mod]
```

Same shape as `ServerClass` and friends, plus the method on the end. The mod is optional; leave it
off and the game looks in `Assembly-CSharp`.

| Reference                                       | Resolves to                                                   |
|-------------------------------------------------|---------------------------------------------------------------|
| `StrongAutoLoot.Tints.FromName, StrongAutoLoot` | `FromName` on `StrongAutoLoot.Tints`, in mod `StrongAutoLoot` |
| `MyMod.Helpers.Slug`                            | `Slug` on `MyMod.Helpers`, in `Assembly-CSharp`               |

### Writing one

Tag it. An untagged method is rejected even if the signature is perfect — that's deliberate, so
nothing in your assembly becomes XML-callable by accident.

```csharp
using StrongMods;

namespace StrongAutoLoot {
  public static class Tints {
    private static readonly string[] s_tints = { "FF6B6B", "FFD166", "06D6A0", "118AB2" };

    [XmlPatchFunction]
    public static string FromName(string name) {
      if (string.IsNullOrEmpty(name)) {
        return null;
      }

      // FNV-1a, not string.GetHashCode(), which is not stable across processes.
      var hash = 2166136261u;
      unchecked {
        foreach (var c in name) {
          hash = (hash ^ c) * 16777619u;
        }
      }

      return s_tints[hash % s_tints.Length];
    }
  }
}
```

The contract:

- `public static`, returns `string`, every parameter `string`. **Strings only** — no `XElement`, no
  `int`, no `params`.
- Not generic, not overloaded.
- Return `null` for "no value": the iteration skips, or — if the call sits on the left of a `?:` —
  the fallback runs. Return `""` for a legitimately empty value.
- Be pure. Functions run once per matched node at startup, and the patcher promises nothing about
  call count or order. Side effects won't show up in `ConfigDump/`.

**v1 limits:** no nested calls (`{a(b(x))}`), no `?:` inside an argument list, a call must be a
whole side of an expression (no `{tint($x) = 'y'}`). Say the word if you hit a real need.

---

## When it doesn't work

Two kinds of failure, and they behave differently.

**Skips** are per-node. One iteration is abandoned, the rest carry on. These are data conditions —
a node that didn't have what your template needed.

**Errors** kill the whole loop. These are mod bugs — something that would fail identically for every
node, so there's no point trying the other thirty-nine.

| What happened                                                               | Result                                              |
|-----------------------------------------------------------------------------|-----------------------------------------------------|
| `{...}` matched 0 nodes, no `?:`                                            | Skip                                                |
| `{...}` matched 0 nodes, `?:` present                                       | Right side runs; if it's also empty → Skip          |
| 2+ nodes matched, on either side of `?:`                                    | Skip — ambiguity never falls through to the default |
| Function returned `null` (no `?:`)                                          | Skip                                                |
| Function returned `null` (left of `?:`)                                     | Right side runs                                     |
| Function threw                                                              | Skip                                                |
| Substituted element name isn't valid XML                                    | Skip                                                |
| Unknown `source` file, on `<foreach>` or `<bind>`                           | Error                                               |
| Bad `xpath`, missing `as`, name collision                                   | Error                                               |
| `$name` that isn't bound; unknown function                                  | Error                                               |
| Malformed expression: unbalanced brackets, unterminated quote, chained `?:` | Error                                               |
| `<bind>` with both inline content and `source`/`xpath`, or neither          | Error                                               |
| Function won't resolve, isn't tagged, wrong signature, wrong argument count | Error                                               |

Skips are quiet by design — nothing crashes, you just get fewer items than you expected. **If your
loop produced 12 things instead of 40, read the log.** Every skip names the file, the line, the
iteration, the node, and the expression that failed:

```
WRN XML patch foreach (StrongAutoLoot, items.xml, line 3): iteration 3 of 3
    (<entity_class name="EntityLootContainerDecoy">) skipped —
    "$lootContainer/property[@name='LootList']/@value" matched 0 nodes, expected 1.
```

To see what a loop actually produced, turn on the game's config dump and read `ConfigDump/`. Loops
are expanded before the patch is applied, so the dump shows the finished XML — every generated
item, exactly as the game sees it.

## Gotchas

**`Extends` is not resolved.** `property[@name='Class']` matches a property written *on that element*.
If an entity class inherits `Class` from a parent via `Extends`, your predicate won't match it — the
same way vanilla XPath patches behave. Check what's really in the file before assuming your loop
covers everything, and widen the predicate if you need to:

```xml
xpath="/entity_classes/entity_class[
         property[@name='Class'][@value='EntityLootContainer']
         or @extends='EntityLootContainer']"
```

**Loops and binds see a snapshot.** The `xpath` on a `<foreach>` runs once, before the body does; a
`<bind>` resolves once, before the first iteration. If the body modifies the file either one read
from, neither notices.

**Duplicate table keys skip.** Two `<row>`s with the same key value make every lookup of that key
ambiguous, and ambiguous lookups skip the iteration rather than guessing — even past a `?:`.

**Mods after you are invisible.** See the table at the top.

**Document order only.** No sorting. Output order follows the source file.

## Reference

### `<foreach>`

| Attribute | Required | Default                                                                       |
|-----------|----------|-------------------------------------------------------------------------------|
| `xpath`   | yes      | —                                                                             |
| `as`      | yes      | —                                                                             |
| `source`  | no       | The patch's own target file. Named without `.xml` (`items`, not `items.xml`). |

### `<bind>`

| Attribute | Required                | Notes                                                               |
|-----------|-------------------------|---------------------------------------------------------------------|
| `name`    | yes                     | Direct child of `<foreach>`; scoped to that loop; usable as `$name` |
| `source`  | no                      | Named without `.xml`; defaults to the patch's own target file       |
| `xpath`   | with `source`, or alone | Selects the node-set; mutually exclusive with inline children       |

### `<function>`

| Attribute | Required | Notes                                                                           |
|-----------|----------|---------------------------------------------------------------------------------|
| `name`    | yes      | Direct child of `<foreach>`; scoped to that loop                                |
| `method`  | yes      | `[namespace.]Class.Method, [mod]` — mod optional, defaults to `Assembly-CSharp` |

### Expressions

| Syntax                           | Meaning                                                          |
|----------------------------------|------------------------------------------------------------------|
| `{$name}`                        | String-value of the bound node                                   |
| `{$name/xpath}`                  | Any XPath 1.0 over the bindings — must land on exactly one node  |
| `{$table[@key = $item/@k]/@col}` | Table lookup against a `<bind>`                                  |
| `{left ?: right}`                | If `left` selects no nodes (or a call returns null), use `right` |
| `{count($x/y)}`                  | XPath scalars are fine and never skip                            |
| `{fn($arg, $arg)}`               | Call a declared function; arguments are expressions              |
| `{{` `}}`                        | Literal `{` `}`                                                  |
| `foreach-name="..."`             | Sets the element's tag name                                      |

Valid in attribute values, element text, body command XPaths, and `foreach-name`. Body command
`xpath` attributes themselves are vanilla XPath — variables only exist inside `{...}`.

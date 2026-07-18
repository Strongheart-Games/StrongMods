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
    <item name="AutoLoot_{lootContainer/@name}"> ... </item>
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

| | State when your loop runs |
|---|---|
| Vanilla XML | Fully loaded |
| Mods **before** you in load order | Fully applied, every file |
| Mods **after** you in load order | Not applied — invisible to your loop |

That last row is not a bug, it's arithmetic: a mod that hasn't run yet hasn't added anything to see.
If you need to see another mod's content, load after it.

## Writing a loop

```xml
<foreach source="entityclasses" xpath="..." as="lootContainer">
  <!-- body -->
</foreach>
```

| Attribute | Required | What it does |
|---|---|---|
| `xpath` | yes | Selects the nodes to loop over. Runs once, in document order. |
| `as` | yes | Names the current node so the body can refer to it. Letters, digits, underscores; must not start with a digit. |
| `source` | no | Which config file to select from, named **without the `.xml`** — `source="items"`, not `source="items.xml"`. Defaults to the file your patch is already targeting. |

The body is **ordinary patch commands** — `append`, `set`, `setattribute`, `remove`, `insertBefore`,
`insertAfter`, and `<foreach>` itself. Anything you can write in a patch file, you can write here.

### Body XPaths target the file, not the node

This is the one that trips everyone. `as` binds a node for *reading values out of*. It does not
change what your commands target.

```xml
<foreach xpath="/items/item[starts-with(@name, 'strong_')]" as="item">
  <append xpath="item">          <!-- WRONG: selects nothing -->
  <append xpath="/items/item[@name='{item/@name}']">   <!-- right -->
```

Body XPaths are absolute against your target document, exactly as they are outside a loop. To aim a
command at the node you're looping over, interpolate your way back to it.

Nesting works, and inner loops can read outer bindings. Reusing a name that's already in scope is an
error, not a shadow.

## Filling in values

Write `{binding}` to get the node's text, or `{binding/some/xpath}` to run a relative XPath from it:

```xml
value="{lootContainer/@name}"
value="{lootContainer/property[@name='LootList']/@value}"
```

Interpolation works in four places: **attribute values**, **element text**, **body command XPaths**,
and element names. Element names need a workaround, since XML won't let you type `{` in a tag —
use the reserved `foreach-name` attribute, and the tag renames itself:

```xml
<placeholder foreach-name="{lootContainer/@name}_extra" />
```

For a literal brace, double it: `{{` and `}}`.

### Exactly one node, or the iteration is skipped

Every `{...}` must resolve to **exactly one node**. Zero matches or two matches, and the patcher
skips that iteration entirely — no half-written item — and logs a warning saying which node and
which expression.

That's usually what you want: an entity class with no `LootList` shouldn't produce a broken item.
When you'd rather have an empty string than a skip, wrap it in XPath's `string()`, which returns
`""` instead of nothing:

```xml
value="{lootContainer/string(property[@name='Tier']/@value)}"
```

Scalar XPath functions like `count()` and `string-length()` work too — they always return one value.

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
    <append xpath="/items/item[@name='{item/@name}']">
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
      <recipe name="{tool/@name}" count="1" craft_area="workbench" tags="learnable">
        <ingredient name="resourceForgedSteel" count="{tool/property[@name='StrongSteelCost']/@value}" />
        <ingredient name="resourceDuctTape" count="2" />
      </recipe>
    </append>
  </foreach>
</config>
```

`source="items"` reads across files; `xpath` in the body still targets `/recipes` in the file
this patch belongs to. A tool that forgets `StrongSteelCost` gets no recipe and a warning in the log,
rather than a recipe costing nothing.

---

## Custom functions

**Requires a C# mod.** If your mod is XML-only, skip this section.

Some values can't be computed in XPath. Declare a function, and call it like one:

```xml
<foreach source="entityclasses" xpath="..." as="lootContainer">
  <function name="tint" method="StrongAutoLoot.AutoLootIcons.Tint, StrongAutoLoot" />
  <append xpath="/items">
    <item name="...">
      <property name="CustomIconTint" value="{tint(lootContainer/@name)}" />
    </item>
  </append>
</foreach>
```

`<function>` must be a **direct child** of `<foreach>`, and its name lives for that loop only.
Function names and `as` names share one namespace — you can't have both called `tint`.

### The `method` reference

```
[namespace.]Class.Method, [mod]
```

Same shape as `ServerClass` and friends, plus the method on the end. The mod is optional; leave it
off and the game looks in `Assembly-CSharp`.

| Reference | Resolves to |
|---|---|
| `StrongAutoLoot.AutoLootIcons.Tint, StrongAutoLoot` | `Tint` on `StrongAutoLoot.AutoLootIcons`, in mod `StrongAutoLoot` |
| `MyMod.Helpers.Slug` | `Slug` on `MyMod.Helpers`, in `Assembly-CSharp` |

### Writing one

Tag it. An untagged method is rejected even if the signature is perfect — that's deliberate, so
nothing in your assembly becomes XML-callable by accident.

```csharp
using StrongMods;

namespace StrongAutoLoot {
  public static class AutoLootIcons {
    [XmlPatchFunction]
    public static string Tint(string entityClassName) {
      ...
    }
  }
}
```

The contract:

- `public static`, returns `string`, every parameter `string`. **Strings only** — no `XElement`, no
  `int`, no `params`.
- Not generic, not overloaded.
- Return `null` to **skip the iteration**, exactly as a zero-match XPath would. Return `""` for a
  legitimately empty value.
- Be pure. Functions run once per matched node at startup, and the patcher promises nothing about
  call count or order. Side effects won't show up in `ConfigDump/`.

**v1 limits:** arguments are binding references only. No nested calls (`{a(b(x))}`), no string
literals (`{a('foo')}`), no caching. Say the word if you hit a real need.

## Example 3: AutoLoot

Generate an auto-loot item for every loot container in the game, with an icon and tint chosen by
code.

```xml
<config>
  <foreach source="entityclasses"
           xpath="/entity_classes/entity_class[property[@name='Class'][@value='EntityLootContainer']]"
           as="lootContainer">
    <function name="icon" method="StrongAutoLoot.AutoLootIcons.Icon, StrongAutoLoot" />
    <function name="tint" method="StrongAutoLoot.AutoLootIcons.Tint, StrongAutoLoot" />
    <append xpath="/items">
      <item name="AutoLoot_{lootContainer/@name}">
        <property name="Extends" value="AutoLoot_Base" />
        <property name="CreativeMode" value="Player" />
        <property name="AutoLootSubstituteFor" value="{lootContainer/@name}" />
        <property name="CustomIcon" value="{icon(lootContainer/@name)}" />
        <property name="CustomIconTint" value="{tint(lootContainer/property[@name='LootList']/@value)}" />
        <property class="Action0">
          <property name="Class" value="OpenLootBundle" />
          <property name="Delay" value="0" />
          <property name="Sound_start" value="close_garbage" />
          <property name="LootList" value="{lootContainer/property[@name='LootList']/@value}" />
        </property>
      </item>
    </append>
  </foreach>
</config>
```

The C# side — a lookup for icons, a hash for tints. *Icon names below are illustrative; use real
atlas names.*

```csharp
using System;
using System.Collections.Generic;
using StrongMods;

namespace StrongAutoLoot {
  /// <summary>Icon and tint selection for generated AutoLoot items.</summary>
  public static class AutoLootIcons {
    private const string EntityPrefix = "EntityLootContainer";
    private const string DefaultIcon = "bag";

    private static readonly Dictionary<string, string> s_icons = new() {
      ["Nurse"] = "medicalFirstAidBandage",
      ["Soldier"] = "armorMilitaryHelmet",
      ["Biker"] = "apparelBikerHelmet",
    };

    private static readonly string[] s_tints = {
      "FF6B6B", "FFD166", "06D6A0", "118AB2", "C77DFF", "EF476F",
    };

    [XmlPatchFunction]
    public static string Icon(string entityClassName) {
      if (!entityClassName.StartsWith(EntityPrefix, StringComparison.Ordinal)) {
        return DefaultIcon;
      }

      var suffix = entityClassName.Substring(EntityPrefix.Length);
      return s_icons.TryGetValue(suffix, out var icon) ? icon : DefaultIcon;
    }

    [XmlPatchFunction]
    public static string Tint(string lootList) {
      if (string.IsNullOrEmpty(lootList)) {
        return null;
      }

      // FNV-1a, not string.GetHashCode(), which is not stable across processes.
      var hash = 2166136261u;
      unchecked {
        foreach (var c in lootList) {
          hash = (hash ^ c) * 16777619u;
        }
      }

      return s_tints[hash % s_tints.Length];
    }
  }
}
```

Given three entity classes — Nurse (`zPackReg`), Soldier (`zPackSoldier`), and Decoy (no `LootList`
at all) — you get:

```xml
<item name="AutoLoot_EntityLootContainerNurse">
  ...
  <property name="CustomIcon" value="medicalFirstAidBandage" />
  <property name="CustomIconTint" value="C77DFF" />
  <property class="Action0">
    ...
    <property name="LootList" value="zPackReg" />
  </property>
</item>

<item name="AutoLoot_EntityLootContainerSoldier">
  ...
  <property name="CustomIcon" value="armorMilitaryHelmet" />
  <property name="CustomIconTint" value="06D6A0" />
  <property class="Action0">
    ...
    <property name="LootList" value="zPackSoldier" />
  </property>
</item>
```

Decoy is skipped at `{lootContainer/property[@name='LootList']/@value}`, before `Tint` is ever
called. Its `null` guard is belt-and-braces.

---

## When it doesn't work

Two kinds of failure, and they behave differently.

**Skips** are per-node. One iteration is abandoned, the rest carry on. These are data conditions —
a node that didn't have what your template needed.

**Errors** kill the whole loop. These are mod bugs — something that would fail identically for every
node, so there's no point trying the other thirty-nine.

| What happened | Result |
|---|---|
| `{...}` matched 0 or 2+ nodes | Skip |
| Function returned `null` | Skip |
| Function threw | Skip |
| Substituted element name isn't valid XML | Skip |
| Unknown `source` file | Error |
| Bad `xpath`, missing `as`, name collision | Error |
| `{...}` names something that isn't bound | Error |
| Function won't resolve, isn't tagged, or has the wrong signature | Error |
| Wrong number of arguments at a call site | Error |

Skips are quiet by design — nothing crashes, you just get fewer items than you expected. **If your
loop produced 12 things instead of 40, read the log.** Every skip names the file, the line, the
iteration, the node, and the expression that failed:

```
WRN XML patch foreach (StrongAutoLoot, items.xml, line 3): iteration 3 of 3
    (<entity_class name="EntityLootContainerDecoy">) skipped —
    {lootContainer/property[@name='LootList']/@value} matched 0 nodes, expected 1.
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

**Loops see a snapshot.** The `xpath` runs once, before the body does. If the body modifies the file
it's looping over, the loop won't notice.

**Mods after you are invisible.** See the table at the top.

**Document order only.** No sorting. Output order follows the source file.

## Reference

### `<foreach>`

| Attribute | Required | Default |
|---|---|---|
| `xpath` | yes | — |
| `as` | yes | — |
| `source` | no | The patch's own target file. Named without `.xml` (`items`, not `items.xml`). |

### `<function>`

| Attribute | Required | Notes |
|---|---|---|
| `name` | yes | Direct child of `<foreach>`; scoped to that loop |
| `method` | yes | `[namespace.]Class.Method, [mod]` — mod optional, defaults to `Assembly-CSharp` |

### Interpolation

| Syntax | Meaning |
|---|---|
| `{name}` | Text of the bound node |
| `{name/xpath}` | Relative XPath from the bound node — must match exactly one node |
| `{name/string(xpath)}` | Same, but empty instead of skipping |
| `{fn(arg, arg)}` | Call a declared function |
| `{{` `}}` | Literal `{` `}` |
| `foreach-name="..."` | Sets the element's tag name |

Valid in attribute values, element text, body command XPaths, and `foreach-name`.

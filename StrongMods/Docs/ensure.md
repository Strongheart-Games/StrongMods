# Idempotent patching: `<ensure>`

`<ensure>` makes a child element exist, with the attributes you give it, on every node an XPath matches. If it is
missing, it is added. If it is already there, its attributes are brought up to date. You write what the config should
look like; the patcher works out which of those two it is.

The problem it solves: patching config you don't own usually takes two commands.

```xml
<setattribute xpath="/items/item[@name='schematicMaster']/property[@name='AltItemTypeIconColor']" name="value">0,255,0,200</setattribute>
<append xpath="/items/item[@name='schematicMaster' and not(property[@name='AltItemTypeIconColor'])]">
  <property name="AltItemTypeIconColor" value="0,255,0,200" />
</append>
```

Exactly one of those matches. The other matches nothing and logs `did not apply` — a warning for a patch that did
precisely what its author intended, indistinguishable in a log from a real failure. One `<ensure>` replaces both:

```xml
<ensure xpath="/items/item[@name='schematicMaster']">
  <property name="AltItemTypeIconColor" value="0,255,0,200" />
</ensure>
```

This guide assumes you already write vanilla XPath patches. It only covers what's new.

## Requirements

Your mod needs **StrongMods** loaded. Add it to your `ModInfo.xml` dependencies. Without it the game doesn't recognize
`<ensure>`, logs a warning, and skips the block — your mod loads, that patch just does nothing.

## Writing one

```xml
<ensure xpath="..." position="append">
  <!-- one or more template children -->
</ensure>
```

| Attribute  | Required | What it does                                                                                        |
|------------|----------|-----------------------------------------------------------------------------------------------------|
| `xpath`    | yes      | Selects the **parent** nodes. Every match is processed.                                             |
| `position` | no       | Where newly created children go: `append` (default) or `prepend`. Never moves an existing element.  |

Each child element inside the block is a **template**. For every matched parent, each template is applied in turn:

| The parent has…                     | What happens                                                                  |
|-------------------------------------|-------------------------------------------------------------------------------|
| no child matching the template      | the template is cloned in and inserted per `position`                          |
| exactly one matching child          | the template's attributes are set on it; attributes you didn't name are kept  |
| two or more matching children       | ambiguous — nothing is changed and a warning names the parent and the key      |

Several templates in one block are independent, so related properties travel together:

```xml
<ensure xpath="/items/item[@name='schematicMaster']">
  <property name="AltItemTypeIconColor" value="0,255,0,200" />
  <property name="AltItemTypeIcon" value="checkmark" />
</ensure>
```

### It applies to every match

`xpath` selects parents, plural. This is the whole difference from the two-command idiom, which needs a hand-written
`not(...)` predicate to keep its two halves from colliding:

```xml
<!-- every item that doesn't already say otherwise stacks to 65000 -->
<ensure xpath="/items/item[not(property[@name='Stacknumber'][@value='1'])]">
  <property name="Stacknumber" value="65000" />
</ensure>
```

### It is idempotent

Applying the same block twice leaves the document exactly as applying it once did, and two mods ensuring the same
property converge on the later one's value instead of producing a duplicate. That is what makes `<ensure>` safe to
write in a mod you expect other people to patch around.

## Identity: how a child is recognized

A template is matched against the parent's existing children by **tag name plus key attributes**. By default the key
is `name`, or `class`, or both — whichever the template carries:

```xml
<property name="Stacknumber" value="65000" />   <!-- keyed on name -->
<property class="Action0" />                    <!-- keyed on class -->
```

That covers the overwhelming majority of the game's config. It does not cover everything, because `name` is not always
unique among siblings — items routinely carry several `<passive_effect name="...">` that differ by `operation` and
`tags`, and blocks several `<drop name="...">` that differ by `event`. When the default key can't tell them apart, name
the attributes that can:

```xml
<ensure xpath="/items/item[@name='meleeToolRepairT3Wrench']">
  <passive_effect ensure-key="name,operation,tags"
                  name="EntityDamage" operation="perc_add" tags="salvaging" value="0.5" />
</ensure>
```

`ensure-key` is a comma-separated list of attribute names, and it is stripped from the element before it is written —
it never reaches the game's config. Every attribute it names must be present on the template, since those values *are*
the key.

**Ambiguity is never resolved by guessing.** If two of a parent's children match the key, that parent is left
untouched and warned about. Updating one of a stack of `passive_effect`s at random is the kind of corruption you find
three weeks later, so the patcher refuses. Narrow the key, or narrow the `xpath`.

A template with no `name`, no `class`, and no `ensure-key` cannot be identified at all — `<triggered_effect>` and
`<effect_group>` are the common ones — and that is an error, not a guess.

## Nested templates

Templates nest, and the same rule applies at each level: ensure the child, then ensure *its* children inside it.

```xml
<ensure xpath="/items/item[@name='noteIntroToHades']">
  <property class="Action0">
    <property name="Sound_start" value="read_mod" />
    <property name="Sound_in_head" value="true" />
  </property>
</ensure>
```

Whether `Action0` was already there or had to be created, both of those properties end up inside it with those values.

**`<ensure>` never removes anything.** A child already sitting inside `Action0` that your template doesn't mention
stays exactly where it is. "Ensure" means "make sure this is true", not "make this the whole story" — use `remove` when
you mean to delete.

## Text content

A template with text and no child elements sets the element's text:

```xml
<ensure xpath="/blocks/block[@name='terrDirt']">
  <property name="Group">Building</property>
</ensure>
```

Surrounding whitespace is trimmed, and only a template with **no child elements** can carry text — the whitespace
between nested children is formatting, not content, and is never mistaken for it.

## Ensuring an attribute

`<ensure>` ensures child *elements*. For an attribute you don't need it — vanilla `setattribute` already does the
whole job:

```xml
<setattribute xpath="/items/item[starts-with(@name, 'boss')]" name="extends">masterBoss</setattribute>
```

It **creates the attribute when it is absent** and overwrites it when present, on every matched element, and warns
only when the xpath matched nothing. That is exactly what `<ensure>` would offer, so there is no `<ensure>` spelling
of it.

The form that looks like it ought to work does not, and it is worth knowing why:

```xml
<!-- does NOT do what it looks like -->
<ensure xpath="/items/item[starts-with(@name, 'boss')]/@extends">masterBoss</ensure>
```

XPath selects nodes that *exist*. An attribute-targeting xpath returns only the items that **already have**
`@extends`, so the items you were trying to fix are invisible to it — the add-if-missing half is unreachable by
construction, not by omission. `<ensure>` recognizes both spellings of this mistake and tells you the
`setattribute` to write instead.

(Attribute-targeting xpaths are perfectly normal elsewhere — vanilla `csv` requires one. They suit commands that
only ever modify what is already there, which is precisely why they don't suit this one.)

Setting several attributes on the same element is one `setattribute` each.

## Order matters, and so does `position`

Among duplicate siblings in 7 Days to Die config, **the last one wins**. So:

- **New children are appended by default**, which puts what your mod declares after anything already present — your
  value takes effect.
- **`position="prepend"`** states the opposite intent: contribute a value only if nothing later overrides it. This is
  how you supply a default other mods, or the player's own config, are free to beat.
- **Merging never moves an element.** `position` governs insertion only. If the key matched exactly one child, where
  it sits can't change which value wins, so `<ensure>` leaves it in place rather than shuffling your config around.
  If you genuinely need something moved, `remove` it and let `<ensure>` re-add it, or use `insertBefore`/`insertAfter`.

Several new children in one block keep the order you wrote them, prepended or appended.

## When it doesn't work

Two kinds of failure, and they behave differently — the same split `<foreach>` uses.

**Warnings** are per-parent. One parent is left alone, the rest are still processed. These are data conditions.

**Errors** kill the whole block before anything is changed. These are mod bugs — they would fail identically for every
parent, so there is no point applying half of them.

| What happened                                                    | Result                                                         |
|-------------------------------------------------------------------|----------------------------------------------------------------|
| `xpath` matched at least one parent                              | Applied; no warning                                            |
| `xpath` matched nothing                                          | The ordinary vanilla `did not apply` warning — a real one      |
| `xpath` selected attributes rather than elements                 | One warning for the block, naming the `setattribute` to write instead |
| `xpath` selected some other non-element node                     | One warning for the block; those matches skipped               |
| A parent holds 2+ children matching the key                      | Warning naming the parent and key; that parent left alone      |
| `<ensure>` has no template children                              | Error — and if the block declared a value, it names `setattribute` too |
| A template has no `name`/`class` and no `ensure-key`             | Error                                                          |
| `ensure-key` is empty, or names an attribute the template lacks  | Error                                                          |
| `position` is neither `append` nor `prepend`                     | Error                                                          |
| `xpath` attribute missing, or malformed                          | The patcher rejects it before `<ensure>` runs, as for any command |
| Text or comments directly inside `<ensure>`                      | Ignored                                                        |

Unlike the idiom it replaces, **a warning from `<ensure>` always means something is actually wrong** — your selector
matched nothing, or your key doesn't identify one element. There is no expected-and-ignorable case left to train
yourself to skip past.

## Reference

### `<ensure>`

| Attribute  | Required | Default  | Notes                                                    |
|------------|----------|----------|-----------------------------------------------------------|
| `xpath`    | yes      | —        | Selects parent nodes; every match is processed            |
| `position` | no       | `append` | `append` or `prepend`; applies to newly created children  |

### Template children

| Syntax                        | Meaning                                                                       |
|-------------------------------|--------------------------------------------------------------------------------|
| `<tag name="..." ... />`      | Keyed on `name`                                                               |
| `<tag class="..." ... />`     | Keyed on `class`                                                              |
| `<tag name="" class="" ... />`| Keyed on both                                                                 |
| `ensure-key="a,b"`            | Keyed on the named attributes instead; stripped from the output               |
| `<tag ...>text</tag>`         | Also sets the element's text (only when the template has no child elements)   |
| `<tag ...><child ... /></tag>`| Ensures `child` inside `tag`, by the same rules, at any depth                 |

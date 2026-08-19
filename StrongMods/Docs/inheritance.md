# XPath inheritance functions

StrongMods adds two XPath functions for selectors that need 7 Days to Die's XML inheritance. They are read-only: no
property is copied onto a child, so a later patch to a parent remains visible to every child that still inherits it.

Use the `sm:` prefix without declaring it in the patch file. A mod that uses it must depend on StrongMods in its
`ModInfo.xml`.

```xml
<!-- Items whose effective Class is LootContainer, whether declared here or inherited. -->
<set xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']
            /property[@name='Stacknumber']">1</set>
```

## `sm:chain(node, link, key [, population])`

Returns `node`, then its parent, grandparent, and so on. It includes `node`, so testing the returned set reads "is or
descends from":

```xml
<set xpath="/items/item[sm:chain(., '#Extends', '@name')[@name='ammoBase']]" />
```

Do not use `[1]` or `[last()]` to mean nearest or root. XPath may reorder a function-returned node-set into document
order when a location step or positional predicate follows it. Use `sm:inherited` whenever nearest-wins matters.

## `sm:inherited(node, select, link, key [, population])`

Returns `select` from the nearest chain member that defines it, starting at `node`. This is the effective value:

```xml
<set xpath="/items/item[sm:inherited(., '#Class', '#Extends', '@name') = 'LootContainer']" />
```

The node itself is deliberately first. The function name is compact rather than perfectly literal, so read it as
"declared or inherited" wherever it appears. An empty result means no chain member defines the selected value.

## Arguments

`link` is a relative XPath from a child to its parent's identity; `key` is a relative XPath from a candidate parent to
its own identity. The common item hierarchy uses `'#Extends'` and `'@name'`. `select` is also relative and says what
effective value to retrieve.

For `link` and `select` only, `'#Name'` is shorthand for `property[@name='Name']/@value`. General relative XPath is
always accepted when the XML is not in the game's flat property form. `key` has no shorthand because identities such
as `@name` and `@id` already need no nested quoting.

`population` is an optional absolute XPath naming possible parents. Without it, StrongMods searches siblings of the
start node that have the same element name. Supply it when the hierarchy lives elsewhere in the document.

## Broken data

A missing link ends a chain normally. A multiple link, missing or duplicate parent key, cycle, or chain longer than 64
links logs one warning and truncates only that chain; it does not stop unrelated selector matches. `node` must contain
zero or one node: an empty input returns empty, and multiple nodes are an XPath error.

The functions resolve only within the document being read. They work in vanilla command xpaths, `foreach` and `bind`
xpaths, and foreach `{...}` expressions. Plain command xpaths still do not gain foreach variables merely because they
use `sm:`.

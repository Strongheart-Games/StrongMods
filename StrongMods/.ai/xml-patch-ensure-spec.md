# Idempotent config patching — `<ensure>`

**Version:** 1.0 (Draft 4)\
**Status:** **Implemented** — shipped in StrongMods 1.1.0 (#60). This is the design record: what was weighed, what
was rejected, and why. `StrongMods/Docs/ensure.md` is the living specification; when the two disagree, the doc is
right and this is history.\
**Tracked by:** [#60](https://github.com/Strongheart-Games/StrongMods/issues/60)\
**Applies to:** StrongMods XML patch extensions (alongside `<foreach>`)\
**Audience:** Mod authors writing XPath config patches; StrongMods maintainers

Supersedes the `<oneof>` draft. `<oneof>` survives here as a rejected-for-now alternative (§8).

## 1. The problem

A patch that has to work against config it does not own often needs two mutually exclusive commands to express one
intent. From `StrongholdTweaks/Config/items.xml`:

```xml
<setattribute xpath="/items/item[@name='schematicMaster']/property[@name='AltItemTypeIconColor']" name="value">0,255,0,200</setattribute>
<append xpath="/items/item[@name='schematicMaster' and not(property[@name='AltItemTypeIconColor'])]">
  <property name="AltItemTypeIconColor" value="0,255,0,200" />
</append>
```

Exactly one matches. The other matches nothing, and the patcher logs `did not apply` — scaring admins about intended
behavior. The author's intent, *"make sure this property is on this item"*, has no way to be written down.

The repo now measures this noise: `Tests/Patcher/PatchApplicationTests.cs` applies every mod's real patches to real
vanilla XML — since #23/#37/#21, once per game version the mod declares — and requires every warning to be declared
with a reason. Two of its standing declarations are this exact idiom (`StrongholdTweaks/items` for
`AltItemTypeIconColor`, `StrongholdTweaks/blocks` for `AllowedRotations`), both annotated "goes away when `<ensure>`
lands (#60)".

## 2. What vanilla already provides

Established by disassembling `Assembly-CSharp.dll` and by running the game's shipped `NCalc.dll` against the
expressions below.

- **Patch methods** are `[XmlPatchMethod("name")]` statics, or registered at runtime with
  `XmlPatcher.addXmlFilePatchMethod(name, MethodInfo, bool requiresXpath = true)` — the hook `<foreach>` already uses.
  Built-ins: `append`, `prepend`, `insertAfter`, `insertBefore`, `remove`, `set`, `setattribute`, `removeattribute`,
  `csv`, `conditional`, `include`.
- **A patch "applied" iff its method returns a count > 0.** The warning is emitted by `XmlPatcher.PatchXml` for each
  child that returns false; `XmlPatcher.singlePatch` itself logs nothing. Any patch method that dispatches its own
  children via `singlePatch` therefore owns its warning policy — how `<foreach>` already works.
- **`setattribute` already upserts.** `SetAttributeByXPath` calls `XElement.SetAttributeValue`, which creates the
  attribute when absent (`Attribute "{0}" added/overwritten by: "{1}"`). **The game can already ensure an *attribute*;
  what it cannot do is ensure a *child element*.** That is precisely the hole this spec fills, and it is why the
  command is named for the unsuffixed element form (§7, decision 1).
- **`<conditional>` can express the two-branch form today.** Its `<if cond>` chain plus `<else>` is evaluated by
  `XmlPatchConditionEvaluator`, whose `xpath(expr)` function runs against the target file *as it stands mid-patch* and
  returns the matched node's string, or `null` for zero matches, or `"More than one match"` for 2+. It is not a
  boolean, so it must be compared; verified against the shipped NCalc build that the `null` literal parses and
  `xpath('…') != null` yields a proper `Boolean`:

```xml
<conditional>
  <if cond="xpath('/items/item[@name=&quot;schematicMaster&quot;]/property[@name=&quot;AltItemTypeIconColor&quot;]') != null">
    <setattribute xpath="…/property[@name='AltItemTypeIconColor']" name="value">0,255,0,200</setattribute>
  </if>
  <else>
    <append xpath="/items/item[@name='schematicMaster']"> … </append>
  </else>
</conditional>
```

Warning-free and correct, but the selector is written **three times** in two quoting conventions, `xpath()` emits two
or three `Log.Out` lines per evaluation, and a condition is one boolean for the whole document — it cannot express
"for each item, set it if present, add it if not."

## 3. Proposal: `<ensure>`

A declarative command that makes a child element exist, with the given attributes, on **every** node the xpath matches.

```xml
<ensure xpath="/items/item[@name='schematicMaster']">
  <property name="AltItemTypeIconColor" value="0,255,0,200" />
</ensure>
```

Not "set it, or else append it" — just "this is how it should look." The patcher works out which.

| Attribute  | Required | Meaning                                                                          |
|------------|----------|----------------------------------------------------------------------------------|
| `xpath`    | yes      | Selects the parent nodes. Every match is processed.                              |
| `position` | no       | Where newly created children go: `append` (default) or `prepend`. See §3.5.      |

### 3.1 Semantics

For each node matched by `xpath`, for each template child in document order:

1. Find the existing children that match the template's **identity** (§3.2).
2. **Zero matches** — deep-clone the template and insert it per `position`.
3. **Exactly one match** — merge: `SetAttributeValue` for every attribute on the template, leaving attributes the
   template does not mention untouched, and leaving the element where it is (§3.5). Then apply §3.3 and §3.4.
4. **Two or more matches** — ambiguous. Do nothing to that parent's child, warn, continue. (§3.6)

The return count is the number of matched parents. Zero parents → 0 → one ordinary vanilla warning.

**This makes the warning meaningful rather than suppressed.** Today's noise comes from one of two correct commands
missing; here, the only way to warn is for the parent selector to match nothing — which is a genuine authoring error
worth reporting. The goal was to stop false warnings; this also restores true ones.

`<ensure>` is **idempotent by construction**: applying the same block twice yields the same document as applying it
once, and two mods ensuring the same property converge instead of fighting. The conformance suite asserts this
directly (§6.2).

### 3.2 Identity

Identity is `(tag name, key attributes)`. The default key is `@name`, falling back to `@class` when the template has
no `@name`; a template carrying both uses both. Surveying vanilla config for how children are identified among their
siblings:

| File               | `tag, @name`                                                  | `tag, @class`      | neither                                        |
|--------------------|---------------------------------------------------------------|--------------------|------------------------------------------------|
| `items.xml`        | property 14692, passive_effect 3029, stat 2256, requirement 1472 | property 1214    | triggered_effect 2337, effect_group 656        |
| `blocks.xml`       | property 49308, drop 3103                                     | property 2289      | drop 515                                       |
| `entityclasses.xml`| property 3887, passive_effect 268, effect_group 161            | property 18        | triggered_effect 110                           |
| `recipes.xml`      | ingredient 1970, passive_effect 565                           | —                  | effect_group 231                               |
| `progression.xml`  | requirement 880, passive_effect 628, book 152                 | —                  | effect_group 344, level_requirements 301       |

`@name` covers the overwhelming majority, and `@class` covers `<property class="Action0">`. Neither is present on
`triggered_effect`, `effect_group`, `level_requirements` — exactly the elements whose identity is genuinely compound.

**`@name` is not universally unique among siblings.** Counting parents that hold more than one same-tag, same-`@name`
child:

| File              | passive_effect | stat | drop | requirement | property |
|-------------------|----------------|------|------|-------------|----------|
| `items.xml`       | 523            | 282  | —    | 47          | 2        |
| `blocks.xml`      | —              | —    | 231  | —           | 2        |
| `progression.xml` | 88             | —    | —    | 17          | —        |

For `<property>` — the case this feature exists for — `@name` is effectively a key (2 exceptions across ~64000
elements). For `passive_effect` and `drop` it plainly is not. Hence rule 4 above: **never guess.** Two matches means
the author's key does not identify one element, and silently updating one of them would corrupt the config in a way
that is very hard to trace. This mirrors the exactly-one-node-or-skip rule `<foreach>` already teaches.

An explicit override, as a reserved attribute on the template child, stripped before the element is written — the same
device `foreach-name` already uses:

```xml
<ensure xpath="/items/item[@name='meleeToolRepairT3Wrench']">
  <passive_effect ensure-key="name,operation,tags" name="EntityDamage" operation="perc_add" tags="salvaging" value="0.5" />
</ensure>
```

A template child with neither `@name` nor `@class` and no `ensure-key` is an **error**, not a guess.

### 3.3 Nested templates

Recursion falls out of the algorithm — ensure the child, then apply the same rule to the template's own children
inside it:

```xml
<ensure xpath="/items/item[@name='noteIntroToHades']">
  <property class="Action0">
    <property name="Sound_start" value="read_mod" />
    <property name="Sound_in_head" value="true" />
  </property>
</ensure>
```

Ensures `Action0` exists, then ensures those two properties inside it, whether or not the block already existed. Note
that `ensure` never removes: a stale child already inside `Action0` stays. That is the intended meaning.

### 3.4 Text content

A template child carrying non-whitespace text **and no element children** sets the text of the ensured element:

```xml
<ensure xpath="/blocks/block[@name='terrDirt']">
  <property name="Group">Building</property>
</ensure>
```

Vanilla `setattribute` already sources its value from element text, so authors expect text to mean something, and
silently discarding content someone typed is the worst available outcome. The no-element-children guard is
load-bearing: whitespace between children is everywhere in these files and must never be mistaken for content.

### 3.5 Ordering

**Sibling order is load-bearing in 7 Days to Die config: among duplicates, the last sibling wins.** Two consequences:

- **New children default to `append`**, so what the mod declares takes effect over anything already present.
  `position="prepend"` states the opposite intent — supply a value only if nothing later overrides it — which is how a
  mod contributes a default that other mods, or the player's own config, may beat.
- **Merging never moves an existing element.** `position` governs insertion only. When the key matched exactly one
  element, its position cannot change which value wins, so relocating a node the author never asked to move would be
  surprise for no benefit. An author who genuinely needs a node moved has `remove` plus `<ensure>`, or
  `insertBefore`/`insertAfter`.
- **`position` applies at every level of a nested block**, not only the outermost. Settled while implementing: one
  rule everywhere beats a rule that silently stops applying once you nest, and "supply a default others may beat"
  means the same thing at any depth.

Ordering is also why rule 4 warns rather than resolving the ambiguity by merging into the last match: for
`<property>`, last-wins would make "merge the last one" defensible, but for `passive_effect` and `drop` — where the
survey above shows duplicates are routine — siblings stack rather than override, and quietly editing one of a stack is
exactly the untraceable corruption rule 4 exists to prevent.

### 3.6 Diagnostics

| Condition                                              | Result                                                        |
|--------------------------------------------------------|---------------------------------------------------------------|
| `xpath` matched ≥1 parent                              | Return that count; no warning                                 |
| `xpath` matched 0 parents                              | Return 0 → one vanilla `did not apply` warning                |
| A parent has 2+ children matching the key              | `Log.Warning` naming file, line, parent, and key; that child skipped, other parents unaffected |
| `xpath` matched an attribute rather than an element    | `Log.Warning` naming `setattribute` as the command that ensures an attribute, with the rewrite; that match skipped (§3.8) |
| `xpath` matched some other non-element node            | `Log.Warning`, that match skipped                             |
| `<ensure>` has no template children                    | `Log.Error`, return 0 — and when the block carries text, the message names `setattribute` (§3.8) |
| Template child has no `@name`/`@class` and no `ensure-key` | `Log.Error`, return 0 (whole block; it would fail identically for every parent) |
| Template child has an unusable `ensure-key`            | `Log.Error`, return 0                                         |
| `position` is neither `append` nor `prepend`           | `Log.Error`, return 0                                         |
| `xpath` attribute missing                              | Vanilla dispatch throws before `<ensure>` runs (`requiresXpath: true`) — same as every built-in |
| `xpath` malformed                                      | `XPathException` propagates, wrapped by `singlePatch`'s formatted rethrow — same as every built-in |
| Non-element children (text, comments) of `<ensure>`    | Ignored                                                       |

The split between per-parent warnings and whole-block errors follows `<foreach>`: data conditions warn and carry on,
mod bugs stop the construct. Warnings and errors carry the same context prefix foreach's do (mod, file, line), so a
log line is diagnosable on sight.

### 3.7 What it does not do

`<ensure>` is an upsert, not a general alternation. It cannot express "patch layout A, or if this is the older game
build, layout B", or "remove whichever of these two nodes exists". Those remain two commands and one spurious warning.
§8 keeps `<oneof>` on the shelf for that.

### 3.8 Attributes are `setattribute`'s job, and the error says so

`<ensure>` ensures **child elements**. Ensuring an *attribute* needs no new command, because vanilla `setattribute`
already upserts (§2) — it creates the attribute when absent, overwrites it when present, applies to every matched
element, and warns only when the xpath matched nothing. That is `<ensure>`'s contract exactly:

```xml
<setattribute xpath="/items/item[starts-with(@name, 'boss')]" name="extends">masterBoss</setattribute>
```

The natural-looking `<ensure xpath="/items/item[…]/@extends">masterBoss</ensure>` **cannot** be made to mean this, and
the reason is structural rather than a matter of effort: XPath selects nodes that exist, so an attribute-targeting
xpath returns only the elements that *already carry* the attribute. The create-if-absent half of "ensure" is invisible
to it. Implemented literally it would update the items that already have `@extends`, silently skip exactly the ones
the author was fixing, and — if none had it — warn `did not apply`, which is the noise this feature exists to remove.

Attribute-targeting xpaths are otherwise idiomatic in vanilla (`csv` *requires* one), so authors will reach for this;
the spec's own author did. It is therefore a **discoverability** gap, not a capability gap, and it gets vanilla's own
answer — `remove` already detects attribute matches and names the attribute-specific command instead. `<ensure>` does
the same, at both places the mistake surfaces: an attribute match during application, and the no-template-children
error when the block is `<ensure xpath="…/@attr">value</ensure>`.

Detecting "this xpath looks attribute-targeting" for the *message* may use a cheap heuristic. That would be
unacceptable for semantics, but a diagnostic that guesses wrong costs a slightly-off hint, never wrong behavior.

## 4. Comparison

|                                       | `<ensure>`                           | `<oneof>` (first-wins)                  | `<any>` (run all, warn if none)        |
|---------------------------------------|--------------------------------------|-----------------------------------------|----------------------------------------|
| Selector written                      | once                                 | once per alternative                    | once per alternative, plus `not(…)`    |
| Set-based (all matches)               | natively                             | only wrapped in `<foreach>`             | yes                                    |
| Trap                                  | ambiguous key (caught, warned)       | wrapping set-based commands silently skips the second | non-disjoint predicates double-apply |
| Warning fires when                    | selector matched nothing (a real bug)| every alternative failed                | every alternative failed               |
| Multiple properties per block         | yes                                  | one block each                          | one block each                         |
| Ordering control                      | `position`, and merge never moves    | whatever the alternatives do            | same                                   |
| Handles non-upsert alternatives       | no                                   | yes                                     | yes                                    |
| Idempotent / multi-mod convergent     | yes                                  | no — depends on the alternatives        | no                                     |
| New concepts for authors              | identity/key rule                    | none (composes known commands)          | none                                   |
| Implementation                        | ~150 lines over the target document  | ~50 lines, dispatch via `singlePatch`   | ~50 lines                              |

Draft 2 carried an "offline-testable" row favoring `<ensure>` (pure `XDocument`, no `singlePatch` needed). The
conformance harness dissolved that differentiator — `Tests/Fixtures/PatcherHost.cs` executes the game's own
`singlePatch` headlessly, so all three designs are now equally testable. The row is corrected rather than silently
dropped; the recommendation never rested on it alone.

## 5. Recommendation

**Build `<ensure>`; shelve `<oneof>`.**

The caveat that sank `<oneof>` — that set-based upserts need a `<foreach>` wrapper or they silently do half the job —
does not exist here: operating on every match is the definition of the command, not a wrapper you must remember. It
removes the duplication entirely rather than relocating it, it collapses several properties into one block, and it is
idempotent, so two mods ensuring the same property converge instead of fighting.

The cost is one genuinely new concept — identity — and the survey in §3.2 says that concept is unavoidable for any
design in this space. Better to name it and make ambiguity loud than to let a hidden first-match rule corrupt a config.

## 6. Implementation, testing, and landing

### 6.1 Implementation

One new file, `StrongMods/XmlPatchMethodEnsure.cs`, shaped like the vanilla methods: signature
`Ensure(XmlFile, string xpath, XElement, XmlFile, Mod)`, matches via the target's own XPath helpers, mutates
`XmlDoc` directly. No `singlePatch` dispatch — `<ensure>` is a document transformation, not a container of other
commands. Registration mirrors `<foreach>` in `ModApi.cs`, gated by a new always-true getter in `Config.cs`
(`XmlPatchMethodEnsureEnabled`, beside `XmlPatchMethodForeachEnabled`):

```csharp
private static void InitXmlPatchMethodEnsure(Mod mod, Harmony harmony) {
  if (!Config.XmlPatchMethodEnsureEnabled) {
    return;
  }

  MethodInfo method = AccessTools.Method(typeof(XmlPatchMethodEnsure), nameof(XmlPatchMethodEnsure.Ensure));
  XmlPatcher.addXmlFilePatchMethod("ensure", method);   // requiresXpath: true (the default)
}
```

No Harmony patch — purely an added patch method, so the patch-target smoke tests are unaffected and no
`[PatchTargetManifest]` is involved. Other interactions:

- **Breadth-first patcher:** none. Operates within one mod's pass over one file.
- **`<foreach>`:** an `<ensure>` in a foreach body is materialized like any other command (`{…}` substitution over
  attributes and children, including `ensure-key` values) and dispatched through `singlePatch`. The composition gets
  its own conformance tests (§6.2) rather than the Draft 2 code-reading TODO.
- **StrongMods absent:** "Patch type (ensure) unknown" plus a `did not apply` warning, and the block is skipped — same
  degradation as `<foreach>`. Consumers must declare the StrongMods dependency in `ModInfo.xml`.

Documentation ships as a standalone `StrongMods/Docs/ensure.md` mirroring `Docs/foreach.md` — and this is now
load-bearing, not just a docs preference: the conformance suite is doc-clause-driven (each test file header cites the
doc section it verifies), so `ensure.md` must be written with the same table discipline — semantics rules, the
diagnostics table with its warn/error split — because those tables are what the tests trace to. Plus a line in
`StrongMods/README.md`.

### 6.2 Testing — conformance suite at parity with `<foreach>`

Draft 2's plan (a pure-function seam for offline tests, an in-game fixture patch checked via `ConfigDump/`) predates
the Tests project and is superseded. The harness that now exists is strictly better: `PatcherHost` loads
Assembly-CSharp, LogLibrary, and the real `StrongMods.dll` headlessly (Unity stubbed), executes the game's own
`singlePatch`, and captures the game's own log — so tests assert real dispatch, real warnings, and the resulting
document, and CI runs the suite on every push. `PatcherHost.CreateXmlFile` also settles Draft 2's "unverified" note:
`XmlFile` construction from a string works headlessly; the host does it for every test.

**Scope: the default host, not per-version.** `.ai/testing-declared-versions.md` §3c settled this for `Foreach/*`
and it governs `Ensure/*` identically — these tests exercise StrongMods' *engine semantics* against synthetic XML,
not a mod's compatibility with a particular vanilla, so per-version legs would be real cost for hypothetical signal.
`Ensure/*` therefore uses `PatcherHost.Instance`, like `Foreach/*` and `PatcherCacheTests`. §3c's caveat applies
unchanged: revisit if StrongMods itself ever pins. The per-version signal for this feature is real but belongs one
layer out, on the mods that *use* `<ensure>` — which is §6.3's job.

Harness change required: `PatcherHost.SeedPatchMethods` fills the patch-command registry by hand (the attribute scan
that discovers commands in-game cannot run against the stub), registering vanilla commands plus `foreach` explicitly.
`<ensure>` needs one more line there, mirroring its `ModApi` registration. Seeding runs from the constructor, so the
one line covers `Instance` and every `ForLabel` host alike. That is the second place registration lives; a
host-vs-`ModApi` drift guard (a test asserting the host registers every command `ModApi` does, or a shared catalog
both read) is worth considering while in there, but must not grow this feature — raise it separately if it grows
legs.

`Tests/Ensure/`, one file per `ensure.md` section, same style as `Tests/Foreach/` (xunit,
`[Collection(PatcherHostCollection.Name)]`, `PatcherHost.Instance.Value.Apply(...)`, raw-string XML fixtures,
asserts on `PatchOutcome.Applied` + `.Xml` + exact warning/error substrings):

| File                     | Covers (doc section)                                                                                          |
|--------------------------|---------------------------------------------------------------------------------------------------------------|
| `UpsertBasicsTests`      | §3.1: absent → inserted; present → merged, unmentioned attributes preserved; multi-parent set-based operation; return-count/`Applied` semantics; **idempotency** (same block twice → identical document) and **convergence** (two overlapping blocks → merged result) |
| `IdentityTests`          | §3.2: `@name` key; `@class` fallback; both-present; `ensure-key` override incl. compound keys; `ensure-key` stripped from output; ambiguous key → warn, skip that child, other parents still processed; missing key → error |
| `NestedTemplateTests`    | §3.3: recursion into existing and freshly created blocks; never-removes semantics                             |
| `TextContentTests`       | §3.4: text set; whitespace-between-children not mistaken for content                                          |
| `OrderingTests`          | §3.5: `append` default; `prepend`; merge never moves an existing element                                      |
| `FailureModeTests`       | §3.6 table row-by-row, foreach-style: zero parents → `Applied` false (via `Apply`) and the vanilla warning text (via `ApplyPatchFile`); empty block, bad `position`, missing/unusable key → `Log.Error`; malformed xpath → throw, mirroring foreach's `A_bad_xpath_is_an_error` |
| `ForeachCompositionTests`| §6.1: `<ensure>` in a foreach body with `{…}` in template attributes and in `ensure-key`; per-iteration materialization |

### 6.3 Acceptance — the repo's own patches go quiet, on every declared version

`PatchApplicationTests` is a ready-made end-to-end acceptance test, and #23/#37/#21 sharpened it: it is now a
`[Theory]` over declared version labels, replaying each mod's `Config\` against **each vanilla that mod declares
support for**. Landing finishes by converting the two declared call sites
(`StrongholdTweaks/Config/items.xml` `AltItemTypeIconColor`, `StrongholdTweaks/Config/blocks.xml`
`AllowedRotations`) to `<ensure>` and deleting their `ExpectedToLog` entries.

StrongholdTweaks declares no pin, so it inherits `build/GameVersions.props`' default `SdtdTestVersions` —
`V3.1.0-b14` and `V3.0.1-b4` today — and the conversion must come out clean against **both**. That is stronger
than what Draft 3 promised and is genuinely new signal: `<ensure>`'s selector has to match on each vanilla
independently, so a property that moved or an item renamed between versions surfaces here rather than in someone's
log. The suite enforces the cleanup in both directions — an undeclared warning fails, and a declaration that has
gone quiet against every label fails — so forgetting it is itself a red test.

One in-game sanity pass at landing is still warranted, for the single thing the host does not exercise: `ModApi`'s
registration path running under the real game. Everything behavioral is the suite's job now.

### 6.5 As built

Landed across the four planned waves — `79224d9`, `81f8110`, `a20e9df`, `badb98d` — shipping
`StrongMods/XmlPatchMethodEnsure.cs`, `Docs/ensure.md`, and 54 tests in seven files under `Tests/Ensure/`, one per
doc section. StrongMods went to 1.1.0; StrongholdTweaks to 13.0.1, with its StrongMods dependency raised from `1.0`
to `1.1` — the conversion made `<ensure>` a hard requirement, and a bare minimum of `1.0` would have let it load
against a StrongMods that answers `Patch type (ensure) unknown`.

Three semantics were settled at the keyboard rather than in this document, each because a test asked the question
the design had not:

- **Creating and merging must produce identical text**, or idempotency is only true on paper. A cloned template
  keeps its authored whitespace while a merge sets trimmed text, so the clone normalizes its own leaf text.
- **Merging text replaces text nodes individually**, never the element's whole value, which would take child
  elements with it and break "ensure never removes".
- **Prepending chains off the previously inserted sibling**, since prepending each new child in turn comes out
  reversed.

The `PatchApplicationTests` acceptance loop (§6.3) closed as designed: deleting the two declarations was forced by
the suite, not remembered. Beyond it, a throwaway check applied the old idiom and the new `<ensure>` to real vanilla
`items.xml` and `blocks.xml` and compared documents — byte-identical on both declared versions, so the conversion
changed the log and nothing else. "No warnings" would not have proven that.

Not converted: the `Stacknumber` pair in `StrongholdTweaks/Config/items.xml`. It looks like the same idiom but its
two halves carry *different* predicates (`setattribute` only where `@value > 1`; `append` only for non-vehicle
placeables), which no single `<ensure>` selector reproduces — and it never warned, which is why it was never
declared. Left alone deliberately.

### 6.4 Landing plan

Waves, foreach-conformance style, each with its own explicit go and committed before the next starts:

1. `XmlPatchMethodEnsure.cs` + `Config` toggle + `ModApi` registration + `PatcherHost` seeding line +
   `Docs/ensure.md` + `UpsertBasicsTests` — the smallest shippable slice that proves the pipeline end to end.
2. `IdentityTests` + `FailureModeTests` (the two suites most likely to push back on the implementation), plus the
   §3.8 attribute teaching errors and an "Ensuring an attribute" section in `Docs/ensure.md`.
3. `NestedTemplateTests` + `TextContentTests` + `OrderingTests` + `ForeachCompositionTests`.
4. StrongholdTweaks conversion + `ExpectedToLog` cleanup + README line + in-game sanity pass + `ModInfo.xml` version
   bumps. Verify with `dotnet test`, which now covers both declared versions (§6.3).

Wave 1 exceeds the ~100-line target on the implementation file alone; flagging that here is the plan-phase
notification the workflow requires.

## 7. Decisions taken

1. **Name `<ensure>`**, not `<ensurechild>`. Vanilla's grammar is that unsuffixed commands operate on elements
   (`append`, `set`, `remove`) and the noun suffix marks the attribute variants (`setattribute`, `removeattribute`).
   Ensuring an element is therefore `<ensure>`; the attribute form the game already has is `setattribute`. Checked
   against nearby ecosystem vocabulary for fuzzy collisions (vanilla command names, Harmony attribute names): none.
2. **Merge only.** No `replace="true"`. Merge is what "ensure" means and the only behavior that composes when two mods
   touch the same element; wholesale replacement is already expressible as `remove` plus `<ensure>`. If it is ever
   added it belongs on the template child, so heterogeneous children can differ.
3. **Template text sets element text** (§3.4).
4. **`position="append|prepend"`, default `append`** (§3.5), because among duplicate siblings the last wins.
5. **No `<ensureabsent>` mirror yet.** `remove` on zero matches produces the same spurious warning, but "make sure it
   is gone" is far rarer and carries none of the identity or merge subtlety — roughly 15 lines whenever it is wanted.
6. **Standalone doc file**, now doubly justified: the conformance suite traces to its clauses (§6.1).
7. **Verification is the conformance suite, at parity with `<foreach>`** (§6.2) — doc-clause-driven files, warn/error
   split asserted against the game's own log — plus the `PatchApplicationTests` acceptance loop (§6.3). Replaces
   Draft 2's pure-seam + in-game-fixture plan, which predated the Tests project.
8. **Draft 2's testability claim corrected, recommendation unchanged** (§4): the harness made every candidate design
   equally testable, so that argument for `<ensure>` is void; the set-based, warning-semantics, and idempotency
   arguments carry it alone.
9. **Attributes stay `setattribute`'s job; `<ensure>` signposts rather than absorbs them** (§3.8, §8). The
   capability already exists in vanilla, and the syntax authors reach for cannot express create-if-absent at all.
   Scope is a teaching error and a doc section, in wave 2.
10. **`Ensure/*` conformance runs on the default host only; per-version assertion happens at the mod layer**
    (§6.2, §6.3). Follows `.ai/testing-declared-versions.md` §3c rather than inventing a second policy: engine
    semantics are version-independent, mod-to-vanilla compatibility is not. Consequence worth stating plainly — no
    test proves `<ensure>` itself behaves identically on `V3.0.1-b4` until a mod using it declares that version,
    which §6.3's conversion does immediately.

## 8. Alternatives considered

**`<oneof>` — ordered alternatives, first success wins, silent otherwise.** ~50 lines dispatching children through
`singlePatch`, which owns the warning policy for free. Rejected as the answer to *this* problem because first-wins is
per-block: wrapping two set-based commands in it applies the first to its whole match set and skips the second
entirely, so the correct set-based form is `<oneof>` inside `<foreach>` — exactly the indirection this feature is meant
to remove. Still the right tool for genuine alternation (§3.7); worth revisiting on demand rather than speculatively.

**`<any>` — run every child, warn only if none applied.** Purely a warning-policy change, so it can never alter which
commands run. But it keeps the duplicated `not(property[…])` predicate that makes the idiom ugly, and it fails silently
when the predicates are *not* disjoint (both apply → duplicate element).

**`optional="true"` on individual commands.** Discards the "warn if *all* fail" requirement — a typo'd selector in
every alternative would go unreported — and requires patching `XmlPatcher.PatchXml`, since the warning is emitted there
and the attribute is invisible to the patch method.

**Extending `<conditional>` with an `xpath_exists()` boolean.** Fixes the `!= null` comparison and one layer of
quoting, but leaves the selector written three times and the `Log.Out` spam untouched, and needs a Harmony patch on
`XmlPatchConditionEvaluator`.

**Teaching `<ensure>` to target attributes.** Raised during wave 1, when the attribute case turned out to be a
reasonable expectation this design does not meet (§3.8). Three shapes were weighed against doing nothing:

| Shape                                                     | Cost                                                              | Verdict                                                  |
|-----------------------------------------------------------|-------------------------------------------------------------------|----------------------------------------------------------|
| `<ensure xpath="…/@extends">masterBoss</ensure>`          | ~80–120 lines: split the trailing step off the xpath, evaluate the element half, set the attribute — plus a test matrix for `attribute::`, `/@*`, unions, and predicates containing `/@` | Rejected. Lexical surgery on an XPath expression, fragile in exactly the cases nobody tests, and semantically identical to `setattribute` |
| `<ensure xpath="…" attribute="extends">masterBoss</ensure>` | ~40 lines, ~6 tests, doc section                                  | Rejected. A second spelling of `setattribute`, and it contradicts decision 1's naming grammar |
| Teaching error plus an "Ensuring an attribute" doc section | ~15 lines, 1 test — the "not an element" branch already exists     | **Adopted**, wave 2                                       |

The deciding evidence: `setattribute` already delivers the full contract (§2), verified against `3.1.0.14`'s
`SetAttributeByXPath` — so every option but the last buys a synonym rather than a capability. What was missing was a
signpost, and that is a fifteen-line problem.

Worth revisiting only if the *multiple* attributes case bites — setting several attributes on one matched element is
N `setattribute` commands today, which is the one ergonomic gap this leaves. Separate and much smaller question.

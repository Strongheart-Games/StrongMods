# ModInfo Dependencies Extension — Specification

**Version:** 1.0 (Draft 2)\
**Status:** Proposed\
**Applies to:** 7 Days to Die mods using the ModInfo v2 format\
**Audience:** Mod authors declaring dependencies; tool authors implementing validation

## 1. Overview

This specification defines a `<Dependencies>` extension to a mod's `ModInfo.xml` that lets authors declare, in a
machine-readable way, which game versions and which other mods their mod requires. A conforming validator (such as a
loader mod) reads these declarations at startup and reports any unsatisfied dependencies to the player or server
administrator with a clear, actionable message — for example:

```
StrongMods requires StrongUI [1.2,2.0), found 1.1
```

The extension is purely additive. The vanilla game parser silently ignores elements it does not recognize, so a mod that
declares dependencies remains fully compatible with unmodified installations, older game builds, and third-party tools
that are unaware of this specification.

Version constraints use the interval notation established by NuGet's version range syntax, which will be familiar to C#
developers and — unlike operator-based syntaxes — requires no XML character escaping.

## 2. Placement and compatibility

The `<Dependencies>` element is a direct child of the ModInfo v2 document's info element, appearing alongside `<Name>`,
`<Version>`, and the other standard elements. A ModInfo.xml MUST contain at most one `<Dependencies>` element. A
ModInfo.xml with no `<Dependencies>` element declares no constraints and is always considered satisfied.

The game's loader dispatches to its v2 parser whenever the document root does *not* contain a child element named
`ModInfo`. The examples in this document therefore use `<ModInfo>` as the document root: the name is descriptive, and
because it is the root (not a child of the root), the file is parsed as v2.

A complete example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModInfo>
  <Name value="StrongMods" />
  <DisplayName value="StrongMods" />
  <Version value="0.0.1" />
  <Description value="Modding tools from Strongheart." />
  <Author value="str0ngh34rt" />
  <Website value="https://github.com/Strongheart-Games/StrongMods/tree/main/StrongMods" />
  <Dependencies>
    <Game version="3.*" />
    <Mod name="StrongCore" />
    <Mod name="StrongUI" version="[1.2,2.0)" />
    <Mod name="SomeCompatShim" version="2.*" optional="true" />
  </Dependencies>
</ModInfo>
```

All characters used by the constraint syntax — brackets, parentheses, commas, digits, dots, and the asterisk — are valid
unescaped inside XML attribute values.

## 3. The Dependencies element

`<Dependencies>` contains zero or more dependency declarations. Two element types are defined in this version of the
specification.

### 3.1 Game

```xml
<Game version="CONSTRAINT" />
```

Declares a constraint on the game version. At most one `<Game>` element may appear. The `version` attribute is REQUIRED
and holds a version constraint (section 4).

Validators compare the constraint against the game's version with any leading `V`/`v` stripped and any build suffix
(such as `b14`) ignored. Build-level constraints are reserved for a future version of this specification.

### 3.2 Mod

```xml
<Mod name="NAME" version="CONSTRAINT" optional="BOOL" />
```

Declares a dependency on another mod. Zero or more `<Mod>` elements may appear.

| Attribute  | Required | Default | Meaning                                                                                                                                                      |
|------------|----------|---------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `name`     | Yes      | —       | The target mod's internal `<Name>` value. Matched exactly: ordinal, case-sensitive. This is the stable identifier; `DisplayName` is never used for matching. |
| `version`  | No       | any     | A version constraint (section 4) evaluated against the target mod's `<Version>` value. When absent, any version satisfies the dependency.                    |
| `optional` | No       | `false` | When `true`, the dependency is satisfied if the target mod is absent. When the target mod *is* present, its version constraint must still be satisfied.      |

The `optional` flag exists for soft integrations: it lets a validator catch the case where a compatible companion mod is
installed but too old, which otherwise fails silently at runtime.

Declaring the same `name` more than once within a single `<Dependencies>` block is an authoring error (section 6).

## 4. Version constraints

Constraints follow NuGet's version range notation: mathematical interval syntax, where a square bracket denotes an
inclusive bound and a parenthesis denotes an exclusive bound. This specification adopts NuGet's semantics as-is, so
intuitions carried over from `.csproj` and `.nuspec` files remain correct.

### 4.1 Grammar

```
constraint  = range | floating | version | "*" ;
range       = open , [ version ] , [ ws ] , "," , [ ws ] , [ version ] , close
            | "[" , version , "]" ;
open        = "[" | "(" ;
close       = "]" | ")" ;
floating    = segment , { "." , segment } , "." , "*" ;
version     = segment , { "." , segment } ;
segment     = digit , { digit } ;
```

A range MUST include at least one bound. A range whose lower bound exceeds its upper bound, or whose bounds are equal
with either bound exclusive, matches nothing and is an authoring error (section 6). The asterisk in a floating
constraint may appear only as the final segment.

### 4.2 Forms and examples

| Constraint  | Meaning                                            |
|-------------|----------------------------------------------------|
| `1.2`       | Version 1.2 **or later** (equivalent to `[1.2,)`)  |
| `[1.2]`     | Exactly version 1.2                                |
| `[1.2,2.0)` | At least 1.2, but before 2.0                       |
| `[1.2,2.0]` | At least 1.2, up to and including 2.0              |
| `(1.2,)`    | Later than 1.2                                     |
| `(,2.0)`    | Before 2.0                                         |
| `(,2.0]`    | Up to and including 2.0                            |
| `3.*`       | Any version whose first segment is 3               |
| `*`         | Any version (equivalent to omitting the attribute) |

> **The most important row is the first one.** As in NuGet, a bare version is a *minimum*, not an exact pin. Authors who
> want to require exactly one version must write `[1.2]`. This default is deliberate: "at least this version" is what a
> dependency declaration usually means, and exact pins should be rare and intentional — an ecosystem of exact pins breaks
> on every upstream release.

The most common real-world constraint is the half-open range `[minimum,next-breaking-version)`, such as `[1.2,2.0)`.

### 4.3 Normalization

Before parsing, validators trim surrounding whitespace and strip a single leading `V` or `v` from both the constraint's
version literals and the value under test. Optional whitespace is permitted after the comma inside a range. For game
versions, any build suffix is ignored (section 3.1).

### 4.4 Comparison semantics

Versions are compared segment-wise. Split both versions on `.`, then compare corresponding segments as integers from
left to right; the first unequal pair decides the ordering. If one version has fewer segments, missing segments are
treated as zero, so `1.2` is equal to `1.2.0`. This matches NuGet's treatment of numeric version parts.

A floating constraint compares only the segments preceding the asterisk. `3.*` therefore matches `3`, `3.0`, and
`3.9.7`, but not `2.9` or `30.0`.

Segments MUST be compared numerically, never lexically: `10` is greater than `9`. Implementers are cautioned against
delegating to platform version classes that impose their own format restrictions (for example, .NET's `System.Version`
throws on inputs outside two to four numeric parts, which real-world mod versions routinely violate).

### 4.5 Deliberate exclusions

Prerelease labels (`1.2.0-beta`) are not supported in this version of the specification: version segments are numeric
only, and a version or constraint containing a prerelease label is an authoring error. NuGet's prerelease ordering rules
are the most intricate part of SemVer, and the mod ecosystem does not use them consistently enough to justify the
complexity. They may be adopted in a future version.

Comparison-operator syntax (`>=1.2 <2.0`) and the caret/tilde shorthand (`^1.2`, `~1.2.3`) are likewise excluded.
Operators require XML escaping of `<` inside attribute values, and caret/tilde presuppose strict semantic-versioning
discipline while requiring readers to know external conventions. Interval notation expresses every useful range without
either problem.

## 5. Evaluation

A mod's dependencies are evaluated as follows. For a `<Game>` declaration, the constraint is tested against the
normalized game version. For each `<Mod>` declaration, the validator looks up a loaded mod whose internal `<Name>`
equals the declared `name`. If no such mod is loaded, the dependency is unsatisfied unless `optional` is `true`. If the
mod is loaded and the declaration carries a `version` attribute, the constraint is tested against that mod's `<Version>`
value; this applies to optional dependencies as well.

A mod's dependency block is satisfied only when every declaration within it is satisfied.

**Cascading.** When a validator is capable of blocking mods (section 6), dependencies MUST be evaluated against the set
of mods that will *actually remain loaded*, not the set initially present on disk. Blocking a mod can therefore cascade:
if StrongUI is blocked for its own violations, a mod that requires StrongUI now has a missing dependency and MUST also
be blocked. Validators MUST re-evaluate until the surviving set is stable (a fixpoint). Optional dependencies follow the
same rule consistently: an optional dependency that is blocked is treated as absent, which satisfies the declaration.
Violation messages for cascaded failures SHOULD identify the root cause, not merely the immediate one — "StrongMods
requires StrongUI, which was blocked (StrongUI requires game version 3.*, found 2.4)" — so that fixing one problem
visibly resolves the chain.

## 6. Failure handling

**Authoring errors** — a malformed constraint, a missing required attribute, an unparseable version, an empty range, or
a duplicate `name` — are attributed to the *declaring* mod. Validators MUST report the error and MUST treat the affected
declaration as unsatisfied. Silently passing a constraint that could not be parsed is prohibited, as it lets broken
declarations propagate unnoticed through the ecosystem.

**Violations** — a missing required mod, a version mismatch, or a game-version mismatch — MUST produce a human-readable
message naming the declaring mod, the requirement, and what was actually found:

```
StrongMods requires game version 3.*, found 2.4
StrongMods requires StrongCore, which is not installed
StrongMods requires StrongUI [1.2,2.0), found 1.1
```

**Enforcement.** Validators operate at one of two levels, and a single validator may operate at both levels
simultaneously depending on load order:

*Blocking.* Where the validator is able to prevent a violating mod from taking effect — for example, by tagging the mod
so that its initialization fails and unloading it once all assemblies have loaded — a non-optional violation SHOULD
result in the mod being blocked entirely. Blocking is the preferred behavior: a partially loaded mod (XML patches
applied, code absent or half-initialized) produces symptoms that are far harder to diagnose than a single clear "this
mod was not loaded" error, and it can mislead an administrator into believing the mod is functional. Blocking MUST cover
every part of the mod — code initialization *and* Config XML patches, asset bundles, and other passive content — since a
block that stops only the code recreates the partial-load problem it exists to prevent. Messages for blocked mods MUST
state that the mod was prevented from loading, not merely that a problem was observed.

*Reporting.* Where the validator cannot block — typically for mods that load *before* the validator itself in the game's
alphabetical load order — violations MUST still be detected and reported. A validator's blocking capability therefore
depends on its position in load order; validator authors SHOULD name their mod's folder to sort as early as possible,
and MUST fall back to reporting for anything they cannot reach.

*Load order guidance.* Because blocking only reaches mods that load after the validator, the validator's folder MUST
sort first for the mechanism to give full coverage. The reference implementation, StrongMods, installs into a folder
named `000000-StrongMods` for exactly this reason. Server administrators MUST NOT rename this folder, and mod authors
MUST NOT name their own mod folders to sort before the validator: a mod that "wins" the sort race gains nothing except
exemption from the checks that protect its own users, and its violations degrade from blocked to merely reported. A
validator SHOULD detect mods sorting ahead of it and note in its report that those mods were outside blocking coverage,
so that an administrator can tell enforced results from advisory ones.

In all cases, validators surface reports through channels appropriate to their environment: at minimum, tagged log lines
(so that server administrators can grep for them) and an in-game notice to players.

## 7. Forward compatibility

Validators MUST ignore unknown attributes on recognized elements and unknown child elements within `<Dependencies>`,
mirroring the tolerance of the vanilla parser. This reserves room for future additions — conflict declarations,
prerelease support, build-suffix constraints — without a breaking schema change. Mod authors SHOULD NOT rely on this by
inventing private extensions inside `<Dependencies>`; unrecognized content carries no meaning under this specification.

## 8. Authoring checklist

Before publishing a mod that declares dependencies, confirm that the file parses as ModInfo v2 (the document root must
not contain a `ModInfo` child element); that each `name` exactly matches the target mod's internal `<Name>`, including
case; that every bare version constraint is genuinely intended as a minimum — write `[1.2]` if you mean exactly 1.2;
that ranges use brackets and parentheses correctly (square = inclusive, round = exclusive); that optional integrations
are marked `optional="true"` so their absence is not reported as an error; and that your mod's folder name does not sort
before the validator's (`000000-StrongMods`) — sorting earlier only removes your mod from blocking coverage.

# Declaring dependencies: `<Dependencies>`

`<Dependencies>` is an extension to `ModInfo.xml` that lets your mod say which game versions and which other mods it
needs. When something is missing or too old, the player gets one clear line in the log instead of a mystery:

```
[ModInfoDependencies] StrongAutoLoot requires StrongMods [0.0.2,), found 0.0.1
```

...and your mod is unloaded **completely** — code, XML patches, localization, everything — rather than half-loading
against a world that doesn't have what it needs. A mod that cleanly refuses to load generates one support message; a
mod that loads anyway and corrupts loot tables generates forty.

Declaring dependencies costs you nothing on installations that can't check them: the vanilla parser silently ignores
elements it doesn't recognize, so your `ModInfo.xml` stays fully compatible with unmodified games, older builds, and
third-party tools.

## Requirements

Enforcement needs **StrongMods** installed — it's the validator that reads these declarations at startup. Without it,
your declarations are inert but harmless.

Your `ModInfo.xml` must be in the game's **v2 format**, which yours almost certainly is: v2 means the *root* element
carries the info children directly. (The ancient v1 format wrapped everything in an extra `<ModInfo>` child inside the
root; declarations in a v1 file are not read.)

## Declaring

Add one `<Dependencies>` element alongside `<Name>`, `<Version>`, and the rest:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModInfo>
  <Name value="StrongAutoLoot" />
  <DisplayName value="Strong Auto Loot" />
  <Version value="1.3.0" />
  <Description value="Loot containers, automatically." />
  <Author value="str0ngh34rt" />
  <Dependencies>
    <Game version="3.*" />
    <Mod name="StrongMods" version="[0.0.2,)" />
    <Mod name="SomeCompatShim" version="2.*" optional="true" />
  </Dependencies>
</ModInfo>
```

### `<Game>` — the game version you support

```xml
<Game version="3.*" />
```

At most one, `version` required. The constraint is checked against the game's version with the leading `V` and the
build suffix (`b14`) stripped — so on "V 2.4 (b14)" your constraint is tested against `2.4`.

### `<Mod>` — another mod you need

```xml
<Mod name="NAME" version="CONSTRAINT" optional="true|false" />
```

| Attribute  | Required | Default | What it does                                                                                                                                            |
|------------|----------|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| `name`     | yes      | —       | The target mod's internal `<Name>` value — **not** its `DisplayName`, not its folder name. Matched exactly, case-sensitive.                             |
| `version`  | no       | any     | A version constraint (below) tested against the target's `<Version>`. Omit it when any version will do.                                                 |
| `optional` | no       | `false` | `true` means "fine if absent, but if present it must satisfy the constraint". For soft integrations that break silently when the companion is too old. |

Declare each mod at most once; a duplicate `name` in the same block is an error (see below — errors are fatal to *your*
mod).

## Version constraints

The syntax is NuGet's version-range notation — the same intervals you'd write in a `.csproj`. Square bracket means
inclusive, parenthesis means exclusive:

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
| `*`         | Any version (same as omitting `version` entirely)  |

**The first row is the one to internalize: a bare version is a minimum, not an exact match.** `version="1.2"` accepts
1.2, 1.3, and 2.0. If you truly need exactly 1.2, write `[1.2]` — but exact pins should be rare and deliberate,
because a pin breaks on every upstream release and forces users to wait for you.

The workhorse is the half-open range `[minimum,next-breaking-version)`:

```xml
<Mod name="StrongUI" version="[1.2,2.0)" />
```

"I need 1.2's features, and I haven't been tested against 2.0."

### How versions compare

Versions are dot-separated numbers, compared segment by segment, **numerically**: `10` is newer than `9`, and `1.2`
equals `1.2.0`. A leading `V`/`v` is ignored on both sides. `3.*` matches `3`, `3.0`, and `3.9.7` — but not `30.0`.

Prerelease labels are not supported: `1.2.0-beta` is not a valid version, in a constraint or in a `<Version>`. Keep
your mod's `<Version>` numeric (`1.2.0`), or it can't be targeted by anyone's constraint.

## What happens when a check fails

At startup, StrongMods evaluates every loaded mod's declarations and logs each violation, prefixed for grepping:

```
[ModInfoDependencies] StrongAutoLoot requires game version 3.*, found 2.4
[ModInfoDependencies] StrongAutoLoot requires StrongCore, which is not installed
[ModInfoDependencies] StrongAutoLoot requires StrongUI [1.2,2.0), found 1.1
```

A mod with any unmet requirement is **unloaded** before anything from it takes effect, and the log says so:

```
[ModUnloader] Mod 'StrongAutoLoot' was prevented from loading: StrongAutoLoot requires StrongUI [1.2,2.0), found 1.1
```

### Unloading cascades

Dependencies are checked against the mods that will *actually stay loaded*. If StrongUI gets unloaded for its own
violations, then a mod requiring StrongUI now has a missing dependency and is unloaded too — with a message that names
the root cause, so fixing one problem visibly fixes the chain:

```
[ModInfoDependencies] StrongAutoLoot requires StrongUI, which was blocked (StrongUI requires game version 3.*, found 2.4)
```

An `optional` dependency that gets unloaded counts as absent — which satisfies the declaration.

### Your own mistakes are fatal too

A malformed constraint, a missing `name`, a duplicate declaration, an empty range like `[2.0,1.2)` — these are errors
in *your* file, they're logged, and they unload *your* mod. The validator never shrugs and passes a constraint it
couldn't parse; that would let broken declarations spread silently through the ecosystem.

## Load order matters

StrongMods can only unload mods that load **after** it — that's why it installs as `000000-StrongMods`, sorting ahead
of everything. Mods that sort before it are checked and their violations logged, but they're already initialized and
can only be *reported*, not unloaded:

```
[ModInfoDependencies] Mod 'AAA_SneakyMod' loads before StrongMods and is outside blocking coverage; its dependency
violations can only be reported
```

Two rules follow:

* **Don't name your mod's folder to sort before `000000-StrongMods`.** Winning the sort race gains you nothing except
  exemption from the checks that protect your own users.
* **Server admins: don't rename the `000000-StrongMods` folder.**

## Gotchas

**`name` is the internal `<Name>`, exactly.** Not the folder, not the `DisplayName`, and the match is case-sensitive:
`strongmods` does not match `StrongMods`. Open the target mod's `ModInfo.xml` and copy the value.

**A bare version is a minimum.** Worth repeating. `version="2.0"` happily accepts 3.0.

**`optional="true"` still enforces the version when the mod is present.** That's its whole purpose: "absent is fine,
present-but-ancient is not." An optional dependency without a `version` constraint does nothing at all.

**One `<Dependencies>` block, one `<Game>`, each mod once.** Repeats are authoring errors, and authoring errors unload
your mod.

**Unknown elements and attributes inside `<Dependencies>` are ignored**, reserved for future versions of the spec.
Don't invent your own — they carry no meaning and future versions may give them one.

## Pre-publish checklist

* `ModInfo.xml` is v2 (children directly under the root — no `<ModInfo>` child inside the root)
* Every `name` copied verbatim from the target mod's `<Name>`, case and all
* Every bare constraint is genuinely a minimum; `[x]` only where you truly mean exactly x
* Square vs. round brackets checked: `[` `]` inclusive, `(` `)` exclusive
* Soft integrations marked `optional="true"`
* Your `<Version>` is plain dot-separated numbers, so others can target it
* Your folder doesn't sort before `000000-StrongMods`

## Reference

### `<Dependencies>`

At most one per `ModInfo.xml`, a direct child of the root. Contains any number of `<Mod>` elements and at most one
`<Game>`.

### `<Game>`

| Attribute | Required | Notes                                                            |
|-----------|----------|-------------------------------------------------------------------|
| `version` | yes      | Constraint vs. the game version, `V` prefix and build suffix ignored |

### `<Mod>`

| Attribute  | Required | Default | Notes                                              |
|------------|----------|---------|-----------------------------------------------------|
| `name`     | yes      | —       | Target's internal `<Name>`, exact and case-sensitive |
| `version`  | no       | any     | Constraint vs. the target's `<Version>`             |
| `optional` | no       | `false` | Absent target is fine; present target must satisfy  |

### Constraints

| Form        | Reads as                        |
|-------------|----------------------------------|
| `1.2`       | at least 1.2                    |
| `[1.2]`     | exactly 1.2                     |
| `[1.2,2.0)` | at least 1.2, before 2.0        |
| `(,2.0]`    | no minimum, at most 2.0         |
| `3.*`       | any 3.x                         |
| `*`         | anything                        |

For the full specification — grammar, evaluation rules, validator requirements — see
`.ai/modinfo-dependencies-v1-spec.md` in the StrongMods repository.

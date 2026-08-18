# Overnight — transpiler match-point verification (#50 gap S1)

**Date:** 2026-08-17 (unattended session) · **Scope:** V3.1.0 b14 and V3.0.1 b4, game unit.
**Suite:** green, 403 passed.

## What was built

| File | What it is |
|------|------------|
| `Tests/Fixtures/IlReader.cs` | Reads a method's IL into Harmony `CodeInstruction`s using nothing but the BCL. |
| `Tests/TranspilerVerification.cs` | Resolves each transpiler's target and runs the transpiler against that target's real IL. |
| `Tests/TranspilerTests.cs` | Three tests: every transpiler still matches, the discovery still finds them all, and a negative control. |

**All 13 transpilers in the repo now verify against both declared game versions.** They are in BloodRain,
DynamicFeralSense (×2), DynamicLandClaimCount, QuestUnlockFixes, StrongMods `CaseSensitiveFilesystem` (×4),
and StrongUtils (×4, counting `LootCommandPatch` and `TouchlessLootContainers`).

## The check is to run the transpiler, not to model it

`TargetResolver` already proves the target *method* exists. What it cannot see is the *instruction sequence*
inside it — and a game update can leave a signature untouched while moving the call a transpiler keys on.
The seed doc framed the gap as "assert the CIL pattern a transpiler keys on still exists". Statically
recovering that pattern from a `CodeMatcher` chain would mean re-implementing Harmony's matcher; **executing
the transpiler against the real IL asks the same question and gets a better answer**, because the mod's own
`ThrowIfInvalid` message is what surfaces when the match point is gone.

Two failure signals, both meaningful:

1. **The transpiler throws.** Every transpiler here calls `ThrowIfInvalid` with a mod-specific message, so
   the failure names the pattern that went missing.
2. **The transpiler returns its input unchanged.** This catches the defensive kind that gives up silently —
   which is the failure mode that would otherwise ship a mod that quietly does nothing.

Nothing is patched: the IL is read, transformed in memory, and discarded.

## The obstacle: Harmony's own IL reader will not run here

`PatchProcessor.GetOriginalInstructions` routes through MonoMod, and MonoMod's runtime detection refuses this
test runner outright:

```
PlatformNotSupportedException: CoreCLR version 10.0.10 is not supported
  at MonoMod.Core.Platforms.Runtimes.CoreBaseRuntime.CreateForVersion(...)
```

Measured on the pinned `MonoMod.Utils 25.0.6` **and** on the newest published `25.0.14` (bumped, tested,
reverted — `Tests.csproj` is unchanged). The tests target modern .NET deliberately (#14), so waiting for
MonoMod was not an option.

`IlReader` reads the IL directly instead: `MethodBody.GetILAsByteArray()` plus `Module.ResolveMethod` /
`ResolveField` / `ResolveType` / `ResolveString`. That is pure BCL — it **removes** a dependency rather than
adding one. Fidelity is scoped to what a transpiler matches on: opcodes, and operands resolved to the same
reflection objects a `CodeMatch` compares against. Branch targets become real `Label`s attached to the
instruction they point at. Exception-handler regions are not reconstructed; no transpiler here inspects them,
and this IL is only ever read, never re-emitted.

## Two harness bugs the strictness caught

Both looked like mod bugs at first, and both were the harness's fault. Worth recording — anyone extending
this will hit them again.

1. **`MethodType.Enumerator` targets the compiler-generated `MoveNext`, not the declared method.** StrongMods
   patches `ModManager.LoadUiAtlases` and `LoadLocalizations` that way. Resolving the declared method reaches
   only the stub that constructs the state machine — no `Exists()` calls in it — so the mod's own
   `"Cannot find any Exists() calls."` fired. The fix is `AccessTools.EnumeratorMoveNext`, the same hop
   Harmony makes when it patches.
2. **`MethodInfo.ToString()` omits the declaring type.** Both `File.Exists` and
   `CaseSensitiveFilesystem.Exists` render as `Boolean Exists(System.String)`. StrongMods' transpilers swap
   one call for the other, so a before/after comparison on the bare `ToString()` showed **no change** and
   reported a healthy transpiler as broken. The comparison now spells out the declaring type.

The second one is the more instructive: a before/after diff is only as good as its rendering, and an
identical-signature swap is exactly what a transpiler often does.

## Guards on the guard

- **`The_transpilers_are_all_found`** fails if fewer than 13 are discovered. A rename that broke discovery
  would otherwise leave the suite green while asserting nothing.
- **`Negative_control_a_transpiler_that_cannot_match_is_reported`** runs a deliberately inert transpiler
  against a local target and asserts it is reported. This is the suite's own proof it can fail — the same
  pattern `SmokeTests.Negative_control_bogus_target_fails_with_diagnostics` established.

## Result

**Negative result, and a good one: no transpiler in the repo has lost its match point on either declared
version.** The seed doc singled out DynamicLandClaimCount's transpiler as "Linux-fragile" (its own source
comment says a `CodeMatch.LoadsConstant` did not match on Linux and was commented out). This harness cannot
speak to Linux — it runs on the Windows game tree — but it does now pin the Windows behavior on both
versions, so a future change to that matcher has a regression test that did not exist yesterday.

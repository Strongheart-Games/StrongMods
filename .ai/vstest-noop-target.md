# Plan: a no-op `VSTest` target for modlets and overlays (issue #51)

- **Issue:** [#51](https://github.com/Strongheart-Games/StrongMods/issues/51) — status lives there.
- **Goal:** make `dotnet test StrongMods.sln -c Debug` — the invocation CLAUDE.md documents under *Verifying* —
  stop emitting 11 `MSB4057` errors and stop failing on account of them.
- **Scope:** two lines of MSBuild plus their explanatory comments, in `build/Modlet.targets` and
  `build/Overlay.targets`. **Nothing under `Tests/`**, no CLAUDE.md change, no C#.

## 1. Root cause (spiked 2026-08-02)

`dotnet test` forwards the `VSTest` target to every project in the solution. That target is not part of MSBuild
proper — it arrives via an auto-import:

```
Microsoft.Common.targets\ImportAfter\Microsoft.TestPlatform.ImportAfter.targets
  └── imports $(MSBuildExtensionsPath)\Microsoft.TestPlatform.targets   (defines Target Name="VSTest")
```

Anything importing `Microsoft.Common.targets` — i.e. every `Microsoft.NET.Sdk` project — gets `VSTest` for free.
The repo's 9 modlets and 2 overlays are bare `<Project>` files that import only their `build\` entry point, so the
target simply does not exist for them, and MSBuild raises `MSB4057`.

**The no-op is the conforming shape, not a workaround.** The SDK's own `VSTest` already does nothing for a
non-test project: its real work lives in `_VSTestMSBuild`, which is `Condition="'$(IsTestProject)' == 'true'"`.
So the SDK's contract is *every project answers to `VSTest`; non-test projects answer by doing nothing*. Our 11
projects break that contract by not answering at all. Setting `IsTestProject=false` would not help — the target
still would not exist to be called.

Affected set, confirmed by measurement — exactly the non-SDK shapes:

| Entry point | Projects |
| --- | --- |
| `build/Modlet.targets` | AECInternationalMarketFixes, AECVehiclesFixes, PlayerSpawnedTraders, PootPavillion, ProgressiveBiomes, ProjectZFixes, RefugeHordeBaseS11, StrongMining, StrongholdTweaks |
| `build/Overlay.targets` | Hades, StrongholdSaves |

## 2. The change

One target in each of the two entry points, beside the `_IsProjectRestoreSupported` precedent it mirrors:

```xml
<!-- `dotnet test` on the solution forwards VSTest to every project. SDK projects get the target from
     Microsoft.TestPlatform.ImportAfter.targets (auto-imported by Microsoft.Common.targets) and no-op it for
     non-test projects; a bare <Project> never sees that import, so without this the whole command fails with
     MSB4057. Same shape as _IsProjectRestoreSupported below and the no-op Deploy in Tests.csproj: a project
     that cannot participate in a solution-wide target says so with an empty target, not an error. -->
<Target Name="VSTest" />
```

`Mod.targets` needs nothing — code mods are SDK projects and already inherit the real target.

## 3. Verification

The pass criterion is **not** "exit 0" — see §4; the suite is red at HEAD for an unrelated, in-flight reason.
What this change must show:

1. **The errors are gone.** `dotnet test StrongMods.sln -c Debug`: `MSB4057` count goes 11 → 0.
   *Spiked and already confirmed in a throwaway worktree.*
2. **No new test failures.** Same passed/failed counts as a `HEAD` baseline run of
   `dotnet test Tests/Tests.csproj -c Debug`, captured immediately before the change.
3. **Build unaffected.** `dotnet build StrongMods.sln -c Debug` still exits 0 with 0 warnings.
4. **Deploy unaffected.** Redirected solution `-t:Deploy` into `.scratch/` still yields the same 28 mod folders
   plus the one StrongholdSaves file — proof the new target did not disturb the entry points it sits in.
5. **Evaluation diff** across all 32 projects vs a `HEAD` worktree: adding a target must not change any evaluated
   property. Expect a clean diff everywhere, no exceptions.

Once the `_Dump` situation in §4 resolves, `dotnet test StrongMods.sln -c Debug` should exit 0 — that is the
issue's stated acceptance criterion and the natural moment to confirm it.

## 4. Coordination — findings in `Tests/`, not touched

The spike surfaced two things in the in-flight foreach effort's territory. Both are **reported, not acted on**;
they belong to that effort and to whatever ordering its owner prefers.

1. **`Tests.Foreach._Dump.Dump` fails at HEAD locally** — 116 passed, 1 failed on
   `dotnet test Tests/Tests.csproj -c Debug` at `6ec2641`. It reads as a deliberate dump-by-failing diagnostic.
   Notable wrinkle: **CI is green at that same SHA**, so something differs between the two environments. Not
   diagnosed here. Consequence for #51: "the suite passes" cannot be the acceptance signal yet, hence §3's
   framing.
2. **`Tests/FunctionMod` has a latent build-ordering hazard.** It references StrongMods by
   `<HintPath>..\..\StrongMods\bin\$(Configuration)\StrongMods.dll`, with no `ProjectReference` and no place in
   `StrongMods.sln` — it is built only as a `ReferenceOutputAssembly=false` project reference of `Tests.csproj`,
   which itself does not depend on StrongMods. So nothing orders StrongMods ahead of it. In a clean worktree the
   spike hit exactly this: 5× `CS0246` on `XmlPatchFunctionAttribute` until StrongMods was built by hand. CI has
   passed every run so far, so this is a latent risk rather than an observed failure — but the ordering is not
   guaranteed by anything, and a clean-checkout `dotnet build StrongMods.sln` can schedule Tests before
   StrongMods.

Neither blocks #51, and this plan changes no file under `Tests/`.

## 5. Out of scope

- Anything under `Tests/` (§4).
- Changing CLAUDE.md: the *Verifying* section already documents the solution-scoped command; this change makes
  that documentation true rather than requiring an edit.
- Changing CI to use the solution-scoped invocation. `build.yml:89` runs `dotnet test Tests/Tests.csproj`, which
  works and is faster; whether to converge the two is a separate call.
- The `dotnet test` / Microsoft.Testing.Platform migration question in .NET 10 generally.

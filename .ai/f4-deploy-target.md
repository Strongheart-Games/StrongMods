# Plan: F4 — a real Deploy step, separate from Build (issue #13)

- **Issue:** [#13](https://github.com/Strongheart-Games/StrongMods/issues/13) — status lives there.
- **Goal:** building never touches a live install; deploying is an explicit, requested act. This is the seam
  [#23](https://github.com/Strongheart-Games/StrongMods/issues/23)'s declarative version pinning hangs on:
  *compile against what is declared, deploy to what is installed.*
- **Scope:** `build/Mod.targets`, `build/Modlet.targets`, `StrongholdTweaks.csproj`, CLAUDE.md. No C# changes.
  Templates unchanged (verified, not edited).

## 1. Current state, precisely

- **Code mods** (`Mod.targets`): Debug sets `OutputPath` to `$(ModsDir)\$(ModDeployName)\` — deploy *is* build.
  Release goes to `bin\Release\`. `ModDeploy=false` (templates) forces `bin\` always.
- **Modlets** (`Modlet.targets`): same `OutputPath` switch; the hand-written `Build` target copies content
  straight to the deploy folder. `Clean` **deletes the deployed folder** (gated by `ModletCleanEnabled` — the
  Hades guard for its un-versioned `Worlds\`). The SDK's `Clean` on code mods likewise deletes build output from
  the live install today.
- **StrongholdTweaks**: `CopySaves` runs `AfterTargets="Build"` — every solution build writes toward the real
  `%APPDATA%\7DaysToDie\Saves` unless `SavesOutputPath` is redirected (the F1 Phase-0 finding).

## 2. Design

### An explicit `Deploy` target; `ModDeploy` stays as the capability gate

The deploy *request* is a target, not a property (revised at plan review, 2026-07-29 — the owner's suggestion,
and better than the property design for two verified reasons):

- **Modern MSBuild forwards an unknown `-t:` target through a `.sln` to every project** — demonstrated on both
  toolchains (the MSB4057s came from the projects, not the solution metaproject). Once `Deploy` exists in
  `Mod.targets` and `Modlet.targets`, every project has it and `-t:Deploy` traverses the solution. The old
  "custom targets don't flow through solutions" lore is stale. Also verified: no built-in `Deploy` target exists
  on either project shape, so the plain name is free.
- **MSBuild treats environment variables as properties.** A request *property* could be silently switched on by
  a stray variable in some environment; a *target* can only run because someone asked for it.

`ModDeploy` survives unchanged as the per-project *capability* (templates: `false`) gating the target's
`Condition`. A global property can never force a deploy — there is no request property to set.

### Build stages, Deploy copies

`OutputPath` becomes `bin\$(Configuration)\` unconditionally, for both shapes. After `Build`, `bin\` holds
exactly the shippable mod folder — a stable artifact contract that #14 (tests), #22 (CI), and any future
packaging step can rely on. Deployment is a copy of that staged folder:

```xml
<Target Name="Deploy" DependsOnTargets="Build" Condition="'$(ModDeploy)' == 'true'">
  <!-- copy $(OutDir)** -> $(ModsDir)\$(ModDeployName)\ (copy-if-newer, like the modlet copy today) -->
</Target>
```

The name is deliberately `Deploy`, not `DeployMod`: it is what a developer types, and not everything deployed is
a `Mods\` folder — StrongholdTweaks' saves content deploys too, via its `CopySaves` hooking
`AfterTargets="Deploy"`. "Deploy this project's content where it belongs" is the honest description.

```bash
dotnet build StrongMods.sln -c Debug              # build everything; touches nothing outside the repo
dotnet build StrongMods.sln -c Debug -t:Deploy    # build and install into the live game
```

**Design revised during Phase 1 review (2026-07-29/30), before Phase 2:**

- **Deploy is a mirror, not a copy** — source is authoritative for content *and existence*; files removed from
  source are deleted from the deployed folder at the next deploy (the scalpel for "a removed file keeps doing its
  thing"; `Clean` stays out of the live install). The mirror is strictly scoped to `$(ModsDir)\$(ModDeployName)\`.
- **The shared machinery lives in a new `build/Deploy.targets`**, imported by `Mod.targets` and `Modlet.targets`
  (not by projects — same pattern as `GamePaths.props`). The `ModDeploy`/`ModDeployName` defaults move there.
- **Mirror semantics assume the repo manages the whole folder — and two projects violate that.** That discovery
  became the **Overlay project type** ([#25](https://github.com/Strongheart-Games/StrongMods/issues/25)):
  protective-additive deploys with `MirrorOnDeploy` opt-in globs, Hades converting to it, and `StrongholdSaves`
  splitting out of StrongholdTweaks. Within #13: **Hades is parked** (`ModDeploy=false`, comment pointing at
  #25) — its live folder holds ~400 MB of unmanaged world binaries
  ([#26](https://github.com/Strongheart-Games/StrongMods/issues/26)) that a mirror would delete.
  `ModletCleanEnabled` retires now (its purpose — protecting deployed files from `Clean` — no longer exists).
- **StrongholdTweaks' `CopySaves` hooks `AfterTargets="Deploy"`** (interim, until the #25 split); plain builds
  never approach the saves folder.

Consequences, all wanted:

- **`Clean` stops reaching into the game.** It cleans `bin\` staging only. Removing a deployed mod from the live
  install becomes a deliberate manual act (or a future Undeploy target — out of scope). `ModletCleanEnabled`
  keeps working, with much lower stakes; Hades' `Worlds\` is doubly safe.
- **The `SavesOutputPath` redirect protocol dies.** `CopySaves` hooks `AfterTargets="Deploy"`, so plain
  builds never approach the saves folder at all.
- **Release becomes deployable** (`-c Release -t:Deploy`) — a new, coherent capability; previously
  Release could never deploy.
- **Verification builds no longer need `-p:ModsDir=` for safety** — a plain build cannot touch a live install.
  The redirect stays useful for *testing the deploy step itself* against scratch.
- Modlets keep their copy-if-newer semantics in both hops (source → staging → live).

### Interaction with #23, deliberately deferred

`ModsDir` still derives from `$(SdtdDir)` — so deploying while `-p:SdtdDir=vendor/...` would "deploy" into the
vendor tree. Documented footgun (don't combine them), the same one that exists today. When #23 flips the default
`SdtdDir` to a vendored tree, it must re-anchor the deploy destination to a live-install property; cutting that
seam now would smuggle #23's design into #13. One line in CLAUDE.md marks it.

## 3. Phases

| # | Work |
| --- | --- |
| 0 | Baseline: redirected full-solution build at HEAD; capture every mod's deployed file set (the parity oracle) |
| 1 | `Mod.targets`: unconditional `bin\` OutputPath + `Deploy` target. Verify code mods |
| 2 | `Modlet.targets`: `Build` stages to `bin\`, `Deploy` copies staged → live, `Clean` note; `StrongholdTweaks` moves `CopySaves` to `AfterTargets="Deploy"`. Verify modlets |
| 3 | Templates verified under `-t:Deploy` (must stay inert); CLAUDE.md *Deploying*/*Verifying* rewrite; results; human live-deploy check; close |

## 4. Verification

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Eval diff, all projects | `OutputPath`/`OutDir`/`TargetDir` → `bin\$(Configuration)\` (the intended change); nothing else moves |
| V2 | **Plain build writes nothing outside the repo** | Full solution, both toolchains, no `-p:` redirects at all: live `Mods\` (game + server), saves, and `vendor/` all untouched; every artifact under `bin\`/`obj\` |
| V3 | **Deploy parity** | `-t:Deploy` with `ModsDir` redirected to scratch: deployed sets file-identical to the Phase-0 oracle for every mod (content hashes; dll/pdb sizes) |
| V4 | Capability gate | Templates (`ModDeploy=false`) deploy nothing even under `-t:Deploy` |
| V5 | Saves gating | Plain build: saves path untouched with **no** redirect in play; deploy request + redirect: `SavesContent` lands in the redirect |
| V6 | Release deploy | `-c Release -t:Deploy` (redirected) deploys the Release bits |
| V7 | Clean semantics | `Clean` removes only `bin\` staging; a populated scratch "live" folder is untouched |
| V8 | Vendor interplay | Build with `-p:SdtdDir=vendor\game\<label>` (no deploy request): compiles, `bin\` only, vendor tree unmodified |
| V9 | Live deploy (human) | One real `-t:Deploy` against the live game, reviewer confirms the install updated — the actual feature, exercised once for real |

## 4b. Phase 0–1 results

**Phase 0 (2026-07-29):** oracle captured at commit `bc0633a` — redirected full-solution build, 28 mod folders /
188 files plus 1 saves file, all hashed (`.scratch/f4-baseline/manifest.json`).

**Phase 1 (2026-07-29):** `Mod.targets` reworked — `OutputPath` unconditionally `bin\$(Configuration)\`; the
`Deploy` target added (`DependsOnTargets="Build"`, gated on `ModDeploy`, copy with `SkipUnchangedFiles`).

| Check | Result |
| --- | --- |
| V1 (code mods + template) | ✅ 19/19 evaluate `OutputPath`/`OutDir` = `bin\Debug\` (note: `OutDir` reports *relative* now that `OutputPath` is relative — earlier absolute values came from the absolute deploy path) |
| Plain build safety | ✅ Unredirected project builds write nothing outside the repo; `bin\` holds the shippable folder |
| V3 (code mods) | ✅ Per-project `-t:Deploy` (redirected): 95 files across all 18 code mods file-identical to the oracle (content hashes; dll/pdb sizes) |
| V4 | ✅ `Template7DtDMod -t:Deploy` exits 0 and deploys nothing — the capability gate holds |
| V7 | ✅ `dotnet clean` clears staging only; the deployed copy is untouched |
| V8 | ✅ Vendor-tree build stays in `bin\`, tree unmodified |
| V5/live | ✅ Game, server, saves untouched throughout |

Transition note: until Phase 2, modlets still deploy-as-build, so **solution-level** builds/`-t:Deploy` remain
redirected; per-project invocations on code mods are already safe unredirected.

**Phase 2 (2026-07-30):** shared `build/Deploy.targets` created (mirror semantics, `ModDeploy`/`ModDeployName`
defaults moved there); `Mod.targets` Phase-1 target replaced by the import; `Modlet.targets` stages to `bin\`
with a plain skip-unchanged copy (the copy-if-newer condition retired — protective semantics belong to the
Overlay type, [#25](https://github.com/Strongheart-Games/StrongMods/issues/25)), `ModletCleanEnabled` retired;
Hades parked (`ModDeploy=false`, pointing at #25/#26); StrongholdTweaks' `CopySaves` gated behind `Deploy`.

| Check | Result |
| --- | --- |
| V2 | ✅ Plain solution build, **no redirects**, both toolchains: exit 0, warnings at baseline, nothing outside `bin\`/`obj\` — live game, server, saves, and `vendor/` all untouched |
| V3 | ✅ Solution-level `-t:Deploy` (forwarding proven in practice): 165 files / 27 mods file-identical to the oracle; Hades correctly absent (parked); templates correctly absent (gate holds) |
| V5 | ✅ Saves content deploys only on the deploy request, oracle-identical; plain builds never approach the saves path |
| Mirror | ✅ Planted stale file + stale subdirectory file both deleted on redeploy, announced in the log (`removed stale: …`). Accepted limitation: emptied *directories* remain (files are mirrored, empty dirs are inert cosmetic residue) |
| V7 | ✅ Modlet `Clean` removes staging only; deployed copies untouched |
| Live | ✅ Untouched throughout |

**Phase 3 (2026-07-30):** CLAUDE.md rewritten — *Deploying* (the "building in Debug *is* the install step" era
ends; `-t:Deploy` commands; mirror semantics; the Hades/Overlay note; the stale `PrismaCoreFixes` reference
removed), *Building without the game* (vendored builds safe by default, `ModsDir` redirect no longer required),
*Verifying* (real builds inherently safe; redirects only for testing the deploy step), the Filesystem Scope
bullet (`Deploy` target, not "Debug build", is the install act), and a `Deploy.targets` row in the shared-files
table. Both templates re-verified inert under an explicit `-t:Deploy`. Remaining: V9 — the owner runs one real
`-t:Deploy` against the live game and confirms the install updates; then #13 closes.

**Phase 3 addendum (2026-07-30, from V9 review feedback):** the long-standing NU1503 baseline warnings (10×, one
per modlet, on every solution restore) are gone. NuGet probes each project for a `_IsProjectRestoreSupported`
target and warns for projects that lack it; `Modlet.targets` now declares it (returning the project — an empty
target verifiably does not suppress). Restore is a genuine no-op for modlets: no `obj\` artifacts appear, and
both toolchains now build the full solution with **zero warnings** — which also clears the runway for
warnings-as-errors in CI someday. Failure mode if NuGet ever changes this internal contract: the warnings return,
nothing breaks.

**Property rename (2026-08-02, [#24](https://github.com/Strongheart-Games/StrongMods/issues/24)):** the capability
gate this doc calls `ModDeploy` is now **`IsDeployable`**, spelled after the SDK's `IsPackable`/`IsPublishable`.
Behavior is unchanged; `ModDeploy` above is the historical name. Rationale in `.ai/is-deployable-rename.md`.

## 5. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Muscle-memory break: Debug build no longer installs | The point of the change, but a real workflow shift | The one-liner `-t:Deploy`; CLAUDE.md rewrite. **Immediate flip confirmed by the owner (2026-07-29)**: no parallel work in flight, no other collaborators yet, and VCS covers rollback — the only real risk is *staying* broken, not breaking. Property-name follow-up: [#24](https://github.com/Strongheart-Games/StrongMods/issues/24) |
| Stale deployed mods drift from source | Deploys are now explicit, so a forgotten deploy means the game runs old bits | Unchanged from today in kind (Release always worked this way); the deploy copy is copy-if-newer and cheap to run |
| Double copy for modlets (source → staging → live) | Negligible I/O; buys the uniform artifact contract | Accepted |
| IDE flows | Rider builds still work (build-only); deploying from the IDE needs a run config invoking the `Deploy` target | Document; owner validates in review (V9 adjacent) |

# Plan: rename `ModDeploy` → `IsDeployable` (issue #24)

- **Issue:** [#24](https://github.com/Strongheart-Games/StrongMods/issues/24) — status lives there. Raised during
  #13's plan review and deliberately deferred; #13 and #25 have both landed and settled.
- **Goal:** rename the per-project capability gate on the `Deploy` target from `ModDeploy` to `IsDeployable`,
  because the "Mod" in the name is content-specific where the concept is not.
- **Scope:** the two shared build entry-point pairs, both `dotnet new` templates, `Tests.csproj`, CLAUDE.md, and
  one dated note in `.ai/f4-deploy-target.md`. Pure rename plus two stale comments — no behavior change, no C#.

## 1. The decision

`IsDeployable`, chosen over the issue's `Deployable` and the other candidates weighed on 2026-08-02.

**The convention it joins.** The .NET SDK already names exactly this shape of thing `Is<Verb>able`: `IsPackable`
gates `Pack`, `IsPublishable` gates `Publish`. `IsPublishable` exists to solve the identical problem this repo
hit — a solution-scope invocation reaching projects that must not participate, which is verbatim what the
`Deploy` no-op in [`Tests/Tests.csproj`](../Tests/Tests.csproj) works around. Adopting the SDK's spelling means a
reader who knows `IsPackable` already knows this property.

**Why not the alternatives.**

| Candidate | Rejected because |
| --- | --- |
| `Deployable` | Same semantics, but "deployable" is a live noun in ops usage ("ship the deployable"), so `$(Deployable)` can read as an artifact or path at a glance. The `Is` costs two characters and forecloses that reading. |
| `DeployEnabled` / `EnableDeploy` | The local precedent (`XmlLintEnabled`) argues against it: that is a **run-scoped bypass** passed as `-p:XmlLintEnabled=false`, whereas this is a **permanent per-project declaration** never passed on a command line. Reserving `*Enabled` for switches and `Is*able` for capabilities keeps the two categories greppable apart. |
| `Deploy` | `-p:Deploy=true` and `-t:Deploy` are one character apart with entirely different meanings, and `grep Deploy` already returns the whole build tree. |
| `SkipDeploy` | Inverts the default, so the gate becomes `!= 'true'`. Negative-polarity flags are a readability trap and the `IsPackable` parallel is lost. |
| `IsInstallable` | Accurate — CLAUDE.md does say "installing" — but the target is `Deploy`, and the gate should share the target's stem. |

**Collision check.** `IsDeployable`, `Deployable`, `DeployEnabled`, and `EnableDeploy` appear in zero `.targets`
or `.props` files under the .NET 10.0.302 SDK on this machine (for calibration: `IsPackable` appears in 8,
`IsPublishable` in 4, `DeployOnBuild` in 3 — so `*OnBuild` spellings are genuinely taken). No VS MSBuild is
installed here, so that is the scope actually verified. Legacy VS deployment project systems (SharePoint, Smart
Device, `.vdproj`) historically defined an `IsDeployable`; no project in this repo imports those target sets, so
the residual risk is low rather than zero.

**The sharper form of the argument.** Not one of the three projects that sets the property today is a mod: two are
`dotnet new` templates and one is a modern-.NET xunit project. Its users are precisely the non-mods.

## 2. Sibling properties stay as they are

`ModDeployName` and `ModStagingDir` keep their `Mod` prefix — it is accurate there. `ModDeployName` is the folder
name beneath `$(ModsDir)`, a Mod/Modlet convention that overlays deliberately do not have (they set `DeployRoot`
instead). A side benefit of renaming only the gate: `ModDeploy`/`ModDeployName` currently *look* like a matched
pair when they are nothing alike — one is a universal capability, the other a `Mods\`-folder naming rule. The
rename breaks a false pairing rather than creating an inconsistency.

No back-compat shim. Honoring a legacy `$(ModDeploy)` would leave a second spelling to grep forever, and every
call site is in this tree.

## 3. Edits

Eight files, ~15 changed lines.

| File | Change |
| --- | --- |
| `build/Deploy.targets` | Default (`:59`) and gate (`:69`); rewrite the property's doc comment (`:57-58`) to cite the `IsPackable`/`IsPublishable` precedent and drop the `#24` pointer; fix the stale Hades sentence (`:15-16`, see §4). |
| `build/Overlay.props` | Default (`:22`). |
| `build/Overlay.targets` | Gate (`:62`); extension-point list (`:30`), dropping `(renaming: #24)`. |
| `Template7DtDMod/Template7DtDMod.csproj` | `:7`, inside the `#if (IsTemplate)` block. |
| `Template7DtDModlet/Template7DtDModlet.csproj` | `:7`, same. |
| `Tests/Tests.csproj` | `:17` — lands directly beneath `IsPackable`, which is the point. |
| `CLAUDE.md` | `:105` (also stale, see §4) and `:262`. |
| `.ai/f4-deploy-target.md` | One dated line recording the rename; that doc is the live design record for the `Deploy` target. |

The remaining `.ai/` mentions (`build-refactor-plan.md`, `f1-sdk-migration.md`, `load-order-tiers-plan.md`,
`overlay-project-type.md`) stay untouched — they are frozen records of what was decided when.

The template blocks are the one delicate spot: `dotnet new` strips `<!--#if (IsTemplate) -->` textually, and
`IsTemplate` is a `bool` parameter symbol, not a text-substitution symbol (`sourceName` is the only substitution
in either `template.json`). So the new property name cannot interact with stripping — but scaffolding is retested
anyway (§5 step 4), per #12's coverage.

## 4. Two stale comments swept along

Both sit on lines this rename already touches, and both went stale when #25 landed (2026-07-31) and Hades
converted to an Overlay — [`Hades/Hades.csproj`](../Hades/Hades.csproj) no longer sets the property at all.

1. `build/Deploy.targets:15-16` — "Hades is parked with `<ModDeploy>false</ModDeploy>` until it converts
   (issue #25)". Hades converted. Replace with a plain pointer to `build\Overlay.targets` as the shape to use
   when a deploy folder holds unmanaged content, keeping Hades as the example.
2. `CLAUDE.md:105` — "marks a project that never deploys (both templates; `Hades`)". Wrong on Hades and silent on
   `Tests`. Becomes "(both templates; `Tests`)".

This stays within single-focus: no functional change rides along, and leaving a comment that names the renamed
property with an obsolete example would be knowingly shipping a wrong line.

## 5. Verification

1. **Evaluation diff.** `build/tools/compare-eval.cs` against a `HEAD` worktree in `.scratch/`, querying
   `-getProperty:ModDeploy,IsDeployable,ModDeployName,OutDir,TargetDir` for every project. Expected: `OutDir`,
   `TargetDir`, and `ModDeployName` byte-identical everywhere; the set of projects reading `false` is the same set
   before (`ModDeploy`) and after (`IsDeployable`) — the two templates and `Tests`, everything else `true`; and
   `ModDeploy` reads empty everywhere post-change.
2. **Full build.** `dotnet build StrongMods.sln -c Debug`.
3. **Redirected deploy.** `dotnet build StrongMods.sln -c Debug -t:Deploy -p:ModsDir=.scratch/deploy
   -p:SdtdSavesDir=.scratch/saves`, then confirm `.scratch/deploy` contains no template or `Tests` output — the
   gate still gating is the whole point of the change.
4. **Scaffolding retest.** `dotnet new` from both templates into `.scratch/`; the generated `.csproj` must contain
   no `IsDeployable` line at all (block stripped), and must evaluate `IsDeployable` to `true`.
5. **Test suite.** `dotnet test StrongMods.sln -c Debug` — unaffected, but cheap insurance that `Tests.csproj`
   still loads.
6. **Final sweep.** `grep -r ModDeploy` returns hits only in `ModDeployName` and the four frozen `.ai/` docs.

### Results (2026-08-02)

| Check | Result |
| --- | --- |
| 1. Evaluation diff | ✅ All 32 solution projects vs a `HEAD` worktree. Every diff is either the two expected gate keys or worktree-path noise. `OutputPath`, `OutDir`, `ModDeployName`, `ModsDir`, `DeployRoot`, `ModStagingDir`, `ModLoadPrefix` identical everywhere — none appears in any diff. `TargetDir`/`TargetPath` differ only by the `.scratch\baseline-24\` prefix (they are absolute; `OutDir` is relative, hence clean — the trap the `compare-eval` header warns about, seen from the other side) |
| 1b. Gate map preserved | ✅ Baseline `ModDeploy` and post-change `IsDeployable` read `false` for exactly the same three projects — `Template7DtDMod`, `Template7DtDModlet`, `Tests` — and `true` for the other 29. `ModDeploy` now evaluates empty in all 32 |
| 2. Full build | ✅ `dotnet build StrongMods.sln -c Debug`: exit 0, **0 warnings**, 0 errors |
| 3. Redirected deploy | ✅ Solution `-t:Deploy` into `.scratch/`: 28 mod folders + the one `StrongholdSaves` file (`StrongMods\custom_chat_commands.xml`) = 29 deploying projects. Neither template nor `Tests` produced anything — the gate holds under its new name |
| 4. Scaffolding | ✅ Both templates installed, instantiated, uninstalled (machine left as found). Generated `.csproj`s contain **no** `IsDeployable` line — stripped to the canonical 4-line mod / 3-line modlet shapes — and evaluate `IsDeployable` = `true`, `ModDeploy` = empty |
| 5. Test suite | ✅ `dotnet test StrongMods.sln -c Debug`: 94 passed, 0 failed |
| 6. Final sweep | ✅ No live build file or `.csproj` mentions the old name. Remaining hits are `ModDeployName`, the four frozen `.ai/` docs, and the deliberate historical references in this doc and `f4-deploy-target.md` |

Noted, not acted on: `dotnet test` on the solution emits 11 `MSB4057: The target "VSTest" does not exist`
errors — the non-SDK modlet and overlay projects have no `VSTest` target for `dotnet test` to forward to. The
count is **11 both before and after** this change, so it is pre-existing and unrelated; the run still passes.
Worth its own issue, not this one.

## 6. Out of scope

- Renaming `ModDeployName`, `ModStagingDir`, or any other `Mod*` build property (§2).
- Any change to deploy *semantics* — mirror, protective-additive, tiers, staging all stay exactly as they are.
- Rewriting the four frozen `.ai/` design docs.

# Plan: the Overlay project type (issue #25)

- **Issue:** [#25](https://github.com/Strongheart-Games/StrongMods/issues/25) — status lives there. Semantics were
  settled in #13's review (recorded in the issue); this plan is the implementation design.
- **Goal:** a third project shape for deploying into directories the repo doesn't fully manage —
  protective-additive by default, `MirrorOnDeploy` for declared managed scopes. Un-parks Hades; splits
  `StrongholdSaves` out of StrongholdTweaks; retires the saves special-casing.
- **Scope:** `build/Overlay.targets` (new), one `GamePaths.props` property, `Hades.csproj`, the
  StrongholdTweaks/StrongholdSaves split, `StrongMods.sln`, CLAUDE.md. No C# changes.

## 1. Semantics (settled in #13 review; restated for implementation)

| | Mod / Modlet | Overlay |
| --- | --- | --- |
| Deploy dir | Fully repo-managed | Contains unmanaged content (third-party, game-generated, or authored out-of-band) |
| Default per file | Mirror everything | **Protective-additive**: copy if absent or newer, never overwrite newer live edits, never delete |
| Managed scopes | Implicit: everything | **`MirrorOnDeploy`** declarations: authoritative overwrite + stale deletion, scoped |

A Mod is an overlay that mirrors everything; the entry points stay separate for clarity.

## 2. Design

### `MirrorOnDeploy` declares identities, not globs

The natural-looking `<MirrorOnDeploy Include="Config\**\*" />` cannot work: MSBuild expands item globs against
the **source** tree at evaluation time, so the *pattern* is gone before deploy needs to re-apply it to the
**deploy root** for stale detection. Instead, declarations are literal directory or file identities:

```xml
<ItemGroup>
  <MirrorOnDeploy Include="Config" />                         <!-- a directory: mirrored recursively -->
  <MirrorOnDeploy Include="ModInfo.xml" />                    <!-- a single file -->
  <MirrorOnDeploy Include="Worlds\Hades S6\WalkerSim.xml" />  <!-- a file deep in unmanaged territory -->
</ItemGroup>
```

No wildcards → nothing expands at evaluation; the target appends `\**\*` to directory identities on both the
staging and deploy sides at execution time (`Exists`-style directory test distinguishes the two). This is also
the more honest declaration — what a project manages is directories and files, not patterns. v1 restriction:
no partial-directory patterns like `Config\*.xml`; carve the directory instead.

### `build/Overlay.targets` — the third entry point

Same skeleton as `Modlet.targets` (imports `GamePaths.props`; `Content` staging glob with
`OverlayContentExclude`; `Build` stages to `bin\$(Configuration)`; `Clean` removes staging; `Rebuild`), plus its
own `Deploy` target — **same target name**, so `-t:Deploy` on the solution reaches overlays too, but overlay
semantics:

1. `_MirrorStaged` / `_MirrorDeployed`: files under `MirrorOnDeploy` identities, globbed at execution on both
   sides. Copy staged→deployed with `SkipUnchangedFiles`; delete deployed-not-staged (scoped mirror).
2. `_ProtectedStaged`: everything else staged. Copy only where absent-or-newer — the named
   `IsNewerThanDest` metadata idiom (the one place the ticks-comparison condition lives, with its why-comment).
3. Never touches anything outside its copied files: no unscoped deletion, ever.
4. `ModDeploy` gates as everywhere (`Deploy.targets`' defaults are reused via import for the properties, but the
   overlay defines its own `Deploy` — import order note: `Overlay.targets` must define `Deploy` itself and NOT
   import `Deploy.targets`, whose mirror target would collide. The `ModDeploy`/`ModDeployName` defaults are tiny;
   duplicating two property lines beats a shared-file split, and #24's rename will touch both anyway.)
5. **`DeployRoot`** replaces the `$(ModsDir)\$(ModDeployName)` convention: every overlay states its destination
   explicitly (`$(ModsDir)\Hades`; `$(SdtdSavesDir)`). A readable error if unset.

### `GamePaths.props`: `SdtdSavesDir`

The game's save-data root joins the path family: default `$(AppData)\7DaysToDie\Saves`, overridable like the
rest (the dedicated server's saves location can be set per-machine/-invocation when needed). Replaces
StrongholdTweaks' private `SavesOutputPath` default.

### Hades converts (and un-parks)

```xml
<Project DefaultTargets="Build" xmlns="...">
  <PropertyGroup>
    <DeployRoot>$(ModsDir)\Hades</DeployRoot>
  </PropertyGroup>
  <ItemGroup>
    <MirrorOnDeploy Include="Config" />
    <MirrorOnDeploy Include="ModInfo.xml" />
    <MirrorOnDeploy Include="README.md" />
    <MirrorOnDeploy Include="Worlds\Hades S6\WalkerSim.xml" />
  </ItemGroup>
  <Import Project="..\build\Overlay.targets" />
</Project>
```

`Prefabs\**` needs no declaration: protective by default (live prefab-editor edits survive; prefabs created only
in-game survive). The ~400 MB of world binaries are unmanaged and untouchable by construction
([#26](https://github.com/Strongheart-Games/StrongMods/issues/26) owns their lifecycle). The
`MirrorOnDeploy` list is the owner's call at review.

### `StrongholdSaves` splits out

New overlay project; the files move from `StrongholdTweaks\Saves\**` to the new project root (the `Saves\`
prefix drops — the project root *is* the saves-relative tree):

```
StrongholdSaves/StrongMods/custom_chat_commands.xml
StrongholdSaves/StrongholdSaves.csproj   (DeployRoot=$(SdtdSavesDir); MirrorOnDeploy=StrongMods\custom_chat_commands.xml)
StrongholdSaves/README.md
```

File-level mirror (not the `StrongMods` directory): that directory in live saves is runtime territory
(KeyValueStore etc.); the overlay manages exactly the one config file. StrongholdTweaks then loses
`SavesContent`/`CopySaves`/`SavesOutputPath`/`ModletContentExclude` and becomes a four-line ordinary modlet.
`StrongMods.sln` gains the project (classic project-type GUID, like modlets).

### Recorded, not implemented: the property-merge flavor

Overlaying values *within* a file (serverconfig.xml per-property) is flavor two — same taxonomy, different merge
granularity. Nothing in v1's file-level design precludes it; it gets its own design pass when picked up
(tracked in #25's body).

## 3. Phases

| # | Work |
| --- | --- |
| 0 | Baselines: hash inventory of live `Mods\Hades\` (the unmanaged-preservation oracle) and live saves file; refresh the deploy oracle at HEAD |
| 1 | `Overlay.targets` + `SdtdSavesDir`; convert Hades (un-park). Verify overlay semantics against scratch roots |
| 2 | StrongholdSaves split; StrongholdTweaks cleanup; sln. Verify |
| 3 | CLAUDE.md (project-shapes line, shared-files table, Deploying notes — Hades un-parked); results; owner runs the real Hades deploy (V-live); close |

## 4. Verification

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Additive safety | Scratch `DeployRoot` pre-seeded with fake unmanaged files (worlds stand-ins, files inside `Prefabs\`): after `-t:Deploy`, every unmanaged file survives byte-identical |
| V2 | Protective copy | Pre-seeded *newer* copy of a tracked prefab is not overwritten; *older* copy is; absent file is copied |
| V3 | Scoped mirror | File removed from a `MirrorOnDeploy` scope (staged) is deleted from the deploy root; file *outside* the scope is never deleted |
| V4 | StrongholdSaves | `-t:Deploy` lands exactly the saves file at the scratch `SdtdSavesDir`; plain builds touch nothing; StrongholdTweaks deploys as a plain modlet with no saves behaviour left |
| V5 | Solution `-t:Deploy` | Overlays participate (Hades no longer parked, StrongholdSaves present); all other mods oracle-identical; templates still inert |
| V6 | Plain-build safety | Full solution, both toolchains, no redirects: zero warnings, nothing outside `bin\`/`obj\` |
| V7 | Live Hades deploy (human) | After review: owner runs the real `-t:Deploy`; live `Worlds\` binaries untouched (hash-verified against the Phase-0 inventory), tracked files current |

## 4b. Phase 0–1 results

**Phase 0 (2026-07-30):** baselines at `48766c0` — live `Mods\Hades\` hash inventory (38 files / 433 MB: 23
repo-tracked, **15 unmanaged that must survive every deploy**), live saves inventory (280 files), and a
zero-warning deploy oracle (165 files / 27 mods + 1 saves file; Hades absent, still parked).

**Phase 1 (2026-07-30):** `build/Overlay.targets` created; `SdtdSavesDir` added to `GamePaths.props`; Hades
converted and un-parked (`DeployRoot=$(ModsDir)\Hades`; mirrors `Config`, `ModInfo.xml`, `README.md`,
`Worlds\Hades S6\WalkerSim.xml`; `Prefabs\` protected by default).

| Check | Result |
| --- | --- |
| Semantic gauntlet (V1/V2/V3) | ✅ All nine assertions in one seeded scratch deploy: unmanaged world + unmanaged prefab survive; stale file inside the `Config` mirror scope deleted (and announced); newer live edit of a tracked prefab preserved; older destination overwritten; absent files copied; both mirror flavors (dir + deep single file) deploy. The `MakeRelative` path-form risk did not materialize — no overdeletion |
| Idempotency | ✅ Second deploy: no removals, quiet no-op |
| Plain-build safety | ✅ Stages to `bin\` only; solution restore still zero NU1503 (the probe rides in `Overlay.targets` too) |
| Live installs | ✅ Untouched (all deploys redirected) |

## 4c. Phase 2 results — done 2026-07-30

`StrongholdSaves` split out (the saves file `git mv`'d so the project root *is* the saves-relative tree; README
excluded from deploy via `OverlayContentExclude`); StrongholdTweaks reduced to a four-line ordinary modlet;
`StrongMods.sln` gained the project. **The `SavesOutputPath` protocol is fully dead** — the redirect property for
saves-deploy testing is now `-p:SdtdSavesDir=`.

**Verification caught a genuine catastrophic bug before it ever ran against real data.** With a *file-only*
`MirrorOnDeploy` (StrongholdSaves' case — no directory declarations), MSBuild's batching expanded
`%(_MirrorDir.Identity)` over the **empty** vector to an empty string instead of zero iterations, turning the
deployed-mirror glob into the whole deploy root — the first V4a run **deleted the seeded runtime files** it was
sworn to protect. Hades had passed only because its declarations include directories. Fix: empty-vector guards on
all four batched includes, marked load-bearing in a comment. This is exactly the class of failure the seeded
verification battery exists for.

| Check | Result |
| --- | --- |
| V4a (after fix) | ✅ File-only mirror overlay into a seeded saves tree: config deployed, runtime KV + world save survive, exactly 3 files total; planted-stale mirror file re-asserted |
| Hades regression | ✅ Full gauntlet re-passed with the guards in place (mixed dir+file declarations) |
| V4b | ✅ StrongholdTweaks deploys as a plain modlet: 19 files, zero `Saves` paths, no saves behaviour left |
| V5 | ✅ Full solution `-t:Deploy`: non-Hades mods oracle-identical; **Hades participates again** (23 files); saves file arrives via the overlay; zero warnings |
| Live | ✅ Untouched throughout |

## 4d. Phase 3 — 2026-07-30

CLAUDE.md updated: the three-shapes taxonomy in *What this repo is*, `Overlay.targets` and `SdtdSavesDir` in the
shared-files table, the overlay import line, the *Deploying* section's overlay semantics (Hades un-parked
language), and the *Verifying* redirect note (`SdtdSavesDir` replaces the dead `SavesOutputPath`). Remaining
for close-out, owner-run:

- **V7**: the first real Hades deploy — `dotnet build Hades/Hades.csproj -t:Deploy` — then re-verify the live
  inventory: the 15 unmanaged files byte-identical to the Phase 0 baseline, tracked files current.
- The real StrongholdSaves deploy (`-t:Deploy`, default `SdtdSavesDir`) if desired — content is identical to
  what StrongholdTweaks last deployed, so this is optional hygiene.
- Close [#25](https://github.com/Strongheart-Games/StrongMods/issues/25). The property-merge flavor
  (serverconfig) stays recorded in the issue for a future pick-up — raise a fresh issue when it becomes real.

## 4e. V7 incident and fix — 2026-07-30

**The owner's first real deploy went to `C:\Hades\`.** `Hades.csproj` set `DeployRoot=$(ModsDir)\Hades` before
the single `Overlay.targets` import — but `GamePaths.props` (which defines `ModsDir`) was imported *inside* that
import, later. Property expansion is immediate, so `DeployRoot` froze as `\Hades` → drive root. Two aggravators:
the terminal logger hid the destination message (`-tl:off` or `-v:m` shows it), and **every scratch verification
had passed `-p:ModsDir=` as a global property — defined before everything — so the redirects that protected the
live install also masked the default-resolution bug.** StrongholdSaves escaped by luck (its reference froze to
empty, tripping the must-set-DeployRoot error).

Fix, all verified: overlays became a **props/targets sandwich** (`Overlay.props` imports `GamePaths.props` and
carries the defaults; body references then resolve; `Overlay.targets` closes), plus three guards in `Deploy` —
missing-props-import (readable error, mirroring `VerifyGameInstall`'s), missing `DeployRoot`, and a
**driveless-rooted-path tripwire** (`\Hades`-shaped values mean a reference expanded empty). New standing check
added to the battery: **evaluate `DeployRoot` with no overrides** for every overlay — the class of check the
redirects can't mask. Both overlays now resolve correctly by default; gauntlets re-passed; the real V7 deploy
landed correctly with all 15 unmanaged files byte-identical and the tracked set current (the mirror healed a
pre-existing ModInfo org-URL drift). The stray `C:\Hades\` (23 staged files, nothing else) awaits owner-approved
deletion.

## 5. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Overlay deletes unmanaged content via a too-broad `MirrorOnDeploy` | The one sharp edge of the design | Identities-not-globs keeps declarations legible; V1/V3 test the boundary; Hades' list reviewed by the owner |
| Duplicate `Deploy` target if an overlay accidentally imports `Deploy.targets` too | Would double-deploy or collide | `Overlay.targets` is self-contained; header comment states it must not be combined with the other entry points |
| First live Hades deploy after weeks of parking | Drift between source and live tracked files is possible | V7 is hash-checked against the Phase-0 inventory before/after; protective default means surprises are additive, not destructive |

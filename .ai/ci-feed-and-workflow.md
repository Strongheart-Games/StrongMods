# Plan: CI — private game-assembly packages, feed, and GitHub Actions build (issue #22)

- **Issue:** [#22](https://github.com/Strongheart-Games/StrongMods/issues/22) — status lives there. Successor to
  #15 / `.ai/f5b-game-assembly-packages.md`, which delivered the vendored trees, the nuspec stubs, the
  version-label scheme, and the proof that the full solution builds against either unit's tree with no install.
- **Goal:** turn a vendored tree into a private NuGet package, host it on a private GitHub Packages feed in the
  Strongheart-Games org, and stand up a GitHub Actions workflow (Linux) that restores the packages and builds the
  whole solution against **both** units — giving #21 its standing compile-against-both check and cashing the
  cross-platform claims f5b made from a Windows machine.
- **Standing constraint (f5b §2):** the packages contain licensed game files and the repo is public. Everything
  here is designed so that no path — feed visibility, Actions artifacts, fork PRs, logs — can ever expose them.

## 1. The leak model comes first

Every design choice below is downstream of one question: *how could a licensed assembly reach the public?* The
channels, and how each is closed:

| Channel | How it would leak | How it is closed |
| --- | --- | --- |
| Package visibility | A GitHub package **linked to a repository inherits that repo's visibility** — linking to this public repo would publish the feed's contents | The nuspec never carries a `<repository>` element or `RepositoryUrl`; `pack.py` refuses to pack if one appears. Packages pushed without a repo link are private by default. Post-first-push human check confirms visibility, and the org setting that blocks public package creation is enabled if available |
| Actions artifacts | `upload-artifact` of a tree/nupkg on a public repo is a public download | The workflow never uploads artifacts — a standing rule stated in the workflow's header comment |
| Fork PRs | A fork PR can *edit the workflow itself*; if the run can reach the feed, the edited workflow can exfiltrate the nupkg (e.g. curl it out) | The feed token is a **repo secret, not `GITHUB_TOKEN`** — GitHub withholds secrets from fork-PR runs, so a fork run cannot authenticate to the feed no matter what the workflow says. An `if:` guard skips fork PRs cleanly, but the *enforcement* is the missing secret, not the guard |
| Logs | Log output on a public repo is public | Logs only ever contain file names, hashes, and build output — never file contents. Nothing to close, noted for completeness |
| Git | Committing a tree or nupkg | Already closed: `/vendor/` and `*.nupkg` are gitignored (f5b) |

The `GITHUB_TOKEN` alternative (granting this repo Actions access to the packages) is rejected specifically
because of the fork-PR row: fork runs get a read-only `GITHUB_TOKEN`, and a read is all a leak needs.

## 2. Packaging: `build/tools/pack.py`

Python beside `vendor.py`/`compare-eval.py`, cross-platform for the same reasons. Packing stays a **manual,
human-triggered act** on a machine with a licensed install (issue pt. 4); CI only ever consumes.

`pack.py --unit <unit> --label <label>` does, in order:

1. **Validate — manifest.json is the arbiter** (issue pt. 1's label↔buildid consistency check):
   - The tree exists for that unit+label; `manifest.json` `unit` and `label` fields match the directory coordinates.
   - The nuspec stub's `<version>` equals the four-part mapping of the label (`V3.1.0-b13` → `3.1.0.13` — the
     mapping `vendor.py` already implements; `pack.py` recomputes independently and compares).
   - Steam `buildid` is present in the manifest (warn-only if null — a non-Steam source install).
   - **Tree fidelity:** recompute per-file SHA-256 against the manifest — a tree that drifted from what
     `vendor.py` wrote (the f5b stray-deploy footgun, manual tampering, corruption) must not be packed.
   - The nuspec contains no `<repository>` element (leak-model row 1) — refuse loudly if it does.
2. **Write the definitive nuspec** to scratch: the stub's metadata plus an explicit `<files>` section derived
   from the manifest's file list **plus `manifest.json` itself** — the package contains exactly the manifested
   tree, nothing globbed. Shipping the manifest inside the package is what lets CI re-verify integrity and
   provenance after restore (§4). The stub in the tree stays untouched (it remains `vendor.py`'s output; two
   hand-maintained nuspecs would drift).
3. **Pack** via `dotnet pack` on a generated throwaway stub project with `-p:NuspecFile=... -p:NuspecBasePath=<tree>`
   (cross-platform, no nuget.exe/mono dependency; NU5100-class "assembly outside lib/" warnings suppressed —
   these are content-shaped packages by design). Fallback if the stub-project pattern fights back: hand-build the
   OPC zip in Python. Output: `vendor/packages/<id>.<version>.nupkg` (gitignored via `/vendor/`).
4. `pack.py --verify-tree <path>` exposes step 1's hash verification standalone — the same code CI runs
   post-restore (§4), so pack-time and restore-time checks cannot drift.

Package contents (at package root, mirroring the tree exactly — this is what makes `-p:SdtdDir=` point-and-go):

```
7DaysToDie_Data/Managed/*.dll        (or 7DaysToDieServer_Data/... for the server unit)
Mods/0_TFP_Harmony/0Harmony.dll
manifest.json
```

Versioning is per-unit and label-derived, as f5b §5 recorded: `7DtD.Assemblies.Game` and
`7DtD.Assemblies.DedicatedServer` rev independently (a server-only hotfix bumps only the server package). The
feed rejects a re-push of an existing version, which is the final backstop against "same label, different bits" —
regenerate under the corrected label instead.

## 3. Feed: private GitHub Packages NuGet in the org

- Registry: `https://nuget.pkg.github.com/Strongheart-Games/index.json`.
- Packages are **never linked to any repository** (leak model §1). Visibility stays private; access is granted
  per-account.
- Two tokens, two lives (accounts decided 2026-07-31):
  - **Write** (`write:packages` + `delete:packages`): the **human's** account, used only inside the publish
    routine (§6) — the delete scope is what lets `release.py` apply retention. Never stored in the repo or CI.
  - **Read** (`read:packages`): the **bot's** account, stored as the repo secret `PACKAGES_READ_TOKEN`, so CI's
    feed access is auditable separately from the human. Note GitHub Packages registries have historically
    required **classic** PATs; whether a fine-grained PAT works for `nuget.pkg.github.com` is verified during
    setup (§7 V4) — use classic if not.
- Quota context: private-package storage on a free org is limited (~500 MB), and a version pair is ~2 × 47 MB
  before zip compression (likely ~15–25 MB each compressed). Downloads from Actions runners are free.
- **Retention policy (decided 2026-07-31): per unit, keep only the latest build per game version.** The retention
  key is `major.minor.patch`; among package versions sharing a key, only the highest build survives — given
  `3.0.0.259`, `3.0.1.4`, `3.0.1.7`, keep the first and third, delete the second. Applied automatically by
  `release.py` after each push (GitHub Packages API, the delete scope above). Deletion is non-destructive here —
  the vendored trees and a licensed install can always regenerate byte-identical packages; old feed versions are
  convenience only.
- **Where money would help, if ever needed:** the org is the right entity (it owns repo, feed, and Actions), and
  today nothing requires payment — Actions on a public repo is free including the scheduled check (§6b), and the
  retention policy keeps storage well under the free 500 MB. If storage ever pressed: metered overage on the Free
  plan (enable a spending limit; billed per GB) or GitHub Team (~$4/user/mo, 2 GB included). A personal paid plan
  changes nothing — no org resource bills to it.

## 4. CI consumption: pin in one place, restore, verify, build

Two small committed files under `build/ci/` (a new directory — not imported by any project, not in the .sln):

- **`build/ci/GameAssemblies.csproj`** — a minimal SDK project whose only job is to be restorable. It carries the
  two `<PackageReference>`s with **pinned versions — the single place the consumed game version lives**. Bumping
  the game version in CI is a one-line-per-unit PR to this file, and the PR's green build is the proof the new
  packages round-trip. Independent versions per unit, matching §2.
- **`build/ci/nuget.config`** — the GitHub source plus nuget.org (BloodRain's Cronos still restores), with
  **package source mapping**: `7DtD.*` → GitHub feed only, everything else → nuget.org only. The mapping is both
  hygiene (no dependency-confusion surface) and documentation. Credentials reference the environment variable
  (`%PACKAGES_READ_TOKEN%`), so no token material ever appears in the file. Scoped under `build/ci/` and used via
  `--configfile`, so local development never sees the feed or an auth prompt.

The workflow then:

1. `dotnet restore build/ci/GameAssemblies.csproj --packages .scratch/game-packages --configfile build/ci/nuget.config`
   — an isolated packages folder, so each unit's tree lands at a discoverable
   `.scratch/game-packages/<id, lowercased>/<version>/`.
2. Globs that path to find the single version directory — the workflow never duplicates the version number; the
   csproj stays the sole source of truth.
3. `pack.py --verify-tree` against the extracted tree: recomputes every SHA-256 against the packaged
   `manifest.json`. Restore-time integrity equals pack-time integrity by construction (§2 pt. 4).
4. Builds (§5).

`GamePaths.props` needs **no changes**: the extracted package root has exactly a vendored tree's shape, the
layout-detection conditional picks the right `*_Data` directory, and `VerifyGameInstall` guards it as usual.

## 5. Workflow: `.github/workflows/build.yml`

- **Triggers:** `push` to `main`, `pull_request`, `workflow_dispatch`. Fork PRs skip via the cosmetic `if:` guard
  (leak model §1). Concurrency group per ref, cancel-in-progress.
- **Permissions:** `contents: read` only. No packages permission — the feed is reached via the secret, and the
  workflow should hold no grant a fork could inherit.
- **Runner:** `ubuntu-latest`, .NET SDK pinned via `setup-dotnet` (no `global.json` — that would constrain local
  dev, which this repo deliberately doesn't).
- **Matrix:** `unit: [game, dedicated-server]` — two jobs, each restoring both packages (one restore project) and
  building the full solution against its unit's tree:

  ```
  dotnet build StrongMods.sln -c Debug -p:SdtdDir=<extracted tree> -p:ModsDir=$RUNNER_TEMP/mods -p:SdtdSavesDir=$RUNNER_TEMP/saves
  ```

  This is the issue's command verbatim, plus the saves redirect for the StrongholdSaves overlay (`$(AppData)` is
  empty on Linux). Plain build only — **never `-t:Deploy`** (CLAUDE.md forbids combining it with `-p:SdtdDir=`,
  and deploy semantics on Linux are out of scope). Since #13, plain builds stage to `bin\` only; the redirects
  are belt-and-braces, not load-bearing.
- The dedicated-server leg **is** #21's standing compile-against-both check — that issue's "how do both-unit
  builds run" question gets its first concrete answer here (in CI always; local dual builds remain #21's scope).
- A commented placeholder marks where #14's `dotnet test` step slots in.
- Debug configuration only for now (the issue's ask). Adding `-c Release` legs later is a two-line matrix change.
- The header comment carries the standing safety rules: no artifacts ever, no `-t:Deploy`, why fork PRs skip.

**The first green run is itself a deliverable:** it is the first execution of this build system on Linux, cashing
f5b §8's "asserted for Linux, developed on Windows" risk. A red first run is *discovery* (e.g. a case-sensitivity
or path-separator seam MSBuild didn't absorb) and gets fixed under this issue.

## 6. The publish routine: one command, guardrails included (`build/tools/release.py`)

Revised 2026-07-31 at the owner's request: the human's part should be as automated as possible — one command,
with the tool doing the checking rather than the human doing the noticing. Publishing remains human-*triggered*
(the licensed-install requirement stands), but no longer human-*orchestrated*:

```
python build/tools/release.py
```

What it does, in order — the guardrails are steps 1–3:

1. **Is there anything to publish?** Queries Steam's branch heads for both apps via **anonymous** SteamCMD
   `app_info_print` (build metadata is public; no login involved) through the shared `steam_check.py` (§6b), and
   compares three coordinates per unit: the watched branch's current buildid, the local install's buildid (its
   appmanifest), and the last published buildid (from `build/ci/game-versions.json`, §6c). Nothing new → say so
   and exit 0. Only stale units proceed — units on different cadences publish independently.
2. **Is the local install current?** If Steam is ahead of the install: with `--steamcmd`, update the install in
   place (`+force_install_dir` + `app_update`; see the caveats below); without it, print exactly what to update
   and stop. Never vendor a stale install into a new label.
3. **One prompt: the label.** The in-game version string is the one thing not machine-derivable (f5b §5).
   Validated against the label grammar, must move forward from the last published label, and must move *iff* the
   buildid moved — a label↔buildid regression refuses loudly.
4. Runs `vendor.py` then `pack.py` for each stale unit (all of §2's validation applies).
5. Pushes with the write PAT (from an environment variable, else a prompt — never stored).
6. Applies the retention policy (§3) via the Packages API.
7. Updates `build/ci/game-versions.json` and the pins in `build/ci/GameAssemblies.csproj`; with `--pr`, also
   branches, commits, and opens the version-bump PR via `gh` — whose green CI is the round-trip proof. Without
   `--pr` it leaves the edits for a manual commit.

`vendor.py` and `pack.py` stay fully usable standalone — the first-ever publish (§7 phase 4) runs them by hand to
prove each piece before the orchestrator exists.

**SteamCMD caveats (recorded so they're not rediscovered):** the dedicated server (294420) installs/updates with
`+login anonymous`. The base game (251570) needs the human's own SteamCMD login — and SteamCMD's credential cache
is separate from the Steam client's, so the first run is interactive (password + Steam Guard), cached on that
machine afterward. Updating a *client-managed* library folder with SteamCMD works but can race the running Steam
client — close Steam first, or configure `release.py` with a separate SteamCMD-owned install root (one-time
~15 GB download for the base game; deltas after). Default behavior is the existing installs with the
close-Steam warning.

### 6b. Update notification: `.github/workflows/check-game-version.yml`

So the human never has to *notice* a new game version (owner request, 2026-07-31): a small scheduled workflow
(daily cron + `workflow_dispatch`) on the public repo — free — that runs the same anonymous SteamCMD query as
`release.py` step 1 and compares against `build/ci/game-versions.json`. On a should-notify decision it opens a
tracking issue @mentioning the owner ("New 7DtD build on Steam: game 24401234 (published: 24392370)"), updating
the existing open one rather than duplicating, and auto-closing it once the published state catches up (i.e.
after the publish PR merges). Uses only `GITHUB_TOKEN` with `issues: write`; touches no secrets and no licensed
content — buildids and branch names are public Steam metadata. Notification reaches the human through normal
GitHub issue notifications.

**Noise model (owner concern, 2026-07-31): notify on *releases*, not every build.** SteamDB shows far more
builds than players ever receive — every depot push mints a buildid, but a build only reaches players when it is
promoted to a **branch head** (`public`, `latest_experimental`, version-pinned branches like `3.0.1`).
`app_info` exposes only branch heads, so polling it inherently filters the push firehose; what remains to design
is *which branches* count as "released" (`public` certainly — it's what the publish routine vendors; whether
`latest_experimental` or newly-appearing version branches warrant an informational mention is decided from data)
and the promotion edge cases: buildid moving *backward* (rollback), re-promotion of the same buildid, the game
and dedicated server promoting at different times (notify per unit, update the same issue as the second unit
lands), and branches appearing/disappearing. These are empirical questions, so:

- **The decision lives in one place:** `build/tools/steam_check.py` — the anonymous branch-head query plus a
  pure `should_notify(published_state, app_info) -> decisions` function. `release.py` imports it and the
  workflow runs it, so the guardrail and the notifier cannot drift. The pure function is testable offline
  against captured fixtures.
- **Exploration comes first (§7 phase 1):** capture real `app_info` for both apps, study the branch structure
  and SteamDB's promotion history for recent releases, and encode the findings as fixtures + tests for
  `should_notify` — before any workflow exists. Findings land back in this section; edge cases nobody
  anticipated become fixtures.
- **Shadow mode before notifications:** the workflow ships with notification off — decisions go to the job
  summary only. After a soak (through at least one real release, ideally) shows it fires only when a release a
  player can install actually happened, the issue-filing switch flips on.

### 6c. `build/ci/game-versions.json` — the published-state file

Small committed file, one entry per unit: `label`, `buildid`, package `version`. It is what §6 step 1 and §6b
compare against, written only by `release.py` (step 7). Buildids and labels are public metadata — safe in the
public repo. The package-version *pins* stay in `GameAssemblies.csproj` (NuGet needs them there); `release.py`
writes both files in the same step, so they cannot drift.

## 7. Plan of attack

Phased to the workstyle constraints; each phase is one reviewable change with its own pause.

| # | Work | Touches |
| --- | --- | --- |
| 1 | **Exploration (§6b):** capture real `app_info` for both apps (needs SteamCMD on this machine — human-approved install — or human-captured output), study branch structure + SteamDB promotion history for recent releases; write `steam_check.py`'s `should_notify` core with fixtures from the real data; findings back into §6b, plan adjusted if they demand it | `build/tools/steam_check.py` (~100 lines incl. fixtures/tests); `.scratch/` captures |
| 2 | `build/tools/pack.py` (validate → nuspec → pack → `--verify-tree`); pack both units locally | 1 new file (~150 lines) |
| 3 | Local round-trip verification (V1–V3 below) — no feed needed: restore from `vendor/packages` as a local file source | `.scratch/` only |
| 4 | `build/ci/GameAssemblies.csproj` + `build/ci/nuget.config` + `build/ci/game-versions.json` (hand-seeded with the current published state) + `.github/workflows/build.yml` | 4 new files (~100 lines) |
| 5 | **Human setup + first publish** (checklist in §7b): tokens, secret, push *by hand* (proves each piece before the orchestrator wraps them), visibility check | Feed + repo settings |
| 6 | First CI runs on a PR branch (V5); fix what Linux discovers | Whatever V5 surfaces |
| 7 | `build/tools/release.py` (§6: guardrails via `steam_check`, orchestration, retention, state-file writes; `--steamcmd`, `--pr`) | 1 new file (~200 lines — human validation before implementing) |
| 8 | `.github/workflows/check-game-version.yml` (§6b) wrapping `steam_check.py`, **shadow mode**; V8/V9; notifications flip on only after the soak | 1 new file (~60 lines) |
| 9 | Docs: CLAUDE.md *Building without the game* gains the package/CI paragraphs; results into this doc; comment + close #22; note on #21 (standing check now exists) and #20 (README CI mention) | Docs |

### 7b. Phase-4 human checklist (nothing here is agent-executable)

1. Create the write PAT on the **human's** account (`write:packages` + `delete:packages`, classic if
   fine-grained fails).
2. First `dotnet nuget push` of both packages (by hand this once; `release.py` owns it thereafter).
3. **Confirm both packages show Private** in org → Packages; confirm no repository link.
4. Enable the org setting blocking public package creation, if the plan offers it.
5. Create the read token on the **bot's** account (`read:packages`); add repo secret `PACKAGES_READ_TOKEN`.
6. Grant the bot account read access to both packages if not implicit.
7. (Optional, once, for `release.py --steamcmd`:) install SteamCMD and do its first interactive `+login` so the
   credential cache exists — §6 caveats.

## 8. Verification

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Package contents | Unzip both nupkgs: file set == manifest file set + `manifest.json` + nuspec/OPC ceremony; hashes of packed DLLs match the manifest |
| V2 | **Local round-trip, no feed** | Restore `GameAssemblies.csproj` from `vendor/packages` as a file source into `.scratch/`; `--verify-tree` passes on both extracted trees; full solution builds against each (`-p:SdtdDir=<extracted>`), exit 0, warnings at baseline — proves the whole consumption path before any credential exists |
| V3 | Tamper detection | Flip one byte in an extracted DLL → `--verify-tree` fails loudly; mismatched label/nuspec/repository-element each refuse to pack |
| V4 | Feed round-trip | After phase 4: restore from the real feed with the read token succeeds; visibility confirmed Private; PAT flavor recorded |
| V5 | **The workflow itself, on Linux** | Both matrix legs green on a PR branch: restore → verify → build × 2 units. First-ever Linux execution — failures are discovery, fixed under this issue |
| V6 | Fork-PR posture | Guard skips fork PRs; reasoning documented (secret withheld regardless of workflow edits) — behavioral simulation not required |
| V7 | Live installs untouched | Standard check; CI is remote and local round-trips are fully redirected |
| V8 | `release.py` guardrails | Nothing-new state exits 0 without prompting; label regression / label-moved-without-buildid refused; retention selection verified against a synthetic version list (`3.0.0.259`, `3.0.1.4`, `3.0.1.7` → deletes exactly `3.0.1.4`) before any real API delete; `--steamcmd` path exercised once end-to-end |
| V9 | Check workflow | `should_notify` fixture tests pass, including the §6b edge cases (rollback, re-promotion, staggered units, unwatched-branch churn → silent). With `game-versions.json` temporarily behind Steam: the tracking issue appears with correct buildids; a re-run updates rather than duplicates; restoring the file auto-closes it. **Soak:** shadow mode runs quiet day-to-day and fires only for a genuine release before notifications switch on |

## 9. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Any leak channel | The standing constraint | §1 closes each named channel; the design never relies on a single guard |
| Linux run fails | Real possibility — first execution of the build off Windows | Expected-and-welcome discovery (V5); MSBuild normalizes `\` separators and the trees are case-exact by design, so residual risk is small |
| GitHub Packages rejects the content-shaped nupkg or the PAT flavor | Server-side validation is the one thing not testable locally | V2 proves everything client-side first; PAT flavor resolved at V4 with classic as the known-good fallback |
| Storage quota exhaustion | ~30–50 MB/version-pair compressed vs ~500 MB free tier | Keep current + previous, delete older (§3); packages are always regenerable from a licensed install |
| `dotnet pack` stub-project friction | The NuspecFile pattern is known-janky | Fallback: hand-built OPC zip in Python (pack.py owns the choice; interface unchanged) |
| Version pinned in CI drifts from feed reality | Restore simply fails red | That *is* the check; bump PR flow (§6) makes green the proof |
| SteamCMD friction (interactive first login, client-managed-dir races, VDF output parsing) | Automation convenience, not correctness — vendoring never depends on SteamCMD | Caveats recorded in §6; `--steamcmd` is opt-in; the buildid *query* is anonymous and needs no login; VDF parsing is a two-field regex extraction, and a parse failure fails loudly, never silently reports "up to date" |
| Retention deletes the wrong version | Would only cost regeneration time, not data | Selection logic verified against synthetic lists first (V8); deletes are per-unit, per-key, never bulk |
| Notification too noisy (fires on non-released builds) or too quiet (misses a release path) | The owner's explicit concern; branch-head polling filters most noise structurally, but promotion edge cases are empirical | Exploration phase 1 encodes real data as fixtures before design freezes; shadow-mode soak (V9) proves quiet-in-practice before any issue is ever filed |

## 10. Decisions (owner, 2026-07-31)

The original open questions, resolved — plus the automation scope they added:

1. **Token accounts:** bot for read, human for write (folded into §3/§7b).
2. **Retention:** keep only the latest build per `major.minor.patch`, per unit (folded into §3).
3. **Matrix:** stay Debug × 2 units.
4. **Automation mandate:** the human's part must be as automated as possible — hence `release.py` as the single
   guarded command (§6), SteamCMD both for the new-version guardrail and optional install updates, and the
   scheduled Steam-buildid check workflow for notification (§6b).
5. **Notify on releases only, not every Steam build:** the notifier watches branch heads, its decision logic is
   designed empirically first (exploration is now phase 1) and shared with `release.py` via `steam_check.py`,
   and it soaks in shadow mode before it is allowed to file an issue (§6b).

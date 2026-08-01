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
| Package visibility | A GitHub package **linked to a repository inherits that repo's visibility** — linking to this public repo would publish the feed's contents | The nuspec never carries a `<repository>` element or `RepositoryUrl`; `pack.cs` refuses to pack if one appears. Packages pushed without a repo link are private by default. Post-first-push human check confirms visibility, and the org setting that blocks public package creation is enabled if available |
| Actions artifacts | `upload-artifact` of a tree/nupkg on a public repo is a public download | The workflow never uploads artifacts — a standing rule stated in the workflow's header comment |
| Fork PRs | A fork PR can *edit the workflow itself*; if the run can reach the feed, the edited workflow can exfiltrate the nupkg (e.g. curl it out) | The feed token is a **repo secret, not `GITHUB_TOKEN`** — GitHub withholds secrets from fork-PR runs, so a fork run cannot authenticate to the feed no matter what the workflow says. An `if:` guard skips fork PRs cleanly, but the *enforcement* is the missing secret, not the guard |
| Logs | Log output on a public repo is public | Logs only ever contain file names, hashes, and build output — never file contents. Nothing to close, noted for completeness |
| Git | Committing a tree or nupkg | Already closed: `/vendor/` and `*.nupkg` are gitignored (f5b) |

The `GITHUB_TOKEN` alternative (granting this repo Actions access to the packages) is rejected specifically
because of the fork-PR row: fork runs get a read-only `GITHUB_TOKEN`, and a read is all a leak needs.

## 2. Packaging: `build/tools/pack.cs`

A C# file-based app (`dotnet run pack.cs`), per the #36 language decision (`.ai/scripting-language-research.md`,
2026-07-31): new tools are born as C#; the SDK that runs them is already a hard prerequisite everywhere the repo
builds. Packing stays a **manual, human-triggered act** on a machine with a licensed install (issue pt. 4); CI
only ever consumes.

`pack.cs --unit <unit> --label <label>` does, in order:

1. **Validate — manifest.json is the arbiter** (issue pt. 1's label↔buildid consistency check):
   - The tree exists for that unit+label; `manifest.json` `unit` and `label` fields match the directory coordinates.
   - The nuspec stub's `<version>` equals the four-part mapping of the label (`V3.1.0-b13` → `3.1.0.13` — the
     mapping `vendor.py` already implements; `pack.cs` recomputes independently and compares).
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
   OPC zip in-tool (`System.IO.Compression`). Output: `vendor/packages/<id>.<version>.nupkg` (gitignored via
   `/vendor/`).
4. `pack.cs --verify-tree <path>` exposes step 1's hash verification standalone — the same code CI runs
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
    routine (§6) — the delete scope is what lets `release.cs` apply retention. Never stored in the repo or CI.
  - **Read** (`read:packages`): the **bot's** account, stored as the repo secret `PACKAGES_READ_TOKEN`, so CI's
    feed access is auditable separately from the human. Note GitHub Packages registries have historically
    required **classic** PATs; whether a fine-grained PAT works for `nuget.pkg.github.com` is verified during
    setup (§7 V4) — use classic if not.
- Quota context: private-package storage on a free org is limited (~500 MB), and a version pair is ~2 × 47 MB
  before zip compression (likely ~15–25 MB each compressed). Downloads from Actions runners are free.
- **Retention policy (decided 2026-07-31): per unit, keep only the latest build per game version.** The retention
  key is `major.minor.patch`; among package versions sharing a key, only the highest build survives — given
  `3.0.0.259`, `3.0.1.4`, `3.0.1.7`, keep the first and third, delete the second. Applied automatically by
  `release.cs` after each push (GitHub Packages API, the delete scope above). Deletion is non-destructive here —
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
3. `pack.cs --verify-tree` against the extracted tree: recomputes every SHA-256 against the packaged
   `manifest.json`. Restore-time integrity equals pack-time integrity by construction (§2 pt. 4).
4. Builds (§5).

`GamePaths.props` needs **no changes**: the extracted package root has exactly a vendored tree's shape, the
layout-detection conditional picks the right `*_Data` directory, and `VerifyGameInstall` guards it as usual.

## 5. Workflow: `.github/workflows/build.yml`

- **Triggers:** `push` to `main`, `pull_request`, `workflow_dispatch`. Fork PRs skip via the cosmetic `if:` guard
  (leak model §1). Concurrency group per ref, cancel-in-progress.
- **Permissions:** `contents: read` only. No packages permission — the feed is reached via the secret, and the
  workflow should hold no grant a fork could inherit.
- **Runner:** `ubuntu-latest`, .NET SDK pinned via `setup-dotnet` to **10.x** — the C# file-based tools (§2,
  §6b) require SDK 10, which ubuntu-latest preinstalls, so it's a pin, not an install; each run pays each tool's
  ~7 s first-run compile once, which is noise. (No `global.json` — that would constrain local dev, which this
  repo deliberately doesn't.)
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

## 6. The publish routine: one command, guardrails included (`build/tools/release.cs`)

Revised 2026-07-31 at the owner's request: the human's part should be as automated as possible — one command,
with the tool doing the checking rather than the human doing the noticing. Publishing remains human-*triggered*
(the licensed-install requirement stands), but no longer human-*orchestrated*:

```
python build/tools/release.cs
```

What it does, in order — the guardrails are steps 1–3:

1. **Is there anything to publish?** Queries Steam's branch heads for both apps via **anonymous** SteamCMD
   `app_info_print` (build metadata is public; no login involved) through the shared `steam_check.cs` (§6b), and
   compares three coordinates per unit: the watched branch's current buildid, the local install's buildid (its
   appmanifest), and the last published buildid (from `build/ci/game-versions.json`, §6c). Nothing new → say so
   and exit 0. Only stale units proceed — units on different cadences publish independently.
2. **Is the local install current?** If Steam is ahead of the install: with `--steamcmd`, update the install in
   place (`+force_install_dir` + `app_update`; see the caveats below); without it, print exactly what to update
   and stop. Never vendor a stale install into a new label.
3. **One prompt: the label.** The in-game version string is the one thing not machine-derivable (f5b §5).
   Validated against the label grammar, must move forward from the last published label, and must move *iff* the
   buildid moved — a label↔buildid regression refuses loudly.
4. Runs `vendor` then `pack` for each stale unit (all of §2's validation applies). Vendoring ports to
   `vendor.cs` in the same phase (decided 2026-07-31): `release.cs` shelling out to Python would keep the
   retired language in the flagship routine's dependency set; dependencies #22 needs move with it, the rest of
   the Python estate is the parked #36 thread's cleanup.
5. Pushes with the write PAT (from an environment variable, else a prompt — never stored).
6. Applies the retention policy (§3) via the Packages API.
7. Updates `build/ci/game-versions.json` and the pins in `build/ci/GameAssemblies.csproj`; with `--commit`,
   also commits both files and pushes — the green Build run on main is the round-trip proof. Without `--commit`
   it leaves the edits for a manual commit. (Originally planned as `--pr`; revised 2026-07-31 — the owner
   doesn't use PRs. If an agent-sandbox-PR workflow materializes later, a `--pr` mode is a small addition.)

The vendor and pack tools stay fully usable standalone — the first-ever publish (§7 phase 4) runs them by hand
to prove each piece before the orchestrator exists.

**SteamCMD caveats (recorded so they're not rediscovered):** the dedicated server (294420) installs/updates with
`+login anonymous`. The base game (251570) needs the human's own SteamCMD login — and SteamCMD's credential cache
is separate from the Steam client's, so the first run is interactive (password + Steam Guard), cached on that
machine afterward. Updating a *client-managed* library folder with SteamCMD works but can race the running Steam
client — close Steam first, or configure `release.cs` with a separate SteamCMD-owned install root (one-time
~15 GB download for the base game; deltas after). Default behavior is the existing installs with the
close-Steam warning.

### 6b. Update notification: `.github/workflows/check-for-new-game-version.yml`

So the human never has to *notice* a new game version (owner request, 2026-07-31): a small scheduled workflow
(daily cron + `workflow_dispatch`) on the public repo — free — that runs the same anonymous SteamCMD query as
`release.cs` step 1 and compares against `build/ci/game-versions.json`. On a should-notify decision it opens a
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

- **The decision lives in one place:** `build/tools/steam_check.cs` — the anonymous branch-head query plus a
  pure `ShouldNotify(published_state, app_info) -> decisions` core. The workflow runs it directly and
  `release.cs` invokes it as a subprocess over its `--json` + exit-code contract (process composition chosen
  over `#:include`, 2026-07-31 — smaller independently-verifiable components, matching the repo's own
  many-small-mods philosophy), so the guardrail and the notifier cannot drift. The pure core is testable
  offline against captured fixtures.
- **Exploration comes first (§7 phase 1):** capture real `app_info` for both apps, study the branch structure
  and SteamDB's promotion history for recent releases, and encode the findings as fixtures + tests for
  `should_notify` — before any workflow exists. Findings land back in this section; edge cases nobody
  anticipated become fixtures.
- **Shadow mode before notifications:** the workflow ships with notification off — decisions go to the job
  summary only. After a soak (through at least one real release, ideally) shows it fires only when a release a
  player can install actually happened, the issue-filing switch flips on.

**Phase-1 findings (2026-07-31, from a real anonymous `app_info` capture of both apps):**

- **Branch structure:** one description-less `public` branch per unit; version-pinned `v<x.y.z>` branches
  ("Version 3.1.0 Stable"); historical `alpha*` branches; a `privatebranches: 1` flag (password-protected
  branches exist but are invisible — correctly out of scope). `latest_experimental` **does not exist while no
  experimental is running** — the notifier must treat its absence as normal, presence as informational.
- **The live state was itself the hotfix edge case:** both units' `public` *and* `v3.1.0` branches had been
  re-pointed to new builds (game 24392370 → 24436778, server 24392395 → 24436799) with the branch description
  unchanged — a same-version hotfix; the in-game b# is the only place the new label component exists. The local
  installs had auto-updated, so Steam == install > published: exactly the state `release.cs` step 1 exists to
  catch, observed in the wild before any code shipped.
- **Data quirks encoded as fixtures:** double-space in one branch description; oldest branches lack
  `timeupdated`; steamcmd interleaves unquoted chatter lines with the VDF; steamcmd can serve a stale/partial
  cache on a first query (one retry is the cure); Windows consoles are cp1252 (tool output is plain ASCII).
- **`steam_check.py` shipped** with the pure `should_notify` core, a minimal VDF parser, `--selftest` (11
  offline checks: hotfix re-point, up-to-date, rollback — which correctly hints the version rolled back *to* —
  new-version branch, missing watched branch → error, missing published state → error, experimental
  informational-only, parser quirks), `--raw` (decide from a saved capture), and `--live` (anonymous steamcmd).
  Exit contract: 0 up-to-date, 1 notify, 2 error — a broken query can never impersonate "up to date".
  Subsequently ported 1:1 to `steam_check.cs` under the #36 language decision — text and `--json` outputs
  byte-identical to the Python original, same exit contract, same 11 checks, plus a hardened top-level catch so
  malformed data also exits 2 — and the `.py` was retired.
- **SteamDB browsing was dropped:** loading steamdb.info crashes the Claude Desktop in-app browser (reported
  upstream), and branch heads turned out to carry everything the decision needs — promotion history was
  context, not input. No design change required; §6b's model survived contact with real data intact.

### 6c. `build/ci/game-versions.json` — the published-state file

Small committed file, one entry per unit: `label`, `buildid`, package `version`. It is what §6 step 1 and §6b
compare against, written only by `release.cs` (step 7). Buildids and labels are public metadata — safe in the
public repo. The package-version *pins* stay in `GameAssemblies.csproj` (NuGet needs them there); `release.cs`
writes both files in the same step, so they cannot drift.

## 7. Plan of attack

Phased to the workstyle constraints; each phase is one reviewable change with its own pause.

| # | Work | Touches |
| --- | --- | --- |
| 1 | **Exploration (§6b), done:** capture real `app_info` for both apps, study branch structure; `should_notify` core with fixtures from the real data; findings in §6b. Delivered as `steam_check.py`, then ported to `steam_check.cs` under the #36 language decision (byte-identical outputs verified; the .py is retired) | `build/tools/steam_check.cs`; `.scratch/` captures |
| 2 | `build/tools/pack.cs` (validate → nuspec → pack → `--verify-tree`); pack both units locally | 1 new file (~150 lines of Python-equivalent; C# runs longer) |
| 3 | Local round-trip verification (V1–V3 below) — no feed needed: restore from `vendor/packages` as a local file source | `.scratch/` only |
| 4 | `build/ci/GameAssemblies.csproj` + `build/ci/nuget.config` + `build/ci/game-versions.json` (hand-seeded with the current published state) + `.github/workflows/build.yml` | 4 new files (~100 lines) |
| 5 | **Human setup + first publish** (checklist in §7b): tokens, secret, push *by hand* (proves each piece before the orchestrator wraps them), visibility check | Feed + repo settings |
| 6 | First CI runs on a PR branch (V5); fix what Linux discovers | Whatever V5 surfaces |
| 7 | `build/tools/release.cs` (§6: guardrails via the `steam_check.cs` subprocess contract, orchestration, retention, state-file writes; `--steamcmd`, `--pr`) **+ port `vendor.py` → `vendor.cs`** (release must not depend on Python; §6 step 4) | 2 files (~400 lines total — human validation before implementing) |
| 8 | `.github/workflows/check-for-new-game-version.yml` (§6b) wrapping `steam_check.cs`, **shadow mode**; V8/V9; notifications flip on only after the soak | 1 new file (~60 lines) |
| 9 | Docs: CLAUDE.md *Building without the game* gains the package/CI paragraphs; results into this doc; comment + close #22; note on #21 (standing check now exists) and #20 (README CI mention) | Docs |

### 7b. Phase-4 human checklist (nothing here is agent-executable)

1. Create the write PAT on the **human's** account (`write:packages` + `delete:packages`, classic if
   fine-grained fails).
2. First `dotnet nuget push` of both packages (by hand this once; `release.cs` owns it thereafter).
3. **Confirm both packages show Private** in org → Packages; confirm no repository link.
4. Enable the org setting blocking public package creation, if the plan offers it.
5. Create the read token on the **bot's** account (`read:packages`); add repo secret `PACKAGES_READ_TOKEN`.
6. Grant the bot account read access to both packages if not implicit.
7. (Optional, once, for `release.cs --steamcmd`:) install SteamCMD and do its first interactive `+login` so the
   credential cache exists — §6 caveats.

### 7c. Results log

**Phase 1 — done 2026-07-31.** Findings recorded in §6b (branch structure, the live hotfix edge case, data
quirks); `steam_check.cs` delivered with 11 offline checks after the mid-phase #36 language pivot (Python
original ported 1:1, equivalence proven byte-for-byte, then retired).

**Phase 2 — done 2026-07-31.** `build/tools/pack.cs` delivered; all §2 behaviors verified:

- Selftest: 11 checks green (four-part mapping incl. missing-patch fill, bad-label refusal, the `<repository>`
  leak-guard refusal, final-nuspec file list exactly manifest + `manifest.json`, stub/label version-disagreement
  refusal, strict/lenient/tampered/unmanifested-file verification against a scratch fixture tree).
- Both b13 units packed for real: 155 files / 47.1 MB tree → **17.3 MB nupkg** each (§3's 15–25 MB estimate
  holds), buildids 24392370 / 24392395 carried in the packed manifests.
- Package contents proven exact: 160 entries = 155 manifested DLLs + `manifest.json` + nuspec + 3 OPC ceremony
  files; nothing else (no stub-project pollution), every manifested entry re-hashed from the archive.
- The `dotnet pack -p:NuspecFile` stub-project pattern worked as designed — the §9 "known-janky" risk did not
  materialize; the `System.IO.Compression` fallback stays unexercised.
- `--verify-tree`: exit 0 on a pristine tree, exit 2 with a readable error on failure.
- Standing note for phase 5: the packed trees are **b13, already behind Steam's public branch** (§6b findings) —
  fine as pipeline proof; re-vendor at the current version before the first real publish.

**Phase 3 — done 2026-07-31.** The feed-less round trip (V1 was already proven inside phase 2's post-pack
nupkg re-hash; V2 and V3 here), everything under `.scratch/roundtrip/`:

- **V2 — restore → verify → build, both units, no network:** a scratch restore-vehicle csproj with
  `vendor/packages` as its only NuGet source (`<clear/>` + local folder) restored both packages into an isolated
  packages folder; extracted roots have exactly the vendored-tree shape plus NuGet's own extras (`.nupkg`,
  `.sha512`, `.nuspec`) — the lenient-verify case as designed. `pack.cs --verify-tree` passed 155/155 on both.
  Full-solution **forced rebuilds** (`--no-incremental`, freshness confirmed via output timestamps) against each
  extracted root: exit 0, 0 warnings (the NU1503 modlet baseline lives in restore, which was a no-op — nothing
  new), correct layout auto-detected for both `*_Data` names, `VerifyGameInstall` satisfied, and the
  `ModsDir`/`SdtdSavesDir` scratch redirects stayed empty — plain builds deploy nothing, as designed. The
  consumption path is proven end-to-end before any credential, feed, or workflow exists.
- **V3 — every tamper/refusal path fires with a readable error, exit 2:** flipped byte in a restored DLL →
  SHA-256 mismatch naming the file and both hashes (and re-verifies clean after restoring the byte);
  tree-at-wrong-label → coordinates-disagree refusal; `<repository>` injected into a stub → the §1 leak-guard
  refusal.
- Phase 4 note: the scratch restore-vehicle is the template for `build/ci/GameAssemblies.csproj` +
  `nuget.config` — same shape, plus the GitHub source, credentials-from-env, and source mapping.

**Phase 4 — done 2026-07-31.** The four committed CI files exist; everything locally validatable was validated:

- `build/ci/GameAssemblies.csproj` — the version pins (single source of truth, per-unit), CI-only by design;
  its header explains the bump-PR flow. Proven: restores both pins from the local package source
  (`--configfile` override), and the solution build is untouched (the project is not in the .sln; NuGet config
  discovery is per-project-directory, so mod restores never see `build/ci/nuget.config`).
- `build/ci/nuget.config` — GitHub feed + nuget.org with source mapping (`7DtD.*` → feed only, `*` → nuget.org
  for Cronos), bot-account credentials from `%PACKAGES_READ_TOKEN%`, no token material in the file. XML-comment
  gotcha worth remembering: `-` `-` is illegal inside XML comments, hence the escaped spellings there.
- `build/ci/game-versions.json` — seeded with the b13 published-state (truthful: those are the packed-but-not-
  yet-pushed packages). Wiring proven: `steam_check.cs --raw <capture> --published build/ci/game-versions.json`
  reads it and reports both units RELEASE, exit 1 — correct, since Steam is already ahead of b13.
- `.github/workflows/build.yml` — per §5: push-to-main/PR/dispatch, `contents: read` only, per-ref concurrency,
  fork-PR skip guard (cosmetic; the secret's absence is the enforcement), SDK pinned 10.0.x, tool selftests as
  a first step (the tools' first Linux execution), restore → glob-the-single-version (the workflow never
  duplicates the version number) → `--verify-tree` → plain Debug build with `ModsDir`/`SdtdSavesDir` redirects,
  matrix over both units, `dotnet test` placeholder for #14. YAML validated by review only — phase 6's runs are
  the real test, deliberately.
- Not locally testable, deferred to phases 5/6: feed auth (no token/packages exist yet), the workflow end-to-end.

**Phase 5 — done 2026-07-31 (first publish + feed setup).**

- **b13 was never pushed, by retention logic:** Steam's current build is still player-version 3.1.0 (branch
  re-point), so its package version shares the `3.1.0` retention key with b13 — pushing b13 would have uploaded
  something scheduled for deletion. Re-vendored instead at **`V3.1.0-b14`** (label from the client log's
  `Version: V 3.1.0 (b14)` line, logs at `%APPDATA%\7DaysToDie\logs`): buildids 24436778/24436799, exactly
  matching Steam's public branch heads. Packed clean; pins bumped
  (`GameAssemblies.csproj` + `game-versions.json` → 3.1.0.14); `steam_check --live` now reports both units
  up-to-date, exit 0.
- **Pushed by the human:** both nupkgs accepted by `nuget.pkg.github.com`. (Client quirk: NuGet's own glob
  chokes on forward-slash patterns on Windows, and `dotnet nuget push` takes one path — a bash loop over the
  files is the working shape; `release.cs` should push per-file, never rely on push-side globbing.)
- **Feed posture confirmed in the UI:** both packages **Private**, **no linked repository**; the org already
  blocks public package creation.
- **V4 PAT-flavor record (so rotations don't re-litigate):** classic `write:packages` force-includes `repo` —
  the UI does not allow deselecting it. Fine-grained PATs don't offer Packages permissions for this org at all
  (not shown in the permission list) — the NuGet registry isn't covered yet; re-check at future rotations.
  Landed on: classic write+delete+repo(forced) on the human account, short expiry, password-manager only,
  entered via prompt (never argv/history). Read side: bot classic `read:packages` in the repo secret
  `PACKAGES_READ_TOKEN` (repo-scoped secret deliberately — an environment would add gating ceremony that a
  read-only token doesn't warrant).
- **Bot access is per-package-ID, once ever** (versions inherit the package's access list): `str0ngh34rt-bot`
  granted Read on both packages. The link-to-private-repo alternative (inherited access, zero per-package
  clicks) was considered and rejected: it couples package visibility to a repo's visibility — a cousin of the
  §1 leak.

**Phase 6 — done 2026-07-31 (V5: the workflow ran green on Linux).** Fired by the phase-5 push to main
(deviation from the planned PR branch — same workflow, `push` event instead of `pull_request`; the PR path gets
exercised naturally by the next version-bump PR). Run 30664311536:

- **Both matrix legs green** — game 28 s, dedicated-server 25 s total, every step executed: tool selftests
  (the C# tools' first Linux execution — both green, no platform seams), restore from the live private feed via
  the bot secret, single-version glob + `--verify-tree` (155/155 on both restored trees), full-solution Debug
  builds against each unit. **f5b §8's "developed on Windows, asserted for Linux" risk is now cashed: zero
  Linux discoveries.** #21's standing compile-against-both check is live on every push/PR from here on.
- **Accidental negative control, worth having:** the phase-4 push (before packages or secret existed) left a
  red run that failed exactly where it should — restore, `error : Value cannot be null or empty string
  (Parameter 'password')` from the unexpanded `%PACKAGES_READ_TOKEN%`. Missing credentials are loud, never a
  silent green.
- Cosmetic annotation to pick up in some future touch: actions/checkout@v4 + setup-dotnet@v4 target Node 20
  (deprecated on runners); bump to the next major versions eventually. Not filed as an issue — it rides along
  whenever the workflow is next edited.

**Phase 7 — done 2026-07-31 (`vendor.cs` port + `release.cs`).**

- **`vendor.cs`** replaced `vendor.py` (retired via `git rm`), equivalence proven against the Python-generated
  b14 trees: all 155 file hashes and manifest metadata identical per unit, nuspec byte-identical modulo one
  *deliberate* improvement — the port always writes LF, where Python's `write_text` produced platform-dependent
  CRLF on Windows. A `pack.cs` run over a `vendor.cs`-generated tree passed every validation.
- **Port shook out a real pack.cs bug:** a relative `--vendor-root` reached MSBuild as a relative
  `NuspecBasePath`, which MSBuild resolves against the stub *project* directory — path doubled, pack failed.
  Masked until now because the default root is always absolute. Fixed: tree and output paths are absolutized at
  entry.
- **`release.cs`** implements §6 with `--dry-run`/`--steamcmd`/`--steam-user`/`--commit`/`--selftest`.
  Verified (V8):
  - Selftest 11 checks: the plan's verbatim retention case (`3.0.0.259`, `3.0.1.4`, `3.0.1.7` → deletes exactly
    `3.0.1.4`), sole-version and cross-key preservation (the #37 lagging-mod guarantee), unparsable-version
    never deleted; label validation (forward ok incl. major jumps, unchanged/backward/bad-grammar refused);
    csproj bump surgical per package id, unknown id throws.
  - **Live nothing-new guardrail:** with the published state current, exits 0 "Nothing to publish" with no
    prompt.
  - **Live full stale path** (state file temporarily reset to b13 — making `V3.1.0-b14` the truthful next
    label): detection of both units, hint display, piped-label validation, `vendor.cs --force` + `pack.cs` for
    both units, clean `--dry-run` stop before push. State restored, working tree clean.
  - **Live unchanged-label refusal:** exit 2 with the rollback-education message.
  - Rollback decisions from steam_check abort loudly before any prompt (code path; the live rollback itself
    can't be simulated against real Steam).
- **Still pending, by nature:** `--steamcmd` end-to-end and retention against the real Packages API — both get
  their first genuine exercise at the next real game update, watched. Push is per-file (never glob), matching
  the phase-5 quirk record.

**Phase 8 — 2026-07-31 (`check-for-new-game-version.yml`, shadow mode).**

- The workflow ships exactly as §6b specified: daily cron + manual dispatch, no secrets, `contents: read` +
  `issues: write`, SteamCMD installed from Valve's Linux tarball behind a `steamcmd` wrapper on PATH (matching
  what `steam_check.cs` looks for), `steam_check.cs --live --json` as the sole decision-maker, verdict + full
  decisions JSON into the job summary, query errors (exit 2) fail the job red — never mistakable for quiet.
- **Shadow mode is a one-word flip:** `NOTIFY: 'false'` at the top. The issue steps are fully implemented but
  gated — on flip, exit 1 opens or updates a single `New 7DtD build on Steam` tracking issue @mentioning the
  owner (with the release.cs runbook line), and exit 0 auto-closes it once the published state catches up.
- Verdict shell logic validated locally against all three real exit codes (up-to-date, would-notify via the b13
  fixture state, query-error via a missing state file); the `set -e`/`&&`-chain interaction proven benign
  empirically.
- **V9 status:** decision fixtures were §6b's (11 steam_check checks). The dispatch smoke test took three runs
  to go green (2026-08-01), both failures being the SteamCMD *install* step, never the decision logic:
  first a silent `curl -s` piping an error into tar ("gzip: unexpected end of file" — fixed with `-f`/retries
  and download-to-file), which then revealed the real cause — **`steamcmd.valvesoftware.com` is NXDOMAIN**; the
  once-documented vanity host no longer exists. Fixed with an ordered mirror list of the long-standing CDN
  hosts (`media.steampowered.com`, `steamcdn-a.akamaihd.net`) that logs which mirror served. Third run:
  **green, "up to date" in the summary — the shadow soak started 2026-08-01.** The notify-path issue steps stay
  gated until the soak proves quiet (the flip is tracked as its own issue at phase 9).

**Addendum: `push.cs` — done 2026-07-31 (§10 decision 8 has the research/experiment record).** Selftest 7
checks (retention suite moved here from release.cs verbatim, numeric-not-lexicographic version sort,
highest-version selection, nuspec-inside-nupkg identity read); release.cs re-verified at 7 checks + live
nothing-to-publish; both dry-runs correct. **Acceptance run passed (owner, 2026-07-31):** all four files
skipped as duplicates (idempotence live), retention no-op, and both packages' latest tag — which the backfill
had left pointing at `3.0.1.4` — reconciled to `3.1.0.14`, confirmed in the UI.

**Phase 9 — done 2026-08-01 (docs + close-out).** CLAUDE.md: *Building without the game* now names `vendor.cs`,
and a new *CI, packages, and publishing* subsection carries the feed/CI/tools story (leak rules, the one-command
publish routine, the C#-tools inventory, feed hygiene). README deliberately untouched — #20 owns it and got a
comment listing the CI-story candidates. #21 got the "standing check is live" note (its remaining scope:
local dual builds, per-unit divergence handling). #39 filed to carry the NOTIFY flip after the soak
(criteria: weeks of quiet summaries, or one real release observed round-trip). **#22 closed 2026-08-01** with
the four-deliverables summary; follow-ups spawned across the effort: #37 (per-mod pinning), #38 (release-window
timing), #39 (notification flip). This doc is now a record, not a tracker — status lives on the issues.

**Backfill experiment — done 2026-07-31.** Both units' `V3.0.1-b4` packages published (buildids
24117861/24117900, verified against the captured `v3.0.1` branch heads before vendoring); the feed holds
`3.0.1.4` and `3.1.0.14` side by side — the #37 cross-key retention guarantee live. Bonus finding: the full
solution compiles clean against **both** 3.0.1 trees (0 warnings, 0 errors) — today's mods are
compile-compatible one version back. The SteamCMD branch-install, `--install-dir` vendoring, and
`force_install_dir` appmanifest-fallback paths all worked first try. What it surfaced became the push.cs
addendum above. Original plan text follows: publish **3.0.1** packages so older-version
builds/tests are possible before 3.2/4.0 arrives — and use it to exercise the paths a routine release can't:
SteamCMD *branch* installs (`+app_update <id> -beta v3.0.1`) into `+force_install_dir` scratch dirs (never the
live installs), vendoring via `--install-dir` from a non-library layout (`vendor.cs` gained the
`<install>/steamapps/` appmanifest fallback for provenance), and — at push — live retention proving the #37
cross-key guarantee (`3.0.1.4` and `3.1.0.14` coexist; nothing deleted). Label `V3.0.1-b4` (corroborated by the
2026-07-17 client log's `Version: V 3.0.1 (b4)`; expected buildids from the captured `v3.0.1` branch heads:
game 24117861, server 24117900 — verified against the downloaded appmanifests before vendoring). Pins and
`game-versions.json` are **not** touched — they track the *current* version; 3.0.1 sits on the feed for #37-era
consumers. Backfills are a manual vendor+pack+push routine by design; `release.cs` only ever publishes Steam's
current head.

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
| V8 | `release.cs` guardrails | Nothing-new state exits 0 without prompting; label regression / label-moved-without-buildid refused; retention selection verified against a synthetic version list (`3.0.0.259`, `3.0.1.4`, `3.0.1.7` → deletes exactly `3.0.1.4`) before any real API delete; `--steamcmd` path exercised once end-to-end |
| V9 | Check workflow | `should_notify` fixture tests pass, including the §6b edge cases (rollback, re-promotion, staggered units, unwatched-branch churn → silent). With `game-versions.json` temporarily behind Steam: the tracking issue appears with correct buildids; a re-run updates rather than duplicates; restoring the file auto-closes it. **Soak:** shadow mode runs quiet day-to-day and fires only for a genuine release before notifications switch on |

## 9. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Any leak channel | The standing constraint | §1 closes each named channel; the design never relies on a single guard |
| Linux run fails | Real possibility — first execution of the build off Windows | Expected-and-welcome discovery (V5); MSBuild normalizes `\` separators and the trees are case-exact by design, so residual risk is small |
| GitHub Packages rejects the content-shaped nupkg or the PAT flavor | Server-side validation is the one thing not testable locally | V2 proves everything client-side first; PAT flavor resolved at V4 with classic as the known-good fallback |
| Storage quota exhaustion | ~30–50 MB/version-pair compressed vs ~500 MB free tier | Keep current + previous, delete older (§3); packages are always regenerable from a licensed install |
| `dotnet pack` stub-project friction | The NuspecFile pattern is known-janky | Fallback: hand-built OPC zip via `System.IO.Compression` (pack.cs owns the choice; interface unchanged) |
| Version pinned in CI drifts from feed reality | Restore simply fails red | That *is* the check; bump PR flow (§6) makes green the proof |
| SteamCMD friction (interactive first login, client-managed-dir races, VDF output parsing) | Automation convenience, not correctness — vendoring never depends on SteamCMD | Caveats recorded in §6; `--steamcmd` is opt-in; the buildid *query* is anonymous and needs no login; VDF parsing is a two-field regex extraction, and a parse failure fails loudly, never silently reports "up to date" |
| Retention deletes the wrong version | Would only cost regeneration time, not data | Selection logic verified against synthetic lists first (V8); deletes are per-unit, per-key, never bulk |
| Notification too noisy (fires on non-released builds) or too quiet (misses a release path) | The owner's explicit concern; branch-head polling filters most noise structurally, but promotion edge cases are empirical | Exploration phase 1 encodes real data as fixtures before design freezes; shadow-mode soak (V9) proves quiet-in-practice before any issue is ever filed |

## 10. Decisions (owner, 2026-07-31)

The original open questions, resolved — plus the automation scope they added:

1. **Token accounts:** bot for read, human for write (folded into §3/§7b).
2. **Retention:** keep only the latest build per `major.minor.patch`, per unit (folded into §3).
3. **Matrix:** stay Debug × 2 units.
4. **Automation mandate:** the human's part must be as automated as possible — hence `release.cs` as the single
   guarded command (§6), SteamCMD both for the new-version guardrail and optional install updates, and the
   scheduled Steam-buildid check workflow for notification (§6b).
5. **Notify on releases only, not every Steam build:** the notifier watches branch heads, its decision logic is
   designed empirically first (exploration is now phase 1) and shared with `release.cs` via `steam_check.cs`,
   and it soaks in shadow mode before it is allowed to file an issue (§6b).
6. **Scripting language is C#, not Python** (#36, `.ai/scripting-language-research.md`): new tools are born as
   C# file-based apps (`dotnet run tool.cs`); `steam_check` was the pilot port (equivalence proven against the
   Python original's outputs); `vendor.cs` ports inside #22 because `release.cs` depends on it; the remaining
   Python (`compare-eval.py`) migrates opportunistically under the parked #36 thread. Tool sharing is process
   composition over `--json` + exit codes, not `#:include`. CI consequence: `setup-dotnet` pins SDK 10.x (§5).
7. **No PRs** (2026-07-31): the owner is the sole contributor and commits directly to main, so every "bump PR"
   in this doc reads as "commit to main; the Build run on main is the proof". `release.cs` ships `--commit`
   instead of the planned `--pr`. (Possible future exception the owner flagged: agent-sandbox workflows that
   hand back PRs — a `--pr` mode can be added then.)
8. **`push.cs` is the single push path** (2026-07-31, owner-prompted after the backfill): directory semantics
   (default `vendor/packages`), ascending-version pushes with `--skip-duplicate` (idempotent re-runs; per-file
   pushed/skipped reporting), retention moved here from `release.cs`, and **GitHub latest-tag reconciliation**.
   Research + experiment established the facts: GitHub's "latest" is most-recent-`created_at`, not highest
   version (npm gets mutable dist-tags; NuGet gets no tags at all); deleting a version does NOT permanently
   retire its number — delete + re-push of the identical nupkg works and mints a fresh `created_at`, verified
   live on the unpinned `3.0.1.4` (id 1087174034 → 1087373330). Reconciliation therefore: if the highest
   version isn't newest-created, delete + re-push it — only ever with the identical nupkg already in hand, with
   a duplicate-refusal escape hatch pointing at the 30-day restore API. `release.cs` delegates its push and
   retention steps to `push.cs` (process composition again). **Restore tested too (2026-07-31, same
   experimental shape):** delete → restore preserves the version row completely — same id, same `created_at`,
   only `updated_at` bumps — and the latest tag does not move. So restore is *recovery-only* (proven working,
   which validates push.cs's escape hatch), it cannot reconcile the tag, and delete + re-push is the sole
   reconciliation lever, by evidence rather than inference.

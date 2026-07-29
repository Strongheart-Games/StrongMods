# Plan: F5b — private per-unit game-assembly packages (issue #15)

- **Issue:** [#15](https://github.com/Strongheart-Games/StrongMods/issues/15) — status lives there. Repurposed
  2026-07-28 from "Refasmer reference assemblies" after the premise was challenged; §1 records the ledger.
- **Goal:** versioned, private packages of the **real** game assemblies, one per releasable unit, so the solution
  can build — and headless tests can run — on a machine with no game install. This is the key that unlocks CI.
- **Scope (decided 2026-07-28):** generate + consume + verify locally. The private-feed wiring for CI (GitHub
  Packages) ships in a follow-on together with the Actions workflow itself.

## 1. Why real assemblies, not Refasmer'd reference assemblies

The original design stripped implementations with Refasmer. Challenged 2026-07-28; the ledger that killed it:

- **Size was the whole advantage, and it is negligible here.** Each unit's `Managed` directory is ~48 MB
  (`Assembly-CSharp` itself is 11 MB). Stripping to ~5 MB solves a problem this repo does not have.
- **Reference assemblies cannot execute.** The most valuable planned test — load the game assemblies headlessly
  and assert every Harmony patch finds and applies to its target — needs real method bodies. That is precisely
  the failure class already hit once in production: a patch that applied to the base game but not the dedicated
  server. Compile-only coverage (all refs can offer) would never catch it; a patch-application test against real
  bits does. Tests are the point of the whole CI effort, so the artifact CI consumes must be the real thing.
- **One artifact kind, not two.** If real bits must move through a private channel for testing anyway, stripped
  refs become a second generated, versioned artifact that buys only a marginally safer leak profile.
- Refasmer's per-assembly failure modes (obfuscation, mixed-mode) disappear entirely — copying cannot fail that way.

What survives from the original design: everything else. Per-unit packaging, the labeling/provenance scheme, the
normalized install-shaped tree, zero-build-file consumption, the private-only constraint, and the
first-compile-against-the-server verification.

**IDE note:** local development is untouched — developers build against their real install exactly as today, so
decompiling navigation ("go to implementation") is unchanged. The packages exist for environments without a game
install.

## 2. The legal constraint

**This repo is public.** The packages contain the game's (and Unity's, Noemax's) licensed assemblies, so they must
**never be committed here or published anywhere public** — not in git, not a public feed, not Actions artifacts on
a public repo. They live in the gitignored local `vendor/` directory and, later, a **private** GitHub Packages
NuGet feed in the Strongheart-Games org, restored with a token. This is private storage of licensed files by a
license owner for their own development — a simpler posture than the derivative-works question Refasmer raised.

## 3. Naming: releasable units

The base game (Steam app **251570**) is one releasable unit acting as *both* client and peer-to-peer server; the
**dedicated server** (app **294420**) is a separate unit with its own release cadence and buildids. Docs and
tooling say **game** and **dedicated server** — "client" is avoided as inaccurate. One tree / future package per
unit, versioned independently, so neither revs because the other updated:

| Unit | Steam app | Package id (future) | Role |
| --- | --- | --- | --- |
| Game | 251570 | `7DtD.Assemblies.Game` | What every mod compiles against today |
| Dedicated server | 294420 | `7DtD.Assemblies.DedicatedServer` | **No compile-time consumer today, but a deliberate target**: every mod must run on both binaries, and they are *almost* identical — a Harmony patch has already once applied to the game but not the dedicated server. This issue generates and verifies both trees; standing compile-against-both is [#21](https://github.com/Strongheart-Games/StrongMods/issues/21) |

A third-party dependency (as `PrismaCore` was before its 2026-07-28 removal in `421f83e`) would get its own unit
if one ever returns, with a package id in the `7DtD.Mods.<Name>` family (e.g. `7DtD.Mods.PrismaCore`).

Package ids deliberately carry the **`7DtD` prefix, not `StrongMods`** (decided 2026-07-28): the ids describe the
contents — the game's assemblies, which are not specific to this solution — not the project that happens to build
the packages. The prefix question is informational only while the feed is private and org-scoped; nothing here is
ever publishable regardless (§2).

**Layout normalization (verified against the live installs):** the dedicated server's data directory is
`7DaysToDieServer_Data`, not `7DaysToDie_Data`; both units ship `Mods/0_TFP_Harmony`, and the server carries the
full Unity module set the builds reference (incl. `UnityEngine.AudioModule`). The generator **normalizes the
dedicated-server tree to the game shape** so one `SdtdDir`-derived path family consumes either tree with zero
build-file changes; `manifest.json` records the true source paths.

## 4. Design

### The generator: `build/tools/vendor.py`

Python, matching the `compare-eval.py` precedent, and **cross-platform by design**: 7DtD installs on Linux, the
repo owner's production environment is a Linux dedicated server, and CI runners lean Linux. Concretely: `pathlib`
throughout, no PE-metadata or PowerShell dependencies, exact-case output layout (Linux is case-sensitive), default
install discovery for the Windows Steam path and the common Linux ones (`~/.steam/steam/steamapps/common/...`,
`~/.local/share/Steam/steamapps/common/...`), overridden by explicit args or `SDTD_HOME` — same precedence as the
build. No external tools at all: generation is a filtered copy.

1. Resolves the install for the requested unit (`--unit game` | `--unit dedicated-server`) and the output root.
2. Copies every `*.dll` from the unit's `Managed/` directory and `Mods/0_TFP_Harmony/0Harmony.dll` into an
   install-shaped tree (normalized per §3), byte-identical:

   ```
   vendor/game/<label>/7DaysToDie_Data/Managed/*.dll
   vendor/game/<label>/Mods/0_TFP_Harmony/0Harmony.dll
   vendor/game/<label>/manifest.json                                (provenance; §5)
   vendor/game/<label>/7DtD.Assemblies.Game.nuspec        (stub for the CI packaging follow-on)
   vendor/dedicated-server/<label>/...                              (same shape)
   ```

3. Records per-file SHA-256 in `manifest.json`, so a tree (or a package built from it) is verifiable against its
   source install at any time.

### Consumption: no build-file changes at all

```bash
dotnet build StrongMods.sln -c Debug -p:SdtdDir=vendor/game/<label> -p:ModsDir=.scratch/deploy
```

`SdtdManagedDir`, `SdtdHarmonyDir`, `FrameworkPathOverride` and `VerifyGameInstall` all derive from `SdtdDir` and
resolve into the tree unchanged. The same command with `-p:SdtdDir=vendor/dedicated-server/<label>` compiles the
solution against the server binary. Future tests (#14) load the same trees for patch-application checks — the
assemblies are real, so anything short of booting the game is on the table.

**Footgun, documented rather than engineered away:** a Debug build with no `-p:ModsDir` override deploys into
`$(SdtdDir)\Mods` — inside the tree, in vendor mode. Vendor-mode builds always redirect `ModsDir` (CI always
will); a stray deploy is caught by the manifest hash check and fixed by regenerating.

## 5. Versioning: human label + machine provenance

Requirements (stated 2026-07-28): fine-grained enough to replicate **any** build, stable or experimental; and
human-readable in the `major.minor[.patch] b#` scheme players and modders know.

- **The label is supplied by the human running the generator** (`--label`), validated against
  `V<major>.<minor>[.<patch>]-b<build>` (e.g. `V2.5-b8` — the in-game `V 2.5 b8` made filesystem- and
  package-safe). It names the tree directory and, later, the NuGet version. The Steam `betakey` is deliberately
  **not** part of the label: it is a marketing/channel name the developers re-point after release, so encoding it
  would make labels unstable. `major.minor b#` is already unique across channels.
- **Provenance is machine-captured** into `manifest.json`: the Steam `buildid` parsed from the unit's appmanifest
  (`appmanifest_251570.acf` / `294420` — cross-platform text files that pin the exact depot build), the mounted
  `betakey` (informational only), generation timestamp, source paths, and per-file SHA-256. The label says *what
  a human wanted*; the manifest proves *what was actually read*.

Future NuGet mapping (recorded for the CI follow-on, not exercised here): one package per unit, 4-part version
`<major>.<minor>.<patch|0>.<build>` (e.g. `2.5.0.8`), each revved only when its own unit updates.

## 6. Plan of attack

| # | Work |
| --- | --- |
| 0 | `/vendor/` gitignore entry with the never-commit comment |
| 1 | `build/tools/vendor.py`; generate both unit trees from the local installs |
| 2 | **Verification** (below); fix what it surfaces |
| 3 | Docs: CLAUDE.md *Building* gains a short *Building without the game* note; plan-doc results; comment on #15; raise the CI feed + workflow follow-on issue |

## 7. Verification

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Tree fidelity | Every copied file byte-identical to its source (manifest hashes recomputed and compared); file counts match the source sets; dedicated-server tree correctly normalized |
| V2 | **Full solution builds against the game tree with no install in play** | `-p:SdtdDir=vendor\game\<label> -p:ModsDir=.scratch\deploy`, both toolchains, exit 0, warnings at baseline |
| V2b | **Full solution builds against the dedicated-server tree** | Same command, dedicated-server tree — the first time anything in this repo compiles against the server binary. A failure is *discovery* (real surface divergence), recorded and resolved under [#21](https://github.com/Strongheart-Games/StrongMods/issues/21) |
| V3 | Output equivalence | Game-tree deploy sets file-identical to a normal-install build; content files byte-identical. (The inputs are byte-identical real assemblies, so this is a sanity check on path plumbing, not on compilation semantics) |
| V4 | The guard still guards | `VerifyGameInstall` passes against a tree; a bogus `SdtdDir` still fails with the readable error |
| V5 | Live installs untouched | Standard check; vendor-mode builds are redirected by definition |

No runtime smoke check is needed (the F1 V6/V8 analogue): mods compiled against byte-identical copies of the same
assemblies are the same mods.

## 8. Risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| Accidental publication of licensed files | The standing constraint | Gitignored `/vendor/`; never-commit comments at every touchpoint; the future feed is private-only; nuspec stub carries a private-repo `requireLicenseAcceptance`-style warning comment |
| Wrong human label on a tree | Label and reality could disagree | `manifest.json` buildid + hashes are the arbiter; the CI packing step can add a label↔buildid consistency check |
| Linux behaviour asserted but developed on Windows | Generator and consumption are designed cross-platform; this machine is Windows | Case-exact layout, no platform-only APIs; actual Linux execution is verified when the CI workflow first runs — called out in the handoff rather than implied |
| Game update changes the assemblies | Expected, routine | Regenerate; version-stamped trees make staleness visible; manifest hashes distinguish "same label, different bits" instantly |

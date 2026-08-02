# #59 — vendor.cs captures `Data/Config` (plan)

Issue [#59](https://github.com/Strongheart-Games/StrongMods/issues/59): vendored trees and the CI packages carry
only the Managed assemblies and `Mods/0_TFP_Harmony`, so patch-application (#43 wave D) and localization (#58)
tests could only run against a live install — which would be the first live-install dependency on the CI path.
Fix: capture `Data/Config` **wholesale** (the #48 rule: the folder, never a cherry-pick), re-vendor the four
trees in play, repack, re-push with `push.cs --repush-duplicates`. This doc is the working plan; each phase's
results are recorded here as it completes (the f5b §7b pattern). Status and follow-ons live on the issue, not here.

## 1. Measured facts (2026-08-02, this machine)

| Fact | Value |
| --- | --- |
| `Data/Config` relative path | `<install>/Data/Config` — identical in both units (`Data` is not the Unity data dir, so no per-unit naming) |
| Contents, live V3.1.0-b14 (both units) | 59 files, ~33 MB: 54 XML (incl. `XUi_*` subdirs) + `Localization.csv` (16 MB) + `BlockUpdates.csv`, `OversizedConversionTargets.txt`, `Stealth.txt`, `XML.txt` |
| Contents, backfill v3.0.1 installs | 59 files, ~32 MB each — same shape |
| Live installs still on b14? | Yes — appmanifest buildids 24436778 (game) / 24436799 (server) match the trees' manifests |
| Backfill installs survive? | Yes — `.scratch/steam-installs/{game,dedicated-server}-v3.0.1`, buildids 24117861 / 24117900 match the trees' manifests |
| The four trees in play | {game, dedicated-server} × {V3.1.0-b14, V3.0.1-b4} — exactly the four nupkgs in `vendor/packages/` and the versions the feed retains (#48 used the same set) |
| V3.1.0-b13 trees | 155 files (pre-#48), off the feed (retention), no install exists to re-vendor from — left as-is, documented here |
| Feed quota | Free-tier ~500 MB; #48-era nupkgs ~17 MB each. XML/CSV compress well; expected growth to ~22–25 MB each, measured at pack time |

## 2. The change

`build/tools/vendor.cs` only (~12 lines + header comment):

1. Next to the existing Managed/Harmony existence checks: fail loudly if `<install>/Data/Config` is missing —
   a tree without it is now a gap by definition.
2. After the Harmony copy loop: copy `Data/Config` wholesale with the same
   `GetFiles(..., AllDirectories)`/ordinal-sort/`VendorFile` idiom, manifest-hashing every file under
   `Data/Config/<rel>`.
3. Header comment: the tool now copies Managed + `Mods/0_TFP_Harmony` + `Data/Config`, and why.

Also in the same change: the one-line CLAUDE.md description of vendor.cs ("copies a unit's assemblies") gains
"and vanilla `Data/Config`" so the entry point stays truthful.

**Not** touched: `pack.cs` / `push.cs` / `release.cs` (contents flow entirely from the manifest — pack's final
nuspec lists whatever is manifested, push is content-agnostic, and future releases inherit the capture
automatically); `build/GamePaths.props` (unit detection keys on the Unity data-dir names, `Data` is not one);
`.github/workflows/build.yml` (post-restore `--verify-tree` picks up the new entries from the manifest — CI
transfer grows by the compressed delta per run, noted, no cache today); `Tests` (consumers arrive with #43/#58).
`.ai/ci-feed-and-workflow.md` §2's contents listing is already one revision behind (#48 left it); widening that
is doc-rot-sweep material, raised separately rather than folded in.

## 3. Phases and verification

Gates per CLAUDE.md: each phase gets its own explicit go; previous phase's artifacts committed first.

### B. Edit vendor.cs + no-touch verification

Edit per §2. Then vendor the live game b14 install to a scratch root — nothing tracked or live is written:

```
dotnet run build/tools/vendor.cs -- --unit game --label V3.1.0-b14 --output-root .scratch/59-verify/vendor
```

Proof of no regression, from the manifests alone: the scratch manifest must contain the existing tree's 169
entries **hash-identical**, plus exactly 59 additions, all under `Data/Config/`; then
`pack.cs --verify-tree .scratch/59-verify/vendor/game/V3.1.0-b14` passes strict. Record counts here.

### C. Re-vendor the four trees in place + repack

Snapshot each tree's `manifest.json` to `.scratch/59-verify/` first, then for each of the four:
`vendor.cs --force` (b14 units from live installs, b4 units from the backfill installs), manifest-diff old vs
new (pre-existing entries hash-identical; additions exactly `Data/Config/**`), then `pack.cs` (its internal
validation re-hashes everything and re-verifies the nupkg). Consumer regression: `dotnet test` on all five
targets (live install + four trees) as in #48's close-out; the manifest diff already proves the DLL set is
byte-identical, so the suite is expected unchanged per target. Record file counts, tree/nupkg sizes, buildids,
test counts here. Writes only gitignored `vendor/` and `.scratch/`.

### D. Push (owner-run)

`dotnet run build/tools/push.cs -- --repush-duplicates` — deletes each same-version feed entry immediately
before its own push (the #48 mechanics; ascending order keeps the latest tag correct, retention no-ops).
`PACKAGES_WRITE_TOKEN` is not in the agent environment, and #48's push was owner-run: same here. Post-push
round-trip proof: the next CI run on main restores the same pinned 3.1.0.14 packages — now with `Data/Config` —
and `--verify-tree` re-hashes them. Feed storage after: four nupkgs, sizes recorded here vs the ~500 MB tier.

### E. Close out

Results comment on #59 (this doc distilled), owner closes. Unblocks #43 wave D and #58; #23/#45 matrix gains
XML coverage for free.

## 4. Risks

- **Quota**: worst case ~4 × 25 MB ≈ 100 MB, well inside ~500 MB; measured before push.
- **Repush window**: each version is absent from the feed for ~2 s during delete+push — local, human-triggered
  moment, same as #48; CI restores pinned versions and would only notice if run inside that window (retry).
- **Local NuGet caches**: machines that ever restored 3.1.0.14 hold the pre-#59 bytes keyed by version. CI
  runners are ephemeral (unaffected); local restores of `GameAssemblies.csproj` are documented as not-expected.
- **b13 trees stay thin**: nothing consumes them (off-feed); `--verify-tree` still passes on them since their
  manifests remain self-consistent.

## Results

### B. vendor.cs edited; no-touch verification passed (2026-08-02)

The change is 15 lines across two files: `vendor.cs` gains a `Data/Config` existence check beside the Managed
and Harmony ones, a wholesale copy loop in the Harmony loop's idiom, and a rewritten header paragraph; CLAUDE.md's
one-line description of the tool now says "assemblies and vanilla `Data/Config`". Nothing else was touched —
`pack.cs`/`push.cs`/`release.cs`/`GamePaths.props`/CI were confirmed content-agnostic during scoping (§2) and the
run below exercised all of them unmodified.

Vendored the live game b14 install to `.scratch/59-verify/vendor` (nothing tracked or live written):

| Check | Result |
| --- | --- |
| Tree | **228 files, 80.9 MB** (was 169 / 48.9 MB) — the issue's ~82 MB estimate, confirmed |
| Manifest diff vs the in-place tree | **0 removed, 0 changed, 59 added** — every pre-existing entry byte-identical (size + SHA-256), additions confined to `Data/Config/`, `BlockUpdates.csv` → `worldglobal.xml` |
| `pack.cs --verify-tree` | 228/228 hash-match, buildid 24436778 |
| `dotnet build StrongMods.sln -c Debug -p:SdtdDir=<scratch tree>` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test -p:SdtdDir=<scratch tree>` | **126/126 passed** |

The enriched tree is still point-and-go as a build root, which was the property at risk. Capture shape matches
the issue's table exactly: 54 XML (incl. the three `XUi_*` subdirs) + `Localization.csv` + 4 other files; all
filenames plain ASCII with no path characters that NuGet would object to.

Diff helper: `.scratch/59-verify/manifest-diff.cs` (disposable, deleted at the end of phase C — the repo's
"maintained code is C#" rule means nothing here graduates).

### C. (pending)

### D. (pending)

### E. (pending)

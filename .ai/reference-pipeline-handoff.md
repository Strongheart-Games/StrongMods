# StrongMods 7DtD Reference Pipeline — Handoff

> Provenance: handoff document authored in a ChatGPT design session, imported 2026-08-16. Wording is verbatim from the
> handoff, with two import-time changes: markdown structure (headings, bold terms) was restored because the paste
> flattened it, and the *Goal and method* section was added the same day from strongheart's own framing — it is not
> part of the original ChatGPT text.

## Goal and method

This exercise re-derives the vocabulary and conceptual model for this problem space from scratch, deliberately
irrespective of the current implementation. The vocabulary around these tools grew confusing — much of it came from
earlier agent sessions before an effective agentic design and review workflow was established — and this document
exists to start the process of backfilling what was skipped.

Naming is load-bearing: a confusing name usually marks a poor design, and the right name gives real power over a thing
(Le Guin's "true names", minus the magic). So the exercise asked an independent model to define the right names first
and look at the code second.

Consequences for the reader:

* **Re-proposing something that already exists is the point, not an oversight.** The proposed conceptual model is a
  way of thinking about the problem, not a feature list. A good design often matches its conceptual model in
  structure, but it does not have to.
* Where the independent derivation converges with existing machinery, treat that as evidence the concept is real (and
  cheap to keep). Where it diverges, that flags the current vocabulary, not the proposal.
* Current-state facts enter the design as evidence and as backfill cost, never as objections. "The code already says
  X" is not an argument against a proposal.

The intended sequence after this document:

1. Agree a vocabulary and conceptual model.
2. Update `CONTEXT.md` (human-written, by design) with an overview and a pointer to more detail, plus a note that the
   vocabulary may not match the code for a while during backfill.
3. Backfill the implementation.

## Purpose

Continue a design review of the StrongMods tooling that acquires proprietary 7 Days to Die files from Steam, packages
the subset needed for StrongMods development/testing, publishes those packages privately, and manages supported game
versions.

The relevant programs are:

* `build/tools/steam_check.cs`
* `build/tools/vendor.cs`
* `build/tools/pack.cs`
* `build/tools/push.cs`
* `build/tools/release.cs`

Also read:

* `CONTEXT.md`
* the version declarations used by the build, especially `build/GameVersions.props`
* any nearby design/docs describing the private package feed, CI, or version workflow

Treat `CONTEXT.md` as authoritative for the repository's intent and vocabulary. Do not let terminology found
incidentally in the current implementation override it.

## Non-negotiable requirements

The system must be able to support:

* 7 Days to Die
* both Product values:
  * Game
  * Dedicated Server
* both OS values:
  * Windows
  * Linux
* any 7DtD version available through Steam, including historical versions where practical
* CI and other automation

Everything else is open for reconsideration, including names, boundaries between tools, package structure, workflow,
metadata, retention policy, and how the process is conceptualized.

## Repo-level vocabulary

`CONTEXT.md` defines the important 7DtD product matrix as:

* Product: Game or Dedicated Server
* OS: Linux or Windows
* Version: 7DtD version

Prefer this vocabulary unless there is a compelling reason to introduce another concept.

In particular, the current tools use unit in places to mean Game vs Dedicated Server. The prior review concluded that
this appears to duplicate the existing domain concept Product and should probably be eliminated.

## Current tool responsibilities

### `steam_check.cs`

Conceptually: discovery/checking.

It queries Steam metadata for Game and Dedicated Server and determines whether Steam's selected/current state has moved
beyond the state StrongMods has already published.

It deals with things such as:

* Steam app/build metadata
* the `public` branch
* known version branches
* identifying new upstream builds
* recognizing apparent rollbacks rather than silently treating every different build ID as an upgrade

It is also used by CI to detect new Steam releases.

### `vendor.cs`

Conceptually: capture/extraction.

Given a locally installed Game or Dedicated Server, it extracts the proprietary/reference material StrongMods needs.

The captured material is broader than just assemblies and includes roughly:

* managed DLLs
* TFP's Harmony mod/files
* game XML/configuration data

It creates a manifest with provenance and hashes so the captured tree can subsequently be verified.

The earlier review questioned the word vendor because these files are not really being vendored into the source tree. A
better domain concept may be snapshot.

### `pack.cs`

Conceptually: verify + package.

It independently verifies the captured tree against its manifest and then creates the private NuGet package used to
transport those proprietary reference files to development and CI environments.

It also contains protections intended to prevent accidental leakage or incorrect packaging of licensed content.

The independence of its verification from `vendor.cs` is considered a positive safety property.

### `push.cs`

Conceptually: publish + feed maintenance.

It pushes packages to the private GitHub Packages feed and also implements feed policy, including things such as:

* replacement/duplicate handling
* retention
* GitHub Packages' notion of "latest"

The prior review questioned whether push understates its responsibility and suggested publish.

It also questioned whether feed retention and "latest" manipulation belong in the publication operation at all.

### `release.cs`

Conceptually: orchestration.

It ties the other programs together into the normal workflow:

1. check Steam state
2. make sure the appropriate Steam installations are present/current
3. determine/obtain the human 7DtD version
4. capture the relevant files
5. package them
6. publish them
7. record the published state

Importantly, publishing a reference package is not necessarily the same as changing the versions StrongMods currently
develops/tests against.

The prior review considered that separation valuable and suggested making it an explicit part of the domain model.

## Current high-level data flow

A useful simplified model of the implementation is:

`Steam → installed product → captured reference tree → private NuGet package → package feed → StrongMods build/test`

`release.cs` orchestrates most of the left-to-right workflow.

## Proposed conceptual model

The previous discussion suggested that thinking of this as a conventional software release process is misleading.

A potentially better model is a 7DtD reference supply chain for StrongMods:

Discover → Acquire → Snapshot → Package → Publish → Adopt → Validate

Possible meanings:

**Discover**

Determine what builds/versions Steam exposes.

**Acquire**

Obtain a specific 7DtD installation from Steam.

**Snapshot**

Extract the exact subset of that installation StrongMods needs to build and test, together with enough provenance to
identify its origin.

**Package**

Create a private transport artifact for that snapshot.

NuGet is an implementation mechanism here, not necessarily part of the domain vocabulary.

**Publish**

Make that package available to CI and other development machines.

**Adopt**

Change StrongMods declarations so a version becomes a development/test target.

**Validate**

Build/test the declared support matrix.

This yields potentially useful lifecycle states such as:

Available on Steam → Snapshotted → Published → Declared → Validated

Do not assume this terminology is final. Evaluate it critically.

## Important distinction: published is not supported

One observation from the current implementation is worth preserving even if other terminology changes:

Having acquired/published reference material for a 7DtD version does not necessarily mean StrongMods claims to support
that version.

The repo can publish the reference artifact first and independently decide whether to add that version to development
or test declarations.

The design should make this distinction obvious.

## OS finding that needs investigation

A major unresolved question was whether Windows and Linux 7DtD installations actually contain different managed
binaries.

Initial investigation found evidence that they do.

For at least one current Steam build, `Assembly-CSharp-firstpass.dll` differs in file size between the Windows and
Linux depots for both Game and Dedicated Server.

Therefore:

* OS cannot safely be assumed to be only an installation-path concern.
* A snapshot's provenance should probably include the OS of the source installation.
* A Steam app `buildid` alone may not uniquely identify the actual bits used, because one app build can comprise
  different platform depots.

Investigate this directly against current Steam metadata and, preferably, actual installations rather than relying
solely on this handoff.

## Product OS vs Host OS

Keep these concepts distinct:

**OS**

The OS in the repo's Product × OS × Version matrix.

This describes the 7DtD installation/runtime being targeted:

* Windows
* Linux

A clearer implementation name, where ambiguity exists, may be `TargetOS` or `ProductOS`, but the domain vocabulary
should remain consistent with `CONTEXT.md` where possible.

**Host OS**

The operating system running StrongMods development/build/test tooling.

For example, a GitHub Actions Linux runner proves that StrongMods tooling can execute on a Linux host.

It does not necessarily prove compatibility with a Linux 7DtD installation if that job consumes reference files
captured from Windows.

Do not conflate these axes.

## Do not automatically expand CI to the full OS Cartesian product

The previous discussion did not conclude that every test should immediately become:

`Product × OS × Version`

Instead, first determine how materially the Windows/Linux snapshots differ.

Suggested experiment:

For one representative/current 7DtD version, obtain:

* Game / Windows
* Game / Linux
* Dedicated Server / Windows
* Dedicated Server / Linux

Compare same-product Windows/Linux pairs for:

1. file-set differences
2. hashes
3. assembly identity and references
4. types/members/signatures/accessibility
5. IL/method-body differences
6. `Data/Config`
7. TFP Harmony material

Do not restrict the comparison to public API compatibility. StrongMods uses Harmony, so internal/private implementation
differences can matter.

A possible future policy worth evaluating:

OS is always part of snapshot provenance. OS becomes a full validation dimension wherever the relevant snapshots differ
materially.

Challenge this if there is a safer or simpler rule.

## Steam provenance

The current system records Steam `buildid`, but that may not be sufficient to identify exact platform-specific source
content.

Investigate whether provenance should include:

* Product
* OS
* 7DtD Version
* Steam App ID
* Steam branch
* Steam build ID
* Depot ID(s)
* immutable Depot Manifest ID(s)
* hashes of all captured files

The installed Steam app manifest may provide useful depot-manifest provenance.

The goal is reproducibility and exact identification, not metadata collection for its own sake.

## Version modeling concern

The existing tooling appears to impose a modern version-label grammar resembling:

`V<major>.<minor>[.<patch>]-b<build>`

and derives NuGet versions from it.

This may conflict with the requirement to support any version available on Steam, because historical 7DtD releases use
Alpha-style names such as `A21.2`.

Investigate all historical Steam branches/versions that the system is expected to support.

Consider treating 7DtD Version as an opaque game-domain identifier, rather than making package-version syntax define
what constitutes a valid game version.

NuGet versioning may need an explicit mapping rather than a direct structural transformation.

## Steam Branch vs Version vs provenance

Keep these concepts separate:

**Steam Branch**

A Steam selector, e.g.:

* `public`
* `latest_experimental`
* a historical/version branch

**Version**

The 7DtD human/game version, e.g. conceptually:

* `3.1.0 b14`
* `2.6 b259`
* `A21.2 ...`

**Steam Build/Depot IDs**

Machine/provenance identifiers for the actual Steam content.

Do not casually use one of these concepts as a substitute for another.

## Current-release synchronization vs historical import

The existing `release.cs` workflow is strongly oriented around:

Has Steam `public` moved forward?

That is appropriate for normal update detection and CI notifications.

It may be inappropriate for deliberately acquiring an old Steam version.

Evaluate splitting the workflow conceptually into something like:

**Sync current**

Bring the private reference feed up to date with the current public 7DtD release.

**Import version**

Explicitly acquire a selected historical or non-public Steam version without applying "must move forward" semantics.

Names and executable boundaries are not decided.

Possible interfaces previously suggested included concepts such as:

* `refs sync`
* `refs import --branch ...`

The important distinction is semantic, not the exact CLI.

## Naming proposals from the earlier review

These are proposals, not accepted decisions.

### `unit` → `product`

Strong recommendation because `CONTEXT.md` already defines Product for exactly this concept.

Potential mechanical changes include:

* `--unit` → `--product`
* `SdtdUnit` → `SdtdProduct`
* `UnitInfo` → `ProductInfo`
* manifest `unit` → `product`

### `vendor.cs` → `snapshot.cs`

Rationale: the operation extracts a provenance-tracked subset of an installed product rather than vendoring third-party
dependencies into the repository.

### Package names

Current package names using `Assemblies` may be misleading because the packages contain more than assemblies.

Possible conceptual names considered:

* `7DtD.Reference.Game`
* `7DtD.Reference.DedicatedServer`

Potentially with OS represented somehow if separate packages are retained.

Do not adopt these mechanically; first decide whether OS belongs in package identity and whether separate packages are
the best representation.

### `push.cs` → `publish.cs`

Rationale: it owns publication/feed behavior rather than merely invoking `nuget push`.

### `release.cs` → `sync.cs` or `update_refs.cs`

Rationale: StrongMods itself is not being released. The operation is synchronizing its private 7DtD references with
Steam.

Again, terminology remains open for debate.

## Retention policy concern

Current publication behavior appears to delete superseded package builds according to version-based retention rules.

This creates a potentially undesirable ordering:

1. publish new reference package
2. remove an old package
3. the repo may still declare/use that old package
4. adoption has to happen promptly to restore consistency

Challenge whether deletion should happen during publication at all.

A safer alternative may be:

1. acquire/snapshot/package/publish
2. adopt
3. validate
4. separately garbage-collect packages that are demonstrably unreferenced

Potential retention criteria could include actual reachability from development/test declarations rather than
assumptions derived only from version numbering.

Investigate storage costs and GitHub Packages constraints before recommending a final policy.

## GitHub Packages "latest" concern

`push.cs` contains logic intended to make GitHub Packages' displayed "latest" version agree with the system's idea of
the newest package, including deletion/republication behavior.

Question whether this has any functional consumer.

If StrongMods and CI always restore exact pinned versions, GitHub's cosmetic/default "latest" concept may provide
little or no value.

Avoid introducing transient package unavailability merely to manipulate a UI label unless there is a concrete
requirement for it.

## Shared metadata vs independent validation

Several programs independently know facts such as:

* product → Steam App ID
* product → package ID
* product → installation/data layout

Consider centralizing stable configuration/facts so they do not drift.

However, preserve independent validation where duplication is deliberate defense-in-depth.

Useful design principle to evaluate:

Share configuration; independently verify invariants where independence improves safety.

## Safety orientation

StrongMods generally prefers tools that are safe to run and do not silently guess.

Apply that principle here:

* provenance should be explicit
* ambiguity should produce a clear error rather than silently selecting content
* historical import must not be mistaken for rollback
* a failed Steam query must not look like "no update"
* publication should not unnecessarily invalidate currently working builds
* automation must be deterministic enough for CI

## What to do next

Do not immediately implement the naming proposals.

First inspect the current repository and produce a revised design analysis addressing:

1. What is the actual domain model represented by these five tools?
2. Which concepts currently have multiple names?
3. Which names imply the wrong abstraction?
4. What assumptions about Product, OS, Version, Steam Branch, build ID, and package identity are embedded in the code?
5. Are Windows/Linux differences material enough to require separate reference artifacts?
6. What exact Steam identifier(s) are necessary for reproducible provenance?
7. How should historical-version import differ from routine current-version synchronization?
8. Is NuGet packaging appropriately separated from the domain model?
9. Can retention be made safer and independent of publication?
10. Can the GitHub "latest" machinery simply be removed?
11. What should the eventual conceptual workflow and CLI/tool boundaries be?

For each recommendation, distinguish:

* observed fact
* inference
* proposal
* tradeoff
* migration cost

Favor a smaller, clearer domain model over preserving current vocabulary merely because it exists.

Do not change code until the design has been reviewed.

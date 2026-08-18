# Quota-burn plan — 2026-08-18 (unattended, ~12:00–14:00 Eastern)

**Goal:** Spend the remaining Claude Max quota (expires ~14:00 Eastern) on the highest-impact unattended work.
**Ground rules from the owner:** no real engineering; prototyping, testing, research, and report writing are allowed.
One local-first HTML report per effort in `.ai/reports/`. New tickets only where a finding fits the three priority
categories. Nothing ships; the owner reviews and commits.

**Priority categories (owner's ranking):**

1. Reusable testing tools (StrongMods repo)
2. Reusable development tools (StrongMods repo)
3. Reusable Strongheart-specific functions for StrongCore

## Inputs reviewed

- Open GitHub issues (95 → 20 range scanned).
- `.ai/test-idea-tool-gap-seed.md` (#50 seed): ranked tool-gap inventory U1–U3, S1–S6, R1–R6, D1, F1.
- `.ai/overnight/INDEX.md`: the 2026-08-17 night run already built S1 (transpiler IL verification), S3
  (reference integrity), S4 (loclint), and the StrongUtils unit-test block (U1 partial). Suite 269 → 403 tests.

## Chosen efforts (6, run in parallel)

| # | Effort | Feeds | Category | Why now |
|---|--------|-------|----------|---------|
| 1 | `game-api-drift` — prototype S2: detect non-Harmony game-API references (direct calls/field refs, ModEvents) and resolve them against both declared units | #50 gap S2, extends `Tests/TargetResolver.cs` | testing | Broad drift class with zero coverage today; overnight `IlReader` makes it cheap; last big *static* gap after S1/S3/S4 landed |
| 2 | `patcher-conformance` — F1 ordering-seam tests (BreadthFirstXmlPatcher's headline guarantee) + S5 post-patch value assertions | #50 gaps F1, S5; #43 | testing | StrongMods' core guarantee is untested above the cache; S5 is the cheapest remaining gap; both extend the same harness |
| 3 | `case-sensitivity-devtool` — research + prototype #95: deliver the case-check as a dev-time tool | #95; context #89 #90 #91 | dev tools | Three open bugs show the runtime delivery is wrong; a dev-time linter sidesteps all three |
| 4 | `strongcore-inventory` — cross-mod survey of Strongheart-specific shared code + extraction design | #29; interacts #34 #35 | StrongCore | StrongCore does not exist yet; a grounded inventory + API sketch is the prerequisite for everything in category 3 |
| 5 | `deploy-shape-harness` — prototype #42/D1: drive `-t:Deploy` into `.scratch/` roots and assert overlay semantics | #42; #37 | testing | Guards the 2026-07-30 data-loss class; seed ranks it dominant for both overlays; fully testable without a live install |
| 6 | `prose-guards-sweep` — #57: enumerate infra behavior defended only by prose, verify each claim, rank for executable guards | #57; feeds #55 #70 | testing/dev | Pure research, read-only, produces the ranked backlog for future conformance tests |

## Skipped candidates, and why (trade-offs for review)

- **R1/R2 world-control harness (#66–#69, #71):** highest leverage per the seed, but it needs live headless server
  runs and long iteration loops — poor fit for a 2-hour unattended window with a hard quota cliff. Left for an
  attended session.
- **#63 auto-vendor:** touches licensed-file publishing paths and the network; too sensitive for unattended work.
- **#49 in-game runner, T2a client work:** same live-server objection as R1/R2.
- **Loclint bug follow-ups (PlayerSpawnedTraders, StrongUtils, AEC stray comma):** mod-content bugs, not in the three
  priority categories. The overnight report already drafted the ticket shapes; filing stays the owner's call.

## Execution mechanics and risk controls

- One Workflow, `pipeline(efforts, run, verify)` — each effort is one deep agent; a second agent verifies its report
  and claims. Only effort 2 may modify `Tests/`; all other prototypes go to `StrongDev/.ai/tools/` or `.scratch/`
  (avoids parallel-build lock contention on `Tests/obj`).
- Baseline `dotnet test StrongMods.sln -c Debug` runs before launch; I re-run it once after all agents finish (final
  gate, since Tests/ changes).
- Agents: no commits, no CONTEXT.md, no deploys except `-p:ModsDir=.scratch/...` redirects (repo-relative paths),
  writes only in repo scope, evidence-scoped claims, stop new work at 13:15 and finalize reports by 13:30.
- Reports follow the local-first contract: `.ai/reports/2026-08-18-<slug>.html`, self-contained, renders offline
  without JavaScript, hand-built diagrams only.
- Tickets: agents *propose*; the orchestrator files them centrally after re-reading
  `docs/agents/issue-tracker.md`, only for findings inside the three categories.

## Deliverables

1. Six HTML reports in `.ai/reports/` (dated 2026-08-18).
2. A summary linking all six, plus this plan doc.
3. New issues where justified, cited by number in the summary.
4. All prototypes on disk, uncommitted, for the owner's review.

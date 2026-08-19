# Submission-candidate verification — implementation plan

Tracks [#145](https://github.com/Strongheart-Games/StrongMods/issues/145). The broader question of isolating parallel
workstreams is deliberately deferred to [#146](https://github.com/Strongheart-Games/StrongMods/issues/146).

## Problem

A report listed three #144 files even though its claims and green validation depended on a fourth,
`Tests/Patcher/PatchApplicationTests.cs`. The human submitted the listed files; CI ran that exact candidate and found
the stale `ExpectedToLog` exception. A report currently describes a dirty working tree, not an independently verified
submission candidate.

## Outcome

For an agent-produced handoff, an explicit file set is overlaid onto a scratch worktree based at a named commit and
validated there. The report's **Submission candidate (verified)** section is generated from that same input and names:

- the resolved base SHA;
- every selected repo-relative file, including the report; and
- the requested validation command and exit result.

The word *candidate* means precisely "the set a human may submit", not all uncommitted work in the shared tree.

## Design

### Tool: `build/tools/verify-submission-candidate.cs`

The checked-in C# file-based tool accepts:

```text
dotnet run --file build/tools/verify-submission-candidate.cs --no-build -- \
  --base HEAD --report AutoCollectLoot/.ai/reports/example.html \
  --file AutoCollectLoot/Config/items.xml \
  --file Tests/AutoCollectLoot/InheritedLootContainerTests.cs \
  --file Tests/Patcher/PatchApplicationTests.cs \
  --file AutoCollectLoot/.ai/reports/example.html -- \
  dotnet build StrongMods.sln -c Debug --then \
  dotnet test Tests/Tests.csproj -c Debug --no-restore --no-build
```

It will resolve the base revision, reject duplicate/out-of-repo paths, create a unique `.scratch/` worktree from that
revision, overlay each named file from the current tree (or remove it if it is selected for deletion), run the command
sequence inside that worktree, and always remove the worktree. It exits nonzero on invalid input, setup failure, or
validation failure. The report is updated only after a successful validation, inside explicit HTML markers owned by the
tool.

The first real proof will run two candidates from the #144 base: the three-file historical set must fail with the stale
exception; adding `Tests/Patcher/PatchApplicationTests.cs` must pass. The tool's `--selftest` will cover parsing and
marker replacement without relying on a live repository.

### Skill: `verify-submission-candidate`

Add a project-local, model-invoked skill under `.agents/skills/verify-submission-candidate/`. Its description will
trigger only when an agent is preparing an uncommitted submission candidate or a human-review report with a candidate
file set. It will require the exact verifier before claiming a candidate is ready, and it will direct broad workstream
isolation questions to #146 rather than treating them as prerequisites.

### AGENTS pointer

Add one concise pointer beside the existing Handoff & Review rules. It names the skill and its trigger; the procedure
stays in the skill so ordinary tasks do not pay its context cost.

## Files and expected size

| File | Purpose |
|------|---------|
| `build/tools/verify-submission-candidate.cs` | Scratch-worktree verifier and generated report section |
| `.agents/skills/verify-submission-candidate/SKILL.md` | Agent workflow and narrow invocation trigger |
| `AGENTS.md` | One Handoff & Review pointer |
| `.ai/submission-candidate-verification-plan.md` | This implementation plan |
| Target HTML report | Tool-owned verified-candidate section during proof |

The tool plus its self-test is likely to exceed 100 lines of code, so this plan requires explicit approval before
implementation under the repository workflow. It will stay below the 250-line hard stop and does not implement #146.

## Verification

1. `dotnet clean build/tools/verify-submission-candidate.cs`, then
   `dotnet build build/tools/verify-submission-candidate.cs`, then
   `dotnet run --file build/tools/verify-submission-candidate.cs --no-build -- --selftest`.
2. Historical three-file #144 candidate: verifier exits nonzero and surfaces the stale expected-log failure.
3. Complete four-file #144 candidate: verifier exits zero.
4. `dotnet test StrongMods.sln -c Debug --no-restore` and `git diff --check`.

## Open design decision

The tool will regenerate an explicitly marked section of an HTML report rather than parse an arbitrary manual inventory.
That gives the report one source of truth while keeping report layout and analysis human-authored.

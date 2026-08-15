---
name: harvest-memory
description: >
  Review staged agent memories, promote durable claims into maintained sources, discard stale or duplicate claims, and
  preserve uncertain ones. Use when asked to harvest, promote, graduate, retire, or prune memory.
---

# Harvest memory

Treat auto-memory as a staging area, not a source of truth. Route claims rather than files, and retire a claim only
after its approved destination is maintained.

## Workflow

### 1. Bound the review

Read the memories the user selected, or the active project's current memory when no subset was named. Read only the
maintained sources needed to check those claims. Expand the search when a claim points elsewhere or conflicts with an
existing source.

Before proposing changes, identify the active harness's supported memory-update mechanism and the repository's rules
for destination files. Do not assume that another agent's paths, commands, or loaders apply.

Complete this step when every candidate claim has a source and its relevant maintained source has been checked.

### 2. Split and triage claims

Split each memory into atomic claims when its statements have different actions, destinations, or scopes. Separate an
underlying fact from its task-specific implications; route each independently and discard implications already obvious
from maintained sources. Assign one action:

| Action  | Use when                                                                          |
|---------|-----------------------------------------------------------------------------------|
| Promote | The claim is durable, useful, and not maintained elsewhere.                        |
| Discard | The claim is obsolete, incorrect, no longer useful, or already covered accurately. |
| Defer   | The claim is uncertain, weakly inferred, conflicted, or tied to an unsettled decision. |

An explicit user instruction may be durable after one occurrence. Require corroboration for an inferred preference.
Verify conflicts against the code or system where practical; defer unresolved conflicts and name the conflicting
source.

Complete this step when every atomic claim has exactly one action and supporting evidence.

### 3. Route promotions

Route each promotion to the narrowest loaded scope that reaches every task where it should affect behavior. Judge scope
by where the claim is useful, not where it was observed.

| Claim type                               | Destination                                                                                     |
|------------------------------------------|-------------------------------------------------------------------------------------------------|
| Project purpose, boundary, or term       | Recommend a human update to `CONTEXT.md`; describe the claim and evidence without drafting prose |
| Shared repository instruction            | The narrowest applicable `AGENTS.md`                                                            |
| Machine-specific across repositories     | The active harness's machine-wide instruction source                                            |
| Machine- and repository-specific         | The repository's local, gitignored instruction file                                             |
| Detailed knowledge or rationale          | The nearest relevant repository document                                                        |
| Reusable conditional workflow            | A skill                                                                                         |
| Mechanically enforceable rule            | Harness configuration                                                                           |
| Personal instruction                     | The active harness's personal instruction source, scoped as narrowly as practical                |

`CONTEXT.md` is human-authored and read-only to agents in this repository. Never edit it or draft text for the human to
paste into it.

Prefer an existing source over a new file. Put a rule in harness configuration only when that harness can enforce it;
do not also retain the same rule as prose.

### 4. Propose the harvest

Present the complete plan and stop for explicit approval:

| Claim | Memory source | Action | Destination | Intended change | Reason |
|-------|---------------|--------|-------------|-----------------|--------|

Include discards and deferrals. For a human-owned destination, make the intended change a concise recommendation, not
replacement wording.

Complete this step only when the user approves or revises every row.

### 5. Apply and retire

Apply only the approved rows:

- Integrate promoted claims where readers will look for them; do not append an unsorted memory section.
- Preserve the durable meaning while removing incidental dates and discovery stories.
- Write facts declaratively and instructions imperatively.
- Keep one source of truth for each claim.
- Respect filesystem scope and human-owned files.

Then use the active harness's supported memory-update mechanism to retire only claims that were promoted or approved for
discard. If a memory contains deferred claims, preserve those claims rather than deleting the whole file. Keep indexes
and links consistent when the memory system exposes them for maintenance.

Complete this step when every approved destination is updated and no retired claim remains as active memory.

### 6. Verify and report

Verify changed files and any loader or configuration behavior touched by the harvest. Report:

- promotions and their destinations
- discarded claims
- deferred claims and why they remain
- recommendations for human-owned or inaccessible destinations
- modified files and uncommitted changes

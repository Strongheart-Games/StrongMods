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

Honor the user's inclusions and exclusions. When no subset is named, inventory every active memory store associated
with the repository across all harnesses, not only the invoking harness. Exclude backups, archives, and inactive
snapshots. Use repository configuration, memory indexes, and harness-provided locations to discover stores; do not
assume that one harness's paths or loaders apply to another.

Record each candidate's harness, file, and section. Read only the maintained sources needed to check those claims;
expand the search when a claim points elsewhere or conflicts with an existing source. Identify each source store's
supported update mechanism and the repository's rules for destination files before proposing changes.

Complete this step when every included candidate has linkable memory evidence, every relevant maintained source has
been checked, and every source store has a known update mechanism or is marked inaccessible.

### 2. Split and triage claims

Split each memory into atomic claims when its statements have different meanings, actions, destinations, or scopes.
Preserve the source's exact meaning: keep claims with similar symptoms but different causes separate. Merge only
semantically equivalent claims, and retain links to every memory source on the merged claim. Separate an underlying
fact from its task-specific implications; route each independently and discard implications already obvious from
maintained sources. Assign a category and one action:

| Action  | Use when                                                                          |
|---------|-----------------------------------------------------------------------------------|
| Promote | The claim is durable, useful, and not maintained elsewhere.                        |
| Discard | The claim is obsolete, incorrect, no longer useful, or already covered accurately. |
| Defer   | The claim is uncertain, weakly inferred, conflicted, or tied to an unsettled decision. |

An explicit user instruction may be durable after one occurrence. Require corroboration for an inferred preference.
Verify conflicts against the code or system where practical; defer unresolved conflicts and name the conflicting
source.

Check repository status before treating a repository file as a maintained destination. Treat an untracked,
nonignored file, or relevant uncommitted changes to a tracked file, as in flight: surface the claim and destination,
recommend `Defer`, and leave both untouched unless the user overrides. Gitignored memory stores are staging sources,
not in-flight destinations; count an ignored destination as maintained only when the repository deliberately defines
it as a local source of truth.

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

Lead with a concise recommended disposition. List explicitly excluded memory sets before the plan without assigning
them actions. Present the complete plan and stop for explicit approval:

| # | Claim | Memory evidence | Category | Action | Destination | Intended change | Reason |
|---|-------|-----------------|----------|--------|-------------|-----------------|--------|

Use stable claim numbers throughout review; suffix a number when splitting it instead of renumbering later claims.
Link memory evidence to the precise file or section when the interface supports file links, and identify the harness
and topic in each link label. Include every source for a merged claim. Include discards and deferrals. For a human-owned
destination, make the intended change a concise recommendation, not replacement wording.

Complete this step only when the user approves or revises every row.

### 5. Apply and retire

Apply only the approved rows:

- Integrate promoted claims where readers will look for them; do not append an unsorted memory section.
- Preserve the durable meaning while removing incidental dates and discovery stories.
- Write facts declaratively and instructions imperatively.
- Keep one source of truth for each claim.
- Respect filesystem scope and human-owned files.

Then use each source store's supported update mechanism to retire only claims that were promoted or approved for
discard. If a source is inaccessible, report it and preserve its claim. If a memory contains deferred claims, preserve
those claims rather than deleting the whole file. Keep indexes and links consistent when the memory system exposes
them for maintenance.

Complete this step when every approved destination is updated and no retired claim remains as active memory.

### 6. Verify and report

Verify changed files and any loader or configuration behavior touched by the harvest. Report:

- promotions and their destinations
- discarded claims
- deferred claims and why they remain
- excluded memory sets
- recommendations for human-owned or inaccessible destinations
- modified files and uncommitted changes

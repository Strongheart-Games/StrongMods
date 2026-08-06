---
name: harvest-memory
description: >
  Review Claude Code auto-memory and move durable claims into the right source of truth:
  CONTEXT.md, AGENTS.md and its private project conventions, repo documentation, or a
  reusable skill. Use when asked to harvest, promote, graduate, retire, or prune memories.
---

# Harvest memories

Auto-memory is a staging area. Harvesting turns durable claims into maintained project
artifacts, removes duplicates and stale notes, and leaves uncertain claims in memory.

## Project conventions

Use these destinations consistently:

| Destination | Put here |
|---|---|
| `CONTEXT.md` | Stable, descriptive project context: purpose, boundaries, terminology, and major relationships |
| `AGENTS.md` | Team-shared instructions and rules that apply throughout the repository |
| `AGENTS.local.md` | Machine-specific project instructions or operational facts; gitignored |
| `AGENTS.personal.md` | Personal project instructions that should follow the user across machines; stored privately |
| Repo docs | Detailed explanations, reference material, design rationale, and task-specific knowledge |
| A skill | A reusable, multi-step workflow that should load only when relevant |

`AGENTS.local.md` and `AGENTS.personal.md` are project conventions, not filenames defined by
the AGENTS.md standard. Check `setup.md` before the first harvest and verify that every
destination is actually loaded by the relevant agent harness.

## Procedure

### 1. Locate memory and current sources

Find the active auto-memory directory rather than assuming its path. Read:

- `MEMORY.md` and every referenced memory file
- `CONTEXT.md`
- `AGENTS.md` and every instruction file it loads or points to
- relevant repo docs and skills
- private instruction files that are within the current filesystem scope

Do not route a claim until you know whether it is already codified.

### 2. Split memories into atomic claims

Route claims, not files. One memory may contain project context, a personal preference,
a machine path, and a proposed rule.

Split coupled statements when they have different destinations. For example:

- Context: `StrongDev is a support project, not a mod, modlet, or overlay.`
- Rule: `Treat named project-type lists as representative, not exhaustive.`

### 3. Triage each claim

Choose one action before choosing a destination:

| Condition | Action |
|---|---|
| Incorrect, obsolete, or no longer useful | Delete |
| Already covered accurately | Delete as a duplicate |
| Partially covered | Improve the existing source, then delete the memory |
| Tentative, inferred from weak evidence, or tied to an unsettled decision | Defer |
| Durable and useful | Promote |

An explicit user instruction may be durable after one occurrence. An inferred preference
usually needs repeated evidence. Do not turn uncertainty into doctrine merely to empty
the memory directory.

### 4. Classify the durable claim

Ask what kind of artifact the claim belongs in:

1. **Does it describe the project rather than direct the agent?**
   Put stable, always-relevant project facts in `CONTEXT.md`.

   Context answers questions such as:
   - What is this repository for?
   - What are its important boundaries and relationships?
   - What vocabulary does the project use?

2. **Does it direct or constrain the agent?**
   Route it by scope using the instruction table below.

3. **Is it a multi-step workflow used only for certain tasks?**
   Put it in a skill. Keep only the trigger or mandatory-use rule in `AGENTS.md`, when needed.

4. **Is it detailed knowledge, rationale, or reference material?**
   Put it in the nearest relevant repo document. Add a concise pointer from `AGENTS.md` or
   `CONTEXT.md` only when agents would not reliably discover it at the decision point.

Claims that do not fit these destinations may require manual handling. Note them in the
routing plan rather than expanding the skill with uncommon, agent-specific cases.

### 5. Scope instructions

| Scope | Destination |
|---|---|
| Anyone working in this repository | `AGENTS.md` |
| The user, in every repository | The user's global personal AGENTS content, loaded through each agent's supported configuration |
| The user, in this repository, on every machine | Private synced `AGENTS.personal.md` |
| This repository on this machine | Gitignored `AGENTS.local.md` |

Use the competent-collaborator test for `AGENTS.md`: would a capable contributor's work
be wrong, unsafe, or inconsistent without this instruction?

Do not put project description in `AGENTS.md` merely because agents need it. Put the
description in `CONTEXT.md` and keep only any necessary instruction to consult it.

### 6. Rewrite for the destination

- Preserve the claim; remove the dated incident that revealed it.
- Write context declaratively.
- Write instructions imperatively and concretely.
- Include a brief reason only when it prevents a plausible misreading.
- Keep always-loaded files concise. Move supporting detail to docs or a skill.
- Do not create two sources of truth.

### 7. Present the routing plan

Before editing, present this table and stop for explicit approval:

| Claim | Source | Action | Destination | Proposed text | Rationale |
|---|---|---|---|---|---|

Include deletions and deferrals, not only promotions.

### 8. Apply the approved plan

Apply one destination at a time so each change is reviewable:

1. `CONTEXT.md`
2. `AGENTS.md`
3. repo docs and skills
4. `AGENTS.local.md`
5. private files outside the workspace

Edit files outside the current filesystem scope only when access is explicitly available.
Otherwise, provide exact paste blocks. Do not weaken permission boundaries merely to make
the harvest automatic.


### 9. Retire harvested memory safely

1. Back up the active memory directory to a dated sibling location.
2. Delete only claims that were promoted, merged, or intentionally discarded.
3. Repair links from surviving memory files.
4. Update `MEMORY.md` in the same change.
5. Confirm the remaining memory reflects only deferred or still-useful observations.

### 10. Report and verify

Report:

- what moved and where
- what was deleted as stale or duplicate
- what was deferred and why
- what remains outside the agent's filesystem scope
- what files are left uncommitted

Verify that instruction loaders and imports work in a fresh session. For Claude Code, use
`/context` to inspect loaded memory files.

# Domain docs

**Layout: single-context.** One `CONTEXT.md` at the repo root, with durable decisions recorded as ADRs under
`docs/adr/` and active effort review documents under `.ai/reviews/`.

The repo is a monorepo of ~25 mods, but it is **wide and shallow** — most projects are a handful of files with no
vocabulary of their own, and `CONTEXT.md` is human-authored by design, so a per-project split would mean two dozen
documents the human must write and maintain. Single-context is the deliberate choice, not the default. The trigger for
revisiting it is written down below.

## Where the docs are

| Doc                                | Path                                     | Scope             | Who writes it                    |
|------------------------------------|------------------------------------------|-------------------|----------------------------------|
| Domain model / ubiquitous language | `CONTEXT.md` (repo root)                 | the whole repo    | **Human only**                   |
| Architecture Decision Records      | `docs/adr/NNNN-kebab-title.md`           | one decision      | Agents may draft; human approves |
| Active effort review document      | `.ai/reviews/YYYY-MM-DD-<effort>-review.html` | one piece of work | Agents                       |
| Working rules for agents           | `AGENTS.md` (repo root)                  | the whole repo    | Human-led; agents may propose    |
| Per-project detail                 | `<Project>/README.md`, `<Project>/Docs/` | one project       | Agents                           |

## Consumer rules

- **Read `CONTEXT.md` before making design decisions.** It carries the repo's purpose and vocabulary — the product
  matrix (Product / OS / Version), the project types (Modlet, Mod, Overlay), and the core projects (StrongDev,
  StrongMods, StrongCore) with their relationships and naming ambiguities. Use its terms verbatim; do not invent a
  parallel vocabulary.
- **`CONTEXT.md` is read-only to agents.** It is the human's own voice and judgment — the baseline the rest of the repo
  is measured against — so an agent editing it, *or drafting prose a human then pastes in*, destroys the one thing it
  exists to be. `.claude/settings.json` denies `Edit`/`Write` on it, but the rule is the fence, not the deny: never
  route around it with a shell command, a patch file, or a git operation. If something in it reads wrong, stale, or
  missing, say so in a sentence or file an issue — and leave the wording to the human.
- **This binds the `domain-modeling` skill too.** That skill normally maintains the domain model in `CONTEXT.md`; here
  it may only *propose* changes in chat or as an issue. ADRs are its writable surface.

## ADRs vs active review documents

Both explain *why*. They differ in what they are scoped to and how long they are meant to last.

|           | `.ai/reviews/*-review.html`               | `docs/adr/*.md`                                      |
|-----------|------------------------------------------|------------------------------------------------------|
| Unit      | one effort (a migration, a refactor)     | one decision                                         |
| Lifespan  | goes stale when the work lands           | outlives the work; that is the point                 |
| On change | edited or abandoned                      | never edited — superseded by a new ADR               |
| Size      | 100–550 lines                            | ~30–60 lines                                         |
| Named by  | date + effort (`2026-08-19-example-review.html`) | number + decision (`0007-vendor-game-assemblies.md`) |
| Cited as  | path + section, breaks on rename         | `ADR-0007`, stable forever                           |
| Contains  | options weighed, verification, handoff   | the decision and its consequences                    |

**Which one to write:**

> Would a new contributor need this to avoid re-litigating a settled choice? → **ADR**.
> Does it only make sense while the work is in flight? → **HTML review document**.

Most review documents produce zero to two ADRs. Start active efforts from
`docs/agents/review-document-template.html`; do not backfill existing effort documents wholesale. Reserve ADRs for
choices that are expensive to reverse or that keep getting re-asked; five or six a year is healthy. An ADR for every
small choice rots the same way a stub `CONTEXT.md` would.

When an ADR exists, the rule sites stop carrying the rationale inline and cite it instead — `AGENTS.md`, header comments
in `build/`, and issue threads should say "see ADR-0007" rather than restating the argument. One home per *why*.

### ADR format

`docs/adr/` does not exist yet; the first ADR creates it. Number sequentially from `0001`, never reuse a number.

```markdown
# NNNN. Short decision title

**Status:** Accepted            <!-- Proposed | Accepted | Superseded by ADR-NNNN -->
**Date:** YYYY-MM-DD
**Issue:** #NN                  <!-- optional -->

## Context

The forces in play — what made this a decision rather than an obvious call.

## Decision

What was chosen, stated in the present tense as a standing rule.

## Consequences

What this now costs, enables, or forbids. Include the bad parts.
```

**`Status` is the lifecycle of a decision, not of work.** `Proposed`, `Accepted`, `Superseded by ADR-NNNN` — never
"in progress" or an assignee. The backlog lives in GitHub Issues (`issue-tracker.md`); an ADR set that starts tracking
work becomes the second tracker `AGENTS.md` forbids.

**Never edit an accepted ADR.** When a decision changes, write a new ADR that supersedes it and mark the old one
`Superseded by ADR-NNNN`. The old file stays. That sequence is the whole value: it preserves why the rule changed, not
just what it is now.

## When to split into multiple contexts

Revisit single-context when a directory meets **both** tests:

1. It has at least one term that means something different inside it than outside it, and
2. It has enough code that an agent working there does not need the rest of the repo loaded.

Leaf mods fail both and should never get their own `CONTEXT.md` — they get `README.md` plus `Docs/`, which `AGENTS.md`
already mandates. The candidates that would plausibly pass, once the intended structure lands, are `build/` (game tree
vs install, units, load tiers, overlays, vendoring, the package feed), `StrongMods/` (breadth-first patching, load-order
visibility, `<foreach>` / `<bind>` / `<function>`), `StrongCore/` once it exists, and arguably `Tests/` (PatcherHost,
patch-target resolution, near-miss signatures).

If that split happens, the root gains a `CONTEXT-MAP.md` pointing at each per-context `CONTEXT.md`. Every one of those
files is human-authored under the same read-only rule — that cost is the reason to split late rather than early.

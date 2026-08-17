# Issue tracker

Issues for this repo live in **GitHub Issues** on
[Strongheart-Games/StrongMods](https://github.com/Strongheart-Games/StrongMods/issues), driven with the
[`gh`](https://cli.github.com/) CLI.

Skills that read or write issues (`to-tickets`, `triage`, `to-spec`, `qa`, `code-review`) use this file to decide where
work is tracked.

## Authentication

Agents authenticate as a dedicated **bot account**, configured per machine via `GH_CONFIG_DIR`. An unset variable
silently falls back to the human owner's credentials, so confirm the active identity before any write:

```bash
gh auth status
```

## Reading

```bash
gh issue list --limit 50
gh issue list --label "mod:StrongMods" --state open
gh issue view <number> --comments
```

## Writing

```bash
gh issue create --title "..." --body "..." --label "type:bug" --label "mod:BloodRain"
gh issue comment <number> --body "..."
gh issue close <number> --comment "..."
```

## Rules

- **Every issue carries a `type:` facet** — `type:bug`, `type:feature`, `type:build`, `type:tooling`, `type:docs`,
  `type:tech-debt`, `type:security`, `type:research`.
- **Plus a where facet** — `scope:repo-wide`, or `mod:<Name>` for a single mod.
- **An `area:` facet marks a cross-cutting effort** that is neither one mod nor the whole repo — currently
  `area:testing` only. It is additive: it sits alongside the `type:` and where facets, never instead of them.
- **Triage state is a further facet** — `triage:*`, described in `triage-labels.md`. It never replaces the others.
- **Every label carries a facet; there are no unfaceted labels.** GitHub's stock set (`bug`, `enhancement`,
  `documentation`, `question`, `invalid`, `duplicate`) was retired — each shadowed a faceted equivalent and none was
  ever applied to an issue — and stock `wontfix` was renamed to `triage:wontfix`. An unfaceted label appearing on the
  repo is a mistake, not a new convention.
- **Two reserved unfaceted names, currently absent by choice.** `good first issue` and `help wanted` are the one
  deliberate exception to the rule above, and are to be recreated *verbatim and unfaceted* if this repo ever starts
  courting outside contributions. GitHub recognises those exact strings: its documentation states that the
  `good first issue` label feeds an algorithm surfacing approachable issues to potential contributors, so a faceted
  rename would convert a platform feature into a private convention that does nothing. (Documented for
  `good first issue`; `help wanted` is conventionally treated the same, but that page does not confirm it.)
- **Priority is not a label.** Ranking lives on the Project board so there is only one ordering.
- **Labels are human-managed: apply existing labels only, never create one.** The bot's repo role is Triage, which can
  apply labels but not create them — `gh label create` fails with HTTP 403 regardless of the token's grants, so don't
  retry or troubleshoot it (issue #27 has the analysis). If a needed label doesn't exist, file the issue with the labels
  that do exist and ask the human to create the missing one. A new mod needs a new `mod:<Name>` label; ask for it as
  part of landing the mod.
- **Resolve by closing, never by deleting.** Deleting and transferring issues are blocked by permission deny rules, and
  the bot account lacks the admin rights to do either.
- **The backlog lives here, not in documents.** A plan doc under `.ai/` explains *why* — the design, the options
  weighed, the verification. The issue carries the work and its status. Never add a status or follow-on table to a doc;
  two trackers always drift.

## Wayfinding operations

The `wayfinder` skill charts a foggy effort as a **map** issue whose **tickets** are child issues. This section is only
the GitHub *expression* of those concepts — the skill itself supplies their meaning.

### Labels

`wayfinder:` is a further facet, additive like `area:` and `triage:`; it never replaces the `type:` facet or the where
facet. So a ticket carries three or more labels.

| Role   | Wayfinder label                                                                       |
|--------|---------------------------------------------------------------------------------------|
| Map    | `wayfinder:map`                                                                       |
| Ticket | one of `wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, `wayfinder:task` |

Like every label here, these are human-created — see *Rules*.

### Map and tickets

A ticket is a GitHub **sub-issue** of the map. `gh` addresses sub-issues by issue number, so no node id lookup is
needed:

```bash
gh issue create --parent 80 --title "Where does the reference tree live?" --body-file ticket.md \
  --label "wayfinder:grilling" --label "type:research" --label "scope:repo-wide"
```

To attach or detach a ticket that already exists:

```bash
gh issue edit 80 --add-sub-issue 81,82
```

```bash
gh issue edit 81 --remove-parent
```

### Blocking

Blocking uses GitHub's native issue dependencies, not a body convention, so the tracker's own UI shows what is
takeable. Wire the edges in a **second pass**, after the tickets have numbers:

```bash
gh issue edit 83 --add-blocked-by 81,82
```

`--add-blocking`, `--remove-blocked-by` and `--remove-blocking` are the counterparts.

### Claiming

The assignee **is** the claim. A session assigns the ticket to the dev driving the map before doing any work; an open,
unassigned ticket is unclaimed. Agent sessions assign the bot account:

```bash
gh issue edit 81 --add-assignee str0ngh34rt-bot
```

### The frontier

The frontier is the map's children that are open, unblocked and unclaimed. One query answers it:

```bash
gh issue list --state open --limit 100 --json number,title,parent,assignees,blockedBy --jq '
  .[]
  | select(.parent.number == 80)
  | select(.assignees | length == 0)
  | select([.blockedBy.nodes[] | select(.state == "OPEN")] | length == 0)
  | "\(.number)\t\(.title)"'
```

`blockedBy` lists blockers whatever their state, which is why the last clause keeps only tickets with no *open*
blocker. Each node carries `id`, `number`, `state` (`OPEN` or `CLOSED`), `title` and `url`.

### Recording a resolution

Post the answer as a comment, close the ticket, then edit the map body to add its line to *Decisions so far*:

```bash
gh issue close 81 --comment "$(cat resolution.md)"
```

The close-never-delete rule in *Rules* applies unchanged. A ticket ruled out of scope is also **closed** — with the
reason — and is indexed under the map's *Out of scope* section rather than *Decisions so far*.

## PRs as a request surface

**Off.** Pull requests are not treated as incoming requests to triage. Flip this to on if external PRs should enter the
same queue as issues.

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

## PRs as a request surface

**Off.** Pull requests are not treated as incoming requests to triage. Flip this to on if external PRs should enter the
same queue as issues.

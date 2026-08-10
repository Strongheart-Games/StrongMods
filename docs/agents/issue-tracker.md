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
  `type:tech-debt`, `type:security`.
- **Plus a where facet** — `scope:repo-wide`, or `mod:<Name>` for a single mod.
- **Triage state is a third facet** — `triage:*`, described in `triage-labels.md`. It never replaces the other two.
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

# Triage labels

The `triage` skill sorts issues into five canonical roles. This file maps each role to the label string this repo
actually uses.

Four of the five carry a **`triage:` facet**, matching the repo's existing `type:` / `mod:` / `scope:` convention, so
triage state reads as its own axis and does not crowd the top-level label namespace. `wontfix` is GitHub's stock label
and stays unfaceted — it already existed and already means exactly this.

| Role              | Label                    | Meaning                                             |
|-------------------|--------------------------|-----------------------------------------------------|
| `needs-triage`    | `triage:needs-triage`    | Awaiting triage; role and readiness not yet decided |
| `needs-info`      | `triage:needs-info`      | Blocked on more information before it can be worked |
| `ready-for-agent` | `triage:ready-for-agent` | Scoped well enough for an agent to pick up          |
| `ready-for-human` | `triage:ready-for-human` | Ready to work, but needs human judgement            |
| `wontfix`         | `wontfix`                | Will not be worked on                               |

## Rules

- **Apply existing labels only; never create one.** The bot's repo role is Triage, which can apply labels but not create
  them — `gh label create` fails with HTTP 403 regardless of the token's grants, so don't retry or troubleshoot it
  (issue #27 has the analysis). If one of the labels above is missing, apply the ones that exist and ask the human to
  create the rest.
- **Triage state is one axis, not a replacement for the others.** A triaged issue still carries its `type:` facet and
  its `scope:repo-wide` / `mod:<Name>` facet.
- **Exactly one `triage:` label at a time.** Moving an issue to a new state means removing the old label, not stacking a
  second one.
- **Priority is still not a label.** Ranking lives on the Project board; `triage:ready-for-agent` says an issue *can*
  be picked up, not that it should be picked up next.
- **Resolve by closing, never by deleting.** `triage:`-labelled issues are no exception.

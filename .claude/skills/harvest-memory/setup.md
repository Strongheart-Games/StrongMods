# One-time instruction layout

Run this once per repository, plus the agent-specific setup required on each machine. Verify the result before
harvesting memory.

## What is standard and what is convention

The AGENTS.md format defines repository instruction files named `AGENTS.md`, including nested files for narrower
directory scopes. It does not define universal personal, machine-local, or synced-private companion filenames.

This project uses these additional conventions:

- `CONTEXT.md`: shared project description and vocabulary
- `AGENTS.local.md`: private instructions or operational facts for one checkout or machine
- `AGENTS.personal.md`: private project instructions synced across the user's machines

Claude Code reads `CLAUDE.md` and `CLAUDE.local.md`, not `AGENTS.md` directly. Its CLAUDE files can import AGENTS files
with `@` syntax, so the CLAUDE files can remain loader-only.

## Repository layout

Tracked files:

```text
<repo>/
  AGENTS.md
  CONTEXT.md
  CLAUDE.md
```

`CLAUDE.md` contains only:

```markdown
@AGENTS.md
```

`AGENTS.md` contains the repository's instructions and rules. It should also include a concise instruction to consult
`CONTEXT.md` for project purpose, boundaries, and vocabulary unless another part of the harness already guarantees that
context is loaded.

Private files, created only when needed:

```text
<repo>/
  AGENTS.local.md
  CLAUDE.local.md
```

Add both to `.gitignore`:

```gitignore
AGENTS.local.md
CLAUDE.local.md
```

`CLAUDE.local.md` may contain:

```markdown
@AGENTS.local.md
@~/agents/StrongMods/AGENTS.personal.md
```

A project-level import outside the repository may require approval the first time Claude Code encounters it.

## Personal store

Use a private, synced location as the canonical store:

```text
~/agents/
  AGENTS.md
  StrongMods/
    AGENTS.personal.md
```

- `~/agents/AGENTS.md` contains the user's instructions for every project.
- `~/agents/StrongMods/AGENTS.personal.md` contains the user's private instructions for StrongMods on every machine.

The store may be a private git repository, dotfiles repository, or synced folder.

## Agent-specific global loaders

There is no universal `~/AGENTS.md` location defined by the AGENTS.md format. Each agent harness has its own global
configuration location.

For Claude Code, create `~/.claude/CLAUDE.md` containing:

```markdown
@~/agents/AGENTS.md
```

For Codex, the global AGENTS file is under `~/.codex/`. Link or copy the canonical file to:

```text
~/.codex/AGENTS.md
```

Other agents need their own supported loader, symlink, or configuration. Keep the canonical content in the private store
and make the adapter agent-specific.

`AGENTS.personal.md` is this project's convention, not an official counterpart to
`CLAUDE.local.md`. Likewise, Codex's `AGENTS.override.md` is a Codex-specific override mechanism, not a portable
personal-file standard.

## Verify

Start a fresh agent session and verify every expected source:

1. Put a unique temporary sentinel in each new content file.
2. Confirm the agent reports each sentinel or lists the file as loaded.
3. For Claude Code, run `/context`.
4. Confirm local files are ignored with `git check-ignore`.
5. Remove the sentinels.

Do not assume a missing import fails loudly. A destination is usable only after its loader has been verified.

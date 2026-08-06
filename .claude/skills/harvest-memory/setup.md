# One-time instruction layout

Run this once per repository, plus the agent-specific setup required on each machine. Verify the result before
harvesting memory.

## What is standard and what is convention

The AGENTS.md format defines repository instruction files named `AGENTS.md`, including nested files for narrower
directory scopes. It does not define universal personal, machine-local, or synced-private companion filenames.

This project uses these additional conventions:

- `CONTEXT.md`: shared project description and vocabulary
- `AGENTS.local.md`: private instructions or operational facts for one checkout or machine
- `AGENTS.personal.md`: the in-repository name for a personal project file that lives in the private store, needed only
  by agents that cannot import a file from outside the repository

Files in the private store keep the standard `AGENTS.md` name. A distinguishing suffix is needed only where a file is a
sibling of another AGENTS file, which is true in the repository root and false in the store.

Claude Code reads `CLAUDE.md` and `CLAUDE.local.md`, not `AGENTS.md` directly. Its CLAUDE files can import AGENTS files
with `@` syntax, so the CLAUDE files can remain loader-only.

## Repository layout

Tracked files:

```
<repo>/
  AGENTS.md
  CONTEXT.md
  CLAUDE.md
```

`CLAUDE.md` contains only:

```
@AGENTS.md
```

`AGENTS.md` contains the repository's instructions and rules. It should also include a concise instruction to consult
`CONTEXT.md` for project purpose, boundaries, and vocabulary unless another part of the harness already guarantees that
context is loaded.

Private files, created only when needed:

```
<repo>/
  AGENTS.local.md
  CLAUDE.local.md
  AGENTS.personal.md   # only for agents that cannot import from outside the repository
```

Add each to `.gitignore` before creating it:

```gitignore
AGENTS.local.md
CLAUDE.local.md
AGENTS.personal.md
```

`CLAUDE.local.md` may contain:

```
@AGENTS.local.md
@~/agents/StrongMods/AGENTS.md
```

A project-level import outside the repository may require approval the first time Claude Code encounters it.

## Personal store

Use a private, synced location as the canonical store:

```
~/agents/
  AGENTS.md
  StrongMods/
    AGENTS.md
```

- `~/agents/AGENTS.md` contains the user's instructions for every project.
- `~/agents/StrongMods/AGENTS.md` contains the user's private instructions for StrongMods on every machine.

Both use the standard filename. The directory encodes the scope, and neither file shares a directory with another AGENTS
file, so neither needs a distinguishing suffix.

The store may be a private git repository, dotfiles repository, or synced folder.

## Agent-specific global loaders

There is no universal `~/AGENTS.md` location defined by the AGENTS.md format. Each agent harness has its own global
configuration location.

For Claude Code, create `~/.claude/CLAUDE.md` containing:

```
@~/agents/AGENTS.md
```

For Codex, the global AGENTS file is under `~/.codex/`. Link or copy the canonical file to:

```
~/.codex/AGENTS.md
```

Other agents need their own supported loader, symlink, or configuration. Keep the canonical content in the private store
and make the adapter agent-specific.

An agent that cannot import a file from outside the repository needs the store file placed inside it instead — a symlink
or a copy at `<repo>/AGENTS.personal.md`, which must differ from `AGENTS.md` because the two are siblings. Nothing loads
that file by itself: the AGENTS.md standard defines no import syntax, so the loader is a prose instruction added to the
repository's `AGENTS.md` as part of first use:

> If `AGENTS.personal.md` exists beside this file, read it too: it holds one user's personal project instructions and
> takes precedence over this file where they conflict.

The instruction is harmless on checkouts where the file does not exist; an agent that already loads the store through
its own mechanism will see the same content twice, which wastes context but conflicts with nothing. Prefer the symlink
over a copy — a copy drifts from the store silently (on Windows, symlinks require Developer Mode or administrator
rights) — and re-run the sentinel verification below after changing the store file. The name is this project's
convention, not an official counterpart to `CLAUDE.local.md`. Codex's
`AGENTS.override.md` is a Codex-specific override mechanism rather than a portable personal-file standard; verify it
before relying on it.

## Verify

Start a fresh agent session and verify every expected source:

1. Put a unique temporary sentinel in each new content file.
2. Confirm the agent reports each sentinel or lists the file as loaded.
3. For Claude Code, run `/context`.
4. Confirm local files are ignored with `git check-ignore`.
5. Remove the sentinels.

Do not assume a missing import fails loudly. A destination is usable only after its loader has been verified.

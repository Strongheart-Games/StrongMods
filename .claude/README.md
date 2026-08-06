# Claude Code Setup

This directory contains the shared project boundaries for Claude Code.

## Prerequisites: Bash

This repo's docs and agent instructions are POSIX shell throughout, so agents need the Bash tool rather than the
PowerShell tool. The tracked `settings.json` sets `CLAUDE_CODE_USE_POWERSHELL_TOOL=0` to that end.

**Windows users:** install [Git for Windows](https://git-scm.com/downloads/win). Claude Code uses Git Bash for the
Bash tool when it can find it, and the PowerShell tool when it can't. If Git Bash is installed but not found, set
`CLAUDE_CODE_GIT_BASH_PATH` to its path in your `settings.local.json`. Both variables are documented under
[Set up on Windows](https://code.claude.com/docs/en/setup#set-up-on-windows).

## Machine-specific settings, including game install paths

The tracked `settings.json` is deliberately environment-neutral: git and `gh` permission rules plus the settings-lint
hook, and nothing that names a path on any particular machine. Everything path-specific belongs in the gitignored
`settings.local.json` beside it — settings merge, so local entries add to (but cannot remove) the tracked ones.

Neither file in this directory grants access to anything outside the repository, so each machine declares its own:

- **`permissions.additionalDirectories`** for the game install, the dedicated server, and any save tree the
  `Deploy` target writes into. The default Steam location is `C:\Program Files (x86)\Steam\steamapps\common\...`, but
  nothing in the repo assumes it.
- **Environment** the agent needs per machine, such as `GH_CONFIG_DIR` for the bot account.

`AGENTS.md`'s *Filesystem Scope* rule — never hand-edit inside the game install; the `Deploy` target is the only way
in — is prose, enforced by convention. To have the harness enforce it instead, add an `Edit` deny rule on the same paths
here.

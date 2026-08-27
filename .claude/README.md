# Claude Code Setup

This directory contains the project-wide settings for Claude Code.

Skills live in `.agents/skills`; `.claude/skills` is deliberately untracked so a local link cannot block Git worktrees
on Windows.

## Optional Claude skills setup

Create the local link only when using Claude Code. From the repository root:

```powershell
cmd /c mklink /J .claude\skills .agents\skills
```

On Unix:

```bash
ln -s .agents/skills .claude/skills
```

Remove only the local link before changing its target. Do not commit it.

# Claude Code Setup

This directory contains the shared project boundaries for Claude Code.

## Prerequisites: Bash
This configuration relies on `bash` being the default shell.

**Windows Users:**
Do not use PowerShell. You must update your global config (`~/.claude/settings.json`) to route Claude through bash. Add the following to your global settings:

```json
{
  "shellPath": "C:\\Program Files\\Git\\bin\\bash.exe",
  "defaultShell": "bash"
}
```

## Non-default game install paths

The tracked `settings.json` grants access to the game and dedicated server at the default Steam install location
(`C:\Program Files (x86)\Steam\steamapps\common\...`), both in `permissions.additionalDirectories` and in the
`Edit` deny rule protecting those directories. If your installs live elsewhere, add the real paths to both lists
in your gitignored `settings.local.json` — settings merge, so local entries add to (but cannot remove) the
tracked ones.

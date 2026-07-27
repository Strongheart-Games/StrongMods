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

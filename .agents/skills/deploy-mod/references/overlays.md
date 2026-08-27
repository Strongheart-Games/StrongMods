# Deploy an overlay

Read the target `.csproj` and the headers of `build/Overlay.props` and `build/Overlay.targets` before deploying or
changing an overlay.

An overlay writes into a directory that the repository does not fully manage. It imports `build/Overlay.props` before
its declarations and `build/Overlay.targets` after them. `DeployRoot` must sit between those imports because it normally
uses shared path properties that the props import defines.

## Deployment scopes

- Content outside `MirrorOnDeploy` scopes is protective-additive. Deployment copies a file only when it is absent or
  the staged file is newer. It never deletes those files.
- Each `MirrorOnDeploy` entry is source-authoritative. Deployment overwrites changed files and deletes stale files only
  within that declared scope.
- `MirrorOnDeploy` entries identify literal files or directories. Do not use wildcards.
- `OverlayContentExclude` removes repository content from staging when that content does not belong at `DeployRoot`.

Before a live deployment, name every `MirrorOnDeploy` scope and confirm that deletion is acceptable within it. Treat
an empty or unexpectedly broad scope as an error.

## Redirected verification

When `DeployRoot` derives from `$(ModsDir)`, redirect it with:

```powershell
-p:ModsDir=.scratch\deploy
```

When `DeployRoot` derives from `$(SdtdSavesDir)`, redirect it with:

```powershell
-p:SdtdSavesDir=.scratch\deploy-saves
```

Pass both properties when deploying the whole solution into scratch. After a redirected deployment, verify that files
outside mirror scopes survived and that stale files were removed only inside mirror scopes.

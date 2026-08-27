---
name: deploy-mod
description: >
  Deploy StrongMods code mods, XML-only modlets, or overlays to a live or redirected destination. Use when asked to
  install or deploy, test deployment in scratch, configure deploy destinations or load order, or troubleshoot Deploy
  and install-version checks. Do not use for plain builds.
---

# Deploy a mod

Use the `Deploy` target for installation. Do not copy staged files into game directories by hand. Read the target
project and the header of its deployment target before changing deployment configuration.

## Safety boundary

- A plain build writes only to `bin/` and `obj/`. It does not authorize deployment.
- Run `Deploy` only when the user asks to install or deploy. If the request is to test or verify deployment behavior,
  redirect it to `.scratch/`.
- Prefer one-project deployment. Deploy the whole solution only when the user asks to install every deployable project.
- `Clean` removes staging output only. Removing an installed mod is a separate manual operation.
- `Deploy` can overwrite files and remove stale files within source-authoritative scopes. Resolve those scopes before
  running it.

## Preflight

Identify the project shape from its imports:

- `build/Mod.targets`: code mod
- `build/Modlet.targets`: XML-only modlet
- `build/Overlay.targets`: overlay; read [references/overlays.md](references/overlays.md) completely

Resolve the relevant properties without running a target:

```powershell
dotnet msbuild <project> `
  -getProperty:ModsDir `
  -getProperty:ModDeployName `
  -getProperty:DeployRoot `
  -getProperty:IsDeployable `
  -getProperty:SdtdTestVersions
```

For a mod or modlet, the destination is `$(ModsDir)\$(ModDeployName)`. Deployment mirrors the complete staged folder,
so staged content and file existence are authoritative. A stale deployed file is deleted.

Stop if `IsDeployable` is `false`. Do not override the project capability unless the user asks to change it.

## Choose the destination

- The default `$(ModsDir)` belongs to the client installation.
- `<ModsDir>$(SdtdServerDir)\Mods</ModsDir>` in a project targets the dedicated server.
- `-p:ModsDir=<path>` redirects a mod or modlet for one invocation. A relative path resolves from the directory where
  MSBuild was started.
- `Local.props` holds persistent machine-specific paths. Use absolute paths there. Precedence is `-p:` command-line
  properties, `Local.props`, `SDTD_HOME`, then repository defaults.
- `-p:SdtdDir=...` changes the compile and test tree. It never redirects deployment.

For a safe deployment test from the repository root:

```powershell
dotnet build <project> -c Debug -t:Deploy -p:ModsDir=.scratch\deploy
```

Inspect the resolved destination again when command-line properties change it.

## Configure load order

Prefer `ModLoadTier`:

- `First`
- `AfterDependencies`
- `Last`
- `LocalConfig`

Use `ModLoadPrefix` only when no tier expresses the required order. Never set both properties. The literal prefix map
and verified culture-aware sort behavior live in `build/Deploy.targets`.

## Check the destination version

For a live installation, `Deploy` hashes its `Assembly-CSharp.dll` and compares it with the project's declared
`SdtdTestVersions` trees. The destination layout determines whether client or dedicated-server trees are checked.
A redirected destination without a game assembly is not treated as a live installation and skips this check.

If comparison trees are missing, use `manage-game-assemblies` to restore them. If the destination matches no declared
version, verify and adopt support for that version instead of bypassing the check.

Use `-p:SdtdSkipInstallVersionCheck=true` only after the user explicitly accepts the risk of installing onto an
unverified game version. Explain the mismatch before requesting that decision.

## Deploy

Deploy one project:

```powershell
dotnet build <project> -c Debug -t:Deploy
```

Deploy every deployable project:

```powershell
dotnet build StrongMods.sln -c Debug -t:Deploy
```

Use `-c Release` only when the user requests a release deployment.

Deployment is complete when the command succeeds, the reported destination is the intended destination, version
verification passed or has an explicitly approved bypass, and the destination reflects the project's mirror or overlay
semantics. Report the destination and any removed stale files.

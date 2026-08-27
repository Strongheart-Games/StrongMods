---
name: new-mod
description: >
  Scaffold a new 7 Days to Die code mod or XML-only modlet from the repository templates and add it to StrongMods.sln.
  Use when asked to create or add either project type.
---

# Add a new mod or modlet

From the repository root, confirm that the matching template is registered:

```powershell
dotnet new list 7dtdmod
dotnet new list 7dtdmodlet
```

If the needed template is missing, ask before changing the user's .NET template registry. After approval, install only
the needed repository-local template:

```powershell
dotnet new install .\Template7DtDMod
dotnet new install .\Template7DtDModlet
```

Run the command for the chosen project shape:

```powershell
dotnet new 7dtdmod --name <Name> --output <Name>
dotnet new 7dtdmodlet --name <Name> --output <Name>
```

Create the project directly below the repository root. Its imports use `..\build\...` and depend on that depth.

Keep the generated project file minimal. The templates already import the shared build files and declare shippable
content. Let the existing SDK and content globs discover files. Add only genuine deviations, such as `ModLoadTier`,
`ModsDir`, or `GameAssembly`, in the positions defined by the shared build files.

The source templates contain `<IsDeployable>false</IsDeployable>` inside `<!--#if (IsTemplate) -->`. `dotnet new`
removes that block. Confirm that the generated project does not contain it so the new mod can deploy normally.

For a code mod, keep disposable or unfinished `.cs` files outside the project directory. Every `.cs` file there is
compiled automatically, except files under `.ai\**`.

Finish the integration:

1. Replace template text in `ModInfo.xml` and `README.md` with the new project's actual purpose.
2. Add the project to the root `README.md` project list.
3. Run `dotnet sln StrongMods.sln add <Name>\<Name>.csproj`.
4. Build the new project and run any additional verification required by `AGENTS.md`.
5. Confirm that the `mod:<Name>` issue label exists. Only a human can create a missing label, so ask the user to
   create it before the mod lands.

The work is complete when the generated project matches its chosen shape, appears in `StrongMods.sln` and the root
`README.md`, passes the required verification, and has its issue label or a pending request for that label.

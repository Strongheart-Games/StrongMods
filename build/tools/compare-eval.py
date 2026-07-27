"""Prove an MSBuild project-file change is a no-op, without building anything.

NOT imported by MSBuild. This is a developer tool that happens to live next to the shared build files
because the build system is what it inspects; nothing in build\\*.props or build\\*.targets references it.

MSBuild's -getProperty/-getItem flags *evaluate* a project and print the result as JSON without running any
target -- no compile, no file copy, nothing written to the game install. Diffing the evaluation of a project
before and after a csproj edit is therefore a free, side-effect-free regression check.

Typical use, comparing the working tree against a pristine checkout:

    MSB='<Rider>/tools/MSBuild/Current/Bin/MSBuild.exe'
    PROPS=OutputPath,OutDir,TargetDir,TargetPath,LangVersion,DefineConstants,AssemblyName,RootNamespace,\\
TargetFrameworkVersion,OutputType,DebugType,Optimize,DebugSymbols,PlatformTarget,WarningLevel,ErrorReport,\\
FileAlignment,AppDesignerFolder
    ITEMS=Reference,Compile,Content,None,ProjectReference

    git worktree add --detach /tmp/baseline HEAD
    "$MSB" /tmp/baseline/Foo/Foo.csproj -nologo -p:Configuration=Debug "-getProperty:$PROPS" "-getItem:$ITEMS" > b.json
    "$MSB" Foo/Foo.csproj              -nologo -p:Configuration=Debug "-getProperty:$PROPS" "-getItem:$ITEMS" > a.json
    python build/tools/compare-eval.py b.json a.json Foo

Two things worth knowing, both learned the hard way:

  * Always include OutDir/TargetDir, not just OutputPath. Microsoft.Common.CurrentVersion.targets derives them
    from OutputPath *during evaluation*, so an OutputPath set too late reads back correct while OutDir stays
    latched at the bin\\$(Configuration)\\ fallback.
  * Evaluation does not run Roslyn. A clean diff means the compiler's inputs are unchanged; it does not prove
    the project still compiles. Follow up with one real build.

Exit code is 0 when the two evaluations match, 1 when they differ, so it can gate a script.
"""
import json
import sys

# Metadata that actually affects the build. Everything else MSBuild synthesises is location-derived
# (FullPath, Directory, RootDir, RelativeDir, DefiningProject*, timestamps) and necessarily differs
# between the repo and a git worktree holding the baseline, so comparing it is pure noise.
MEANINGFUL = ("HintPath", "Private", "CopyToOutputDirectory", "Link", "Project", "Name",
              "CopyLocalSatelliteAssemblies", "SpecificVersion", "Aliases", "SubType", "DependentUpon")


def load(path):
    with open(path, encoding="utf-8-sig") as fh:
        return json.load(fh)


def item_key(item):
    # Identity plus build-affecting metadata: catches a changed HintPath/Private/CopyToOutputDirectory,
    # and cannot cancel out if a HintPath moves between two items.
    return (item.get("Identity", ""), tuple((k, item[k]) for k in MEANINGFUL if k in item))


def describe(item):
    ident, meta = item_key(item)
    extras = " ".join(f"{k}={v}" for k, v in meta if v)
    return ident + (f"  [{extras}]" if extras else "")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    before, after = load(sys.argv[1]), load(sys.argv[2])
    label = sys.argv[3] if len(sys.argv) > 3 else ""
    diffs = []

    bp, ap = before.get("Properties", {}), after.get("Properties", {})
    for name in sorted(set(bp) | set(ap)):
        if bp.get(name) != ap.get(name):
            diffs.append(f"    PROP {name}: {bp.get(name)!r} -> {ap.get(name)!r}")

    bi, ai = before.get("Items", {}), after.get("Items", {})
    for kind in sorted(set(bi) | set(ai)):
        b = {item_key(i): i for i in bi.get(kind, [])}
        a = {item_key(i): i for i in ai.get(kind, [])}
        diffs += [f"    {kind} BEFORE-ONLY {describe(b[k])}" for k in b if k not in a]
        diffs += [f"    {kind} AFTER-ONLY  {describe(a[k])}" for k in a if k not in b]

    counts = {k: (len(bi.get(k, [])), len(ai.get(k, []))) for k in sorted(set(bi) | set(ai))}
    summary = ", ".join(f"{k} {b}->{a}" for k, (b, a) in counts.items())
    if diffs:
        print(f"{label:<22} {len(diffs)} diff(s)   [{summary}]")
        print("\n".join(diffs))
        return 1
    print(f"{label:<22} IDENTICAL   [{summary}]")
    return 0


sys.exit(main())

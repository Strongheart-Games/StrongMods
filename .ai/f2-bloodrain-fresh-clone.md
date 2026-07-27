# Plan: F2 — `BloodRain` cannot be built from a fresh clone

| Field | Value |
| --- | --- |
| Status | ✅ **approach agreed** (2026-07-27) — nothing implemented yet |
| Parent | `.ai/build-refactor-plan.md` §0, follow-on **F2** |
| Scope | `BloodRain` only, plus repo-root docs/config. No C# changes. No other project touched. |
| Approach | **Option 2** — convert `BloodRain` to `PackageReference`, keeping the non-SDK csproj. Option 1 (vendor the DLL) **ruled out**: repo policy is no binaries in git. |
| Sequencing | Ships **before** F1, as F1's de-risking step rather than a standalone patch. See §8. |
| Size estimate | ~12 changed lines of build config + ~25 lines of docs. Well under the 100-line target. |

## 1. The defect, precisely

`BloodRain` is the only project in the repo with a real NuGet dependency (`Cronos`, used by `BloodRain.cs` for
`CronExpression.Parse`). Three facts combine into a broken clone:

1. `BloodRain/packages.config` is **tracked** (`git ls-files` confirms) and requests `Cronos 0.11.0`.
2. `BloodRain.csproj` resolves it through a literal path —
   `<HintPath>..\packages\Cronos.0.11.0\lib\net45\Cronos.dll</HintPath>` — with **no `<Private>` element**, so
   copy-local is on and both `Cronos.dll` and `Cronos.xml` deploy into the mod folder. Confirmed in the live install:
   `Mods\BloodRain\` holds `Cronos.dll` (50,424 bytes) and `Cronos.xml` (10,861 bytes).
3. `packages/` is **ignored** by `.gitignore:21` (`[Pp]ackages/`), and `git ls-files packages` returns nothing.

So after `git clone`, `..\packages\` does not exist. The failure is also *badly signposted*: an unresolvable
`<Reference>` is MSBuild warning **MSB3245**, not an error, so the build continues and dies in a wall of `CS0246`
errors about the `Cronos` namespace. The one actionable line is a warning scrolled off the top.

There is no `nuget.config` anywhere in the repo, and because this is `packages.config` rather than `PackageReference`,
restoring it needs **`nuget.exe`** — a tool that ships with neither the .NET SDK nor MSBuild and must be downloaded by
hand. *That* is the actual defect: the repo depends on a tool no standard toolchain provides.

## 2. Constraints this plan must satisfy

**Neutrality (stated by the repo owner, this session).** Anything committed to git must be:

- **IDE-neutral** — no assumption that Visual Studio, Rider, or any other IDE is installed.
- **OS-neutral** — no path or command form that only works on one platform beyond what the repo already requires.
- **Development-style-neutral** — CLI-only, IDE-only, and CI workflows must all work.

Tracked files may *tell* a developer what to configure, and may say "if you use Rider, …" — never "install Rider and
then …". IDE-specific tooling is fine for local, untracked convenience only.

**From CLAUDE.md.** Single logical focus; ≤250 changed lines hard stop; no commits or history rewriting; do not touch
the `StrongMods` core project; verification is by compilation since there is no test suite.

**From the parent plan.** F1 (SDK-style migration) stays deferred. Whatever F2 does must not be thrown away by F1, and
must not quietly preempt it.

## 3. Options considered

| # | Option | IDE-neutral | OS-neutral | Style-neutral | Verdict |
| --- | --- | --- | --- | --- | --- |
| 1 | **Vendor `Cronos.dll` into a tracked folder** (`BloodRain/lib/`), point `HintPath` at it, delete `packages.config` | ✅ | ✅ | ✅ — no restore, no tool, no network | **Rejected** (2026-07-27) — perfect neutrality, but repo policy is no binaries in git |
| 2 | **Convert to `PackageReference`**, keep the non-SDK csproj | ✅ | ✅ | ✅ — restore exists in every IDE, in MSBuild, and in the .NET SDK | **Chosen** |
| 3 | Keep `packages.config`, add `nuget.config`, document `nuget.exe restore` | ✅ | ⚠️ | ❌ — still requires a hand-downloaded `nuget.exe` | Rejected — leaves the actual defect in place |
| 4 | Migrate `BloodRain` alone to SDK-style (an early slice of F1) | ✅ | ✅ | ✅ | Rejected for now — see below |
| 5 | Drop the dependency, hand-roll cron parsing | ✅ | ✅ | ✅ | Rejected — DST/time-zone correctness is the whole reason Cronos is here |

**Why not option 4.** It gets no F2 benefit that option 2 does not already get, while entangling a live defect with a
refactor that needs a different and more expensive verification method. Option 2 is a strict **subset** of F1 — SDK-style
projects use `PackageReference` natively — so it survives F1 unchanged. The full sequencing argument, including why
`BloodRain` is a poor choice of pilot for F1, is in §8.

**A correction to the F2 note as written**, recorded so it is not re-proposed later. The parent plan suggests
"committing the ~30 KB DLL via a `.gitignore` negation" as the stopgap. That specific mechanism does not work: git
does not descend into an excluded directory, so a bare `!packages/Cronos.0.11.0/lib/net45/Cronos.dll` re-include is
inert while `[Pp]ackages/` excludes the parent. It would need a chain of `!dir/` + `dir/*` rules per level. Vendoring
under a path that was never ignored — `BloodRain/lib/` — avoids the problem entirely, which is how option 1 above is
phrased. (The size is also 50 KB for the DLL, plus 11 KB if `Cronos.xml` is kept, not ~30 KB.) Moot either way now
that option 1 is rejected, but the `.gitignore` claim was wrong and should not survive in the parent document.

### The trade option 2 actually makes

Today a fresh clone is **broken**. Option 2 makes it **work, but need network access once** (or a warm NuGet cache).
It is not trading working-offline for needing-a-network — it is trading broken for working. The only case option 2
does not serve is a developer who must build with no network at all and no NuGet cache; with option 1 ruled out, that
case is accepted as unsupported.

`Cronos 0.11.0` is **MIT**-licensed with **zero package dependencies** in its `.NETFramework4.5` group (confirmed from
the nuspec), so neither option drags in a transitive graph and both are redistributable.

## 4. Design — option 2 in detail

### 4.1 `BloodRain/BloodRain.csproj`

Replace the explicit `<Reference>` + `<None Include="packages.config" />` group with a package reference, and set the
restore style. Both go **in the project body**, above the `..\build\Mod.targets` import, so no other project in the
repo becomes restore-aware:

```xml
<PropertyGroup>
  <ProjectGuid>{031D66E3-00A6-4AAD-85B9-7F26BFC9EFAF}</ProjectGuid>
  <!-- The one real NuGet dependency in the repo. PackageReference (not packages.config) so any standard
       toolchain can restore it: `msbuild -restore`, `dotnet restore`, or an IDE's restore-on-load. -->
  <RestoreProjectStyle>PackageReference</RestoreProjectStyle>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Cronos" Version="0.11.0" />
</ItemGroup>
```

`BloodRain/packages.config` is deleted (`git rm`).

### 4.2 Why the import sandwich is undisturbed

The parent refactor's central constraint is that import *position* is load-bearing. `PackageReference` inserts two
generated files, and both land clear of the sandwich:

| Generated file | Imported by | Position relative to the sandwich |
| --- | --- | --- |
| `obj\BloodRain.csproj.nuget.g.props` | `Microsoft.Common.props` (`ImportProjectExtensionProps`) | **before** `..\build\Mod.props` |
| `obj\BloodRain.csproj.nuget.g.targets` | `Microsoft.Common.targets`, reached via `Microsoft.CSharp.targets` | **after** `..\build\Mod.targets` |

Nothing in `Mod.props` or `Mod.targets` reads package assets, and nothing NuGet generates sets `OutputPath`. So
`OutDir`/`TargetDir`/`TargetPath` — the values that latch during evaluation and that the parent plan insists on
checking — should be untouched. **This is an assertion to verify (V3), not an assumption to ship.**

`NuGetTargetMoniker` is derived from `TargetFrameworkVersion`, which `Mod.props` sets to `v4.8.1` before restore
evaluates the project, so restore should target `.NETFramework,Version=v4.8.1` (V2).

`obj/` is already covered by `.gitignore:29` (`[Oo]bj/`), so the generated files are never committed.

### 4.3 Repo-root `nuget.config` — recommended, but separable

A minimal root `nuget.config` that clears inherited feeds and declares nuget.org:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

This is a **neutrality gain**: without it, restore depends on whatever machine-level feed configuration a developer
happens to have, so a machine configured for a private feed only would fail. With it, restore behaves the same
everywhere. It is called out as separable because it is the one piece with a real downside — a developer behind a
mandatory corporate proxy feed would need to override it — and it can be dropped without affecting anything else in
this plan.

### 4.4 `.gitignore`

No change required. `[Pp]ackages/` stops mattering once nothing resolves through `..\packages\`. Developers who
already have a populated `packages/` from the old mechanism can delete it; that is a local cleanup, not a repo change,
and is explicitly out of scope here.

## 5. Plan of attack

### Phase 0 — Baseline (no changes written)

Follows the method already established in the parent plan §4, using whatever MSBuild the developer has. Nothing
tracked depends on which one.

1. Record the current deployed set of `Mods\BloodRain\`: file names, sizes, and hashes. In particular `Cronos.dll`
   = 50,424 bytes (`lib\net45`) and `Cronos.xml` = 10,861 bytes. This is the deploy oracle.
2. `git worktree add --detach <scratch>/baseline HEAD` and evaluate `BloodRain` there with `-getProperty:` /
   `-getItem:`, capturing properties `OutputPath, OutDir, TargetDir, TargetPath, LangVersion, DefineConstants,
   AssemblyName, RootNamespace, TargetFrameworkVersion, OutputType, DebugType, Optimize, DebugSymbols, PlatformTarget,
   WarningLevel, ErrorReport, FileAlignment, AppDesignerFolder` and items `Reference, Compile, Content, None`.
   `build/tools/compare-eval.py` does the diff.

### Phase 1 — The change (~12 lines)

One logical change: swap the dependency mechanism.

- Edit `BloodRain/BloodRain.csproj` per §4.1.
- `git rm BloodRain/packages.config`.
- Add root `nuget.config` per §4.3 (separable; drop on request).

### Phase 2 — Verification

Every item must pass before handoff. With option 1 rejected there is no bail-out path, so **V2** and **V4** are no
longer go/no-go gates — they are *decision* gates. Each has an acceptable second outcome (§6), but the outcome must be
observed and recorded rather than allowed to change silently.

| # | Check | Pass criterion |
| --- | --- | --- |
| V1 | Restore succeeds | Exit 0, `obj/project.assets.json` written. Repeat with `NUGET_PACKAGES` pointed at an empty scratch folder to prove a genuinely cold machine works, not just this warm cache |
| V2 | **Asset selection** | `project.assets.json` targets `.NETFramework,Version=v4.8.1` and picks `lib/net45/Cronos.dll`, not `lib/netstandard2.0/`. The two differ (50,424 vs 53,496 bytes), so this is decidable by size. NuGet's nearest-framework rule should prefer the same-family `net45` group over the netstandard fallback — but this is the single most likely place for behaviour to change, so it is checked directly |
| V3 | Evaluation diff vs. the Phase 0 baseline | Every listed property **identical**, especially `OutDir`/`TargetDir`/`TargetPath`. `Reference`: the 10 game assemblies + `0Harmony` unchanged; the explicit `Cronos` entry gone (expected). `Compile`/`Content` unchanged. `None` loses `packages.config` (expected) |
| V4 | **Redirected build and deploy set** | `-p:ModsDir=<scratch>` build exits 0 and the output folder holds exactly: `BloodRain.dll`, `.pdb`, `ModInfo.xml`, `README.md`, `Config\` (5 files), `Cronos.dll`, `Cronos.xml`. `Cronos.dll` byte-identical to the currently deployed copy. If `Cronos.xml` no longer appears, that is a deploy-set change and needs a conscious decision, not a shrug — see §6 |
| V5 | **Fresh-clone acceptance** — the actual F2 criterion | `git worktree add --detach <tmp> HEAD` (a worktree carries no untracked files, so it has no `packages/` — the same simulation the parent plan used), copy the Phase 1 edits over it, then restore + redirected build. Exit 0. Must not be run from the working tree, whose populated `packages/` would mask the whole defect |
| V6 | No-restore failure is readable | Delete `obj/`, build without restoring. Expect NuGet's "assets file … not found, run a NuGet package restore" error — one actionable line, replacing today's MSB3245 warning plus a `CS0246` wall |
| V7 | Inert elsewhere | Evaluate one unrelated project (`StrongHorns`) before/after. Byte-identical — proves neither the csproj change nor `nuget.config` leaks |
| V8 | Live install untouched | `Mods\BloodRain\` file timestamps unchanged after every redirected build |
| V9 | **Two toolchains, not one** | Restore + build under at least two independent toolchains, so §7's documented commands are tested rather than asserted. Whatever cannot be run here gets said plainly in the handoff rather than implied to have passed |

Shell-quoting reminder carried over from the parent plan §4: in bash use forward slashes and quote the whole switch —
`"-p:ModsDir=C:/Temp/sdtd-verify"`. Backslashes are eaten and MSBuild silently writes to a *relative* path while still
reporting success.

### Phase 3 — Documentation (tracked, tool-neutral wording)

- **`CLAUDE.md`**, *Building → References* (lines 59–61). Replace the "`packages/` is gitignored … needs
  `nuget.exe restore`, not `dotnet restore`" paragraph. New text states that `BloodRain` has the repo's one NuGet
  dependency as a `PackageReference`, restorable by any standard toolchain, and gives the command forms below. Also
  update the line 8 parenthetical listing `packages` as a non-mod top-level directory if the folder stops being part
  of the build story.
- **`BloodRain/README.md`**. One line under a short *Building* heading: depends on Cronos (MIT), restored from
  nuget.org on first build.
- **`.ai/build-refactor-plan.md` §0**. Mark F2 resolved with a pointer to this document, and correct the
  "`.gitignore` negation" stopgap line per §3 above.

Command forms to document — deliberately covering the three styles, none of them naming an IDE as a requirement:

| Toolchain | Restore + build |
| --- | --- |
| .NET SDK | `dotnet build BloodRain/BloodRain.csproj -c Debug` (restores implicitly) |
| MSBuild directly | `msbuild BloodRain\BloodRain.csproj -restore -p:Configuration=Debug` |
| Any IDE | Restores on solution load or first build; no manual step |

No Rider path, no VS path, and no "install X" appears in any tracked file.

## 6. Open questions and risks

| Risk | Assessment | Mitigation |
| --- | --- | --- |
| NuGet resolves `lib/netstandard2.0` instead of `lib/net45` | The nearest-framework rule should prefer the same-family group, but this is the least certain part of the design | V2 checks it directly. If it picks netstandard2.0 the likely answer is **accept and document it**: same library, same version, and a cron parser has no platform-dependent behaviour. Forcing `net45` is possible (`GeneratePathProperty="true"` plus an explicit `HintPath` on `$(PkgCronos)`) but reintroduces a literal path, so it needs a reason beyond tidiness |
| `Cronos.xml` stops deploying | Under `PackageReference` the doc file rides along via `AllowedReferenceRelatedFileExtensions` (`.pdb;.xml`), so it probably still ships. It is XML documentation and inert at runtime — arguably it should never have shipped | V4 detects it. Flag either way as a deliberate deploy-set decision; do not let it change silently |
| First build now needs network access | Real, and new. Today's fresh clone is broken rather than offline-capable, so this is not a regression | Documented in Phase 3. Hard-offline-with-cold-cache is accepted as unsupported, since the only fix for it was option 1 |
| `nuget.config` overrides a corporate feed | Affects only developers on locked-down feed configuration | §4.3 is separable and can be dropped from the change |
| Non-SDK + `PackageReference` is a less-travelled combination | Supported, but not what most of the ecosystem exercises | V1–V6 exercise the whole path end to end; V5 is the one that actually settles F2 |

**Decision, settled 2026-07-27:** option 2. Option 1 was rejected on repo policy — no binaries in source control —
which also settles the "restore step versus vendored DLL" trade in favour of the restore step.

## 7. Explicitly out of scope

F1 (SDK-style migration); the other 30 projects; the load-order-prefix normalisation of F7; cleaning up developers'
existing local `packages/` folders; any C# change to `BloodRain`; central package management
(`Directory.Packages.props`) — pointless for a single package, and it would introduce an auto-imported file into a
repo that deliberately has none.

## 8. Sequencing: this before F1, not folded into it

F1 is intended to follow immediately, which raises a fair question — why not skip this and let the SDK-style migration
fix F2 in passing? Three reasons.

**The rework is one line.** Under F1, `<PackageReference Include="Cronos" Version="0.11.0" />` survives verbatim;
SDK-style projects default to `RestoreProjectStyle=PackageReference`, so only that one property gets deleted. The
`packages.config` removal and the `nuget.config` are permanent regardless. A stepping stone that costs one line of
rework is not a detour.

**`BloodRain` is the wrong pilot for F1.** The tempting alternative is to make this F1's first phase — migrate
`BloodRain` alone to SDK-style, closing F2 as a side effect with no intermediate state. That inverts good pilot
selection. `BloodRain` is the repo's *only* project with a package dependency, so piloting SDK-style there exercises
two novel mechanisms at once and leaves you unable to attribute a failure to either. The parent refactor piloted on
`StrongHorns` — a boring, representative project — and F1 should do the same. Option 2 does the complementary thing:
it isolates the package mechanism on one project and settles it with the *cheap* oracle (evaluation diff against a
`HEAD` worktree, per the parent plan §4) plus one redirected build. F1 then starts with `BloodRain`'s dependency
already a known quantity, and picks its own pilot on its own merits.

**F1 will need restore anyway, and this proves the path first.** Building `net481` under the .NET SDK without
Visual Studio targeting packs installed is normally done by referencing `Microsoft.NETFramework.ReferenceAssemblies.net481`
— which would introduce restore to all 19 code mods at once. Establishing and documenting the restore step now, on one
project, removes that variable from F1 rather than compounding it.

There is also a plain sequencing point: F2 is a live defect — a fresh clone genuinely cannot build `BloodRain` — while
F1 is cleanup. A broken clone should not wait on a multi-phase refactor that has not started.

**Where the argument for going straight to F1 has merit.** The parent plan's decision #3 deferred F1 partly because
real compile verification "is not currently runnable from the agent shell." That premise is now false (see §9), so F1
is more feasible than when it was shelved. What has not changed is that F1 spans ~19 projects against a 250-line cap
per iteration, so it is a multi-phase job either way; doing this first does not delay it by a phase it would not have
needed regardless.

## 9. What has and has not been established

Verified by reading files in this repo and the local package cache: the tracked/ignored status of every file named in
§1; the deployed contents of `Mods\BloodRain\`; the `lib/` layout, byte sizes, MIT license and empty `.NETFramework4.5`
dependency group of `Cronos 0.11.0`; the absence of any `nuget.config`; and the import positions asserted in §4.2 as
read from the shared build files.

**Not yet run:** every item in Phase 2. No restore, no build, and no fresh-clone test has been executed. §4.2 and the
V2 asset-selection expectation are reasoned from NuGet's documented behaviour and must be treated as predictions until
Phase 2 confirms them.

One correction to the parent plan while in the area: `.ai/build-refactor-plan.md` §4 states there is no .NET SDK on
this machine. That is no longer true — a private SDK is bundled with the installed IDE and is usable for local
verification. It changes what *can be verified here*; per §2 it changes nothing about what may be committed, and F1's
deferral rests on scope rather than on tooling availability.

# XML well-formedness linting, wired into the build (#16)

Design and plan for [#16](https://github.com/Strongheart-Games/StrongMods/issues/16). Scope decisions made with the
human before this doc was written:

- **Well-formedness only.** The deeper passes — repo-convention checks (ModInfo BOM, required elements) and
  Config-patch structural checks (known command vocabulary, XPath compilation) — are follow-on issues, filed at
  handoff, not built here.
- **Files:** each project's `ModInfo.xml` and `Config\**\*.xml`. Project XML (`*.csproj`, `build/*.props`,
  `build/*.targets`) is covered by MSBuild itself — see §2.
- **Runs in the build itself** (all three entry points), not as a separate tool or CI step.
- **C#/MSBuild, no external tool.** Challenged and upheld: the only distribution channel the first build command
  already exercises is NuGet restore plus the .NET SDK, and the mature hardened parser (`System.Xml`) ships inside
  it. xmllint's extra power (RelaxNG/Schematron) is power this scope doesn't use, and its Windows install story
  fails the "first build installs it, no human steps" test. If a future pass wants declarative schemas, a CI-only
  xmllint step is the right vehicle then.

## 1. The problem

Nothing validates XML before a loader hits it. Malformed project XML fails the build with MSBuild's load error (how
the F2 `--`-inside-a-comment incident surfaced); malformed `Config/` XML is worse — it ships silently and fails at
*game* load, on a machine that may not be the author's. The 52 `Config/**/*.xml` patch files and 30 `ModInfo.xml`
manifests are hand-written and validated by nothing at all.

## 2. Why project XML needs no new pass

MSBuild parses every `.csproj`, `.props` and `.targets` it loads, and a well-formedness error is a build failure
with file/line/position — the F2 incident *was* caught this way. Every project is in `StrongMods.sln` and every
shared build file is imported by at least one entry point, so the standing CI solution build already sweeps them
all on every push. A second parse of the same files with the same parser (`System.Xml`) adds nothing. Residual gap,
accepted: an XML file that nothing loads (none exist today under `build/`; `Local.props.sample` is not XML-loaded).

## 3. Design

One new shared file, `build/XmlLint.targets`, imported by all three entry points (`Mod.targets`,
`Modlet.targets`, `Overlay.targets`) the same way `Deploy.targets` already is. It contains:

1. **An in-process MSBuild task** (`UsingTask` + `RoslynCodeTaskFactory`, ~20 lines of C#). At review the code
   moved from an embedded `<Code>` fragment to a real class file, `build/XmlLint.cs`, referenced via
   `<Code Type="Class" Source="..." />` — same compile-and-cache behavior, but IDEs treat it as C#, the
   `[Required]` parameter lives in the class instead of a `ParameterGroup`, and `Execute()`'s return value is
   explicit (which retires the `Success`-property fix described under *Results*). For each input file, run an
   `XmlReader` to end-of-document with `DtdProcessing.Prohibit` and `XmlResolver = null` (no file here should
   declare a DTD, and the lint must never touch the network). On `XmlException`, `Log.LogError` with the file,
   line and column — the canonical MSBuild error shape, clickable in IDEs, and it fails the build. Encoding
   errors (invalid bytes for the declared encoding) surface through the same reader and are treated identically.
2. **A target**:

   ```xml
   <Target Name="XmlLint" BeforeTargets="PrepareForBuild;Build" Condition="'$(XmlLintEnabled)' != 'false'">
     <ItemGroup>
       <XmlLintFile Include="ModInfo.xml" Condition="Exists('$(MSBuildProjectDirectory)\ModInfo.xml')" />
       <XmlLintFile Include="Config\**\*.xml" />
     </ItemGroup>
     <XmlWellFormednessCheck Files="@(XmlLintFile)" Condition="'@(XmlLintFile)' != ''" />
   </Target>
   ```

   A target runs at most once per project build, so listing both hooks is safe: code mods hit `PrepareForBuild`
   (early — before compile); modlets and overlays, whose hand-rolled `Build` has no `PrepareForBuild`, hit `Build`.
   Items are declared inside the target, so evaluation is untouched — projects that add nothing see no new items
   until the target runs.

Design points, and the alternatives weighed:

| Decision | Rationale |
| --- | --- |
| Inline task, not `build/tools/xml_lint.cs` invoked per build | `dotnet run` spawns a process and re-checks the file-based app per invocation — ~1s × 32 projects on every solution build. The inline task runs in-proc; `RoslynCodeTaskFactory` compiles it once and caches the assembly by source hash, so steady-state cost is an assembly load. The #36 "checked-in tools are C#" rule is satisfied — the task body *is* C#, it just lives where the build can reach it for free. |
| `RoslynCodeTaskFactory`, not the older `CodeTaskFactory` | The Roslyn factory works under both `dotnet build` and full `MSBuild.exe`; the older one is .NET-Framework-MSBuild-only. Modlets/overlays import no SDK, but the factory ships with MSBuild itself (`$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll`), so bare `<Project>` files can use it. |
| No incrementality (no `Inputs`/`Outputs` stamp) | Parsing a handful of small XML files is milliseconds; an up-to-date stamp adds state and failure modes for no measurable win. |
| Runs in design-time/IDE builds too | Deliberate: malformed Config XML surfaces in the IDE error list while the file is being edited, which is the earliest possible catch. Cost is the same milliseconds. |
| `$(XmlLintEnabled)` escape hatch | One property to bypass the gate in an emergency (`-p:XmlLintEnabled=false`); costs one condition. |
| No new CI step | CI builds the solution against both units; the lint rides along. `build.yml` is untouched. |
| Prefab XML, `StrongholdSaves\StrongMods\*.xml` not covered | Out of the decided scope (World-Editor-authored / overlay content outside `Config\`). A project wanting extra files covered can add `<XmlLintFile Include="..." />` items in its body — the target's `Include`s append to any project-declared items for free. Not exercised in this change. |

## 4. Changes

| File | Change | ~Lines |
| --- | --- | --- |
| `build/XmlLint.targets` | New: header comment, `UsingTask` inline task, `XmlLint` target | ~55 |
| `build/Mod.targets` | One `<Import>` beside the `Deploy.targets` import | 1 |
| `build/Modlet.targets` | Same | 1 |
| `build/Overlay.targets` | Same | 1 |
| `CLAUDE.md` | Row in the shared-build-files table; one sentence under *Verifying* | ~3 |

Well under the 100-line target. No `.csproj` changes anywhere; no evaluation-visible property or item changes.

## 5. Verification

1. **Baseline sweep** — build the full solution (`dotnet build StrongMods.sln -c Debug`). Expected: green, proving
   all 82 in-scope files are currently well-formed. If any pre-existing file fails, fixing it is its own tiny
   commit *before* this change lands, so the lint lands green.
2. **Negative test, the F2 regression case** — a throwaway project under `.scratch/lint-test/` importing
   `Modlet.targets`, with a `Config\broken.xml` containing `--` inside a comment. Expected: build **fails** with
   `Config\broken.xml(line,col): error : ...`. Repeat once with a truncated-document case, and once via
   `-p:XmlLintEnabled=false` (expected: passes) to prove the escape hatch.
3. **Entry-point coverage** — one deliberately-broken build per shape (code mod, modlet, overlay), each in
   `.scratch/`, confirming the `BeforeTargets` hook fires in all three. The overlay case uses a scratch copy, not
   `Hades`.
4. **Evaluation diff** — `build/tools/compare-eval.cs` on one code mod, one modlet, one overlay against a `HEAD`
   worktree (querying `OutDir`/`TargetDir` per its header). Expected: no drift — the change is targets-only.
5. **Both toolchains** — step 2's failing build repeated with full `MSBuild.exe` if present, since IDE builds use
   it and `RoslynCodeTaskFactory` behavior is the one genuinely toolchain-sensitive piece here.

### Results (2026-07-31)

| Step | Result |
| --- | --- |
| 1. Baseline sweep | ✅ `dotnet build StrongMods.sln -c Debug` green, 0 warnings/errors — all in-scope files well-formed. Target execution positively confirmed at `-v:d` (runs before `PrepareForBuild` in code mods, before `Build` in bare projects). |
| 2. Negative tests | ✅ F2 `--`-in-comment case fails the modlet build, exit 1, `Config\broken.xml(3,48): error : An XML comment cannot contain '--'…`; truncated doc fails likewise; `-p:XmlLintEnabled=false` passes. Scratch projects left in `.scratch/lint-test/` for inspection. |
| 3. Entry-point coverage | ✅ All three shapes fail on broken Config XML with exit 1 (code mod / modlet / overlay scratch projects). |
| 4. Evaluation diff | ✅ `compare-eval` vs a `HEAD` worktree on `BloodRain`, `AECInternationalMarketFixes`, `StrongholdSaves`: modlet and overlay IDENTICAL; code mod's only diffs are the worktree's absolute-path prefix in `TargetDir`/`TargetPath` (both resolve `bin\Debug\` correctly) — no drift. |
| 5. Full `MSBuild.exe` | ⚠️ Not executable on this machine: no VS install, no `vswhere`; Rider drives SDK projects through the .NET SDK MSBuild, which is the tested toolchain. `RoslynCodeTaskFactory` is documented to work on full MSBuild ≥ 15.8; this leg stays unverified until a machine with one builds the repo. |

**Defect found and fixed during step 2:** the first cut assumed the factory's generated `Execute()` returns
`!Log.HasLoggedErrors`. It does not — a fragment that only calls `Log.LogError` returns *true*, and a bare-project
(modlet/overlay) build then "succeeds" with 1 error and exit 0; only the SDK build path happened to fail. The fix
is the fragment's predefined `Success` property, set explicitly (`Success = !Log.HasLoggedErrors;`), after which
all three shapes fail with exit 1. The SDK-path masking is why the negative tests cover every entry point.

Superseded at review: the fragment became the class file `build/XmlLint.cs` (§3), whose `Execute()` returns
`!Log.HasLoggedErrors` directly — the failure mode above can no longer be expressed. Steps 1–3 were re-run against
the class-file shape: same results (three failures with exit 1, escape hatch passes, solution green).

## 6. Handoff

After human review and merge: file the two follow-on issues (repo-convention checks; Config-patch structural
checks — both `type:tooling`, `scope:repo-wide`, citing #16 and this doc), then close #16 noting §2's reasoning for
why project XML gets no separate pass.

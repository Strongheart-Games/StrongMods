# Research: replace or harden Python for `build/tools/` scripting (issue #36)

- **Issue:** [#36](https://github.com/Strongheart-Games/StrongMods/issues/36) — status lives there.
- **Question:** the repo's developer tooling is Python (`compare-eval.py`, `vendor.py`, `steam_check.py`; `pack.py`
  and `release.py` planned under #22), and the owner loathes Python's semantic indentation and unenforced typing.
  Find a better default before the tooling surface grows.
- **Recommendation:** **C# file-based apps** (`dotnet run tool.cs`, .NET 10) for all new tools, with existing
  tools migrated as they are next touched. Evidence and the honest costs below.

## 1. The constraints, and one fact that reframes them

The issue's constraints: cross-platform (Windows dev + Linux CI/servers, same script unchanged), zero-friction
availability, single-file dependency-free ergonomics, cheap `--selftest`-style offline checks.

The reframing fact: **"zero-friction availability" for this repo is not "what ships with an OS" — it is "what a
machine must already have to build this repo at all."** The .NET SDK is a hard prerequisite everywhere the tools
run: no machine can do anything useful with StrongMods without `dotnet`. Measured on 2026-07-31:

| Machine | .NET SDK | Python | pwsh 7 | Go | Node | Deno |
| --- | --- | --- | --- | --- | --- | --- |
| Dev machine (Windows 11) | 10.0.302 | 3.14.6 | — | — | — | — |
| GitHub `ubuntu-latest` | 8.x/9.x/**10.0.302** preinstalled | 3.12.3 system | 7.6.3 | 1.24–1.26 | 22/24 | — |

The dev machine and the CI runner already carry the *same* .NET SDK version, today, with no install step. Python
is the only other runtime present on both — and it already has a version skew (3.14 vs 3.12).

## 2. Candidates

### 2a. C# file-based apps — recommended

.NET 10 runs a bare `.cs` file with no csproj: `dotnet run tool.cs -- args` (or `dotnet tool.cs args`).
File-level directives replace the project file: `#:package Foo@1.2.3` for NuGet, `#:property Nullable=enable`,
`#:include shared.cs` for multi-file, `#!` shebang for Unix direct execution. `dotnet project convert` graduates
a script to a real project if one ever outgrows the shape.

Everything load-bearing was **verified empirically on this machine's SDK 10.0.302**, not taken from blog posts:

- **Runs, args, exit codes, `--selftest` pattern:** all work; a port of the `steam_check.py --selftest` shape is
  natural (top-level statements, `args`, `Environment.Exit`).
- **Startup:** first run of a file compiles it — measured **6.7 s cold, 0.19 s warm** (cached until the file
  changes). Warm is competitive with Python startup; cold is the price of enforced typing (§3).
- **Multi-file:** sibling files are *not* auto-compiled, but explicit `#:include shared_core.cs` **works on
  10.0.302** (backported from .NET 11; included files add types but not top-level statements). The planned
  `release.py`-imports-`steam_check.py` sharing (#22 §6b) maps to `#:include steam_check_core.cs` — or to
  process composition via the `--json` + exit-code contract `steam_check` already defines.

Against the constraints:

- **Cross-platform:** same file runs unchanged via `dotnet run` on Windows and Linux; shebang (`#!/usr/bin/env
  dotnet`, LF, no BOM) additionally allows `./tool.cs` on Unix. The repo's `.editorconfig` already mandates LF.
- **Availability:** the only candidate *guaranteed* present wherever the repo builds, because it is the build.
  CI needs nothing (preinstalled; #22's `setup-dotnet` pin covers version drift).
- **Single-file ergonomics:** preserved — directives in the file, doc header as a comment block, no manifest.
- **Typing:** the strongest of any candidate, and enforced at the strongest point: **the script cannot run
  ill-typed**, because every run compiles. Python-with-mypy only enforces where a CI/pre-commit gate runs;
  nothing stops executing an unchecked edit locally. And braces, obviously.

Honest costs:

- **Cold compiles during iteration:** every edit pays a rebuild (seconds) on next run. Python's edit-run loop is
  instant. For tools run far more often than edited (all of ours), the warm path dominates.
- **SDK, not runtime, wherever tools run.** Today every tool runs on dev machines or CI only. If a tool ever
  needs to run on a Linux game server, that server needs the ~450 MB SDK (one `dotnet-install.sh` invocation),
  where Python is typically already present. Real, currently-hypothetical cost — no `build/tools/` script runs
  on a game server today.
- **Verbosity for text-munging:** subprocess + regex + dict-shuffling code (the `steam_check` VDF parser) is
  somewhat wordier in C# than Python. `System.Text.Json`, `Regex`, and `Process` cover everything the current
  tools do; none pulls a package.
- **Migration cost:** 538 lines of working, reviewed Python across three tools. Mitigated by migrating
  opportunistically (§4), not big-bang.
- **Dialect skew:** mods are pinned to C# 9 (game-dictated); tools would use current C#. Minor — the same skew
  exists today between C# mods and Python tools, and it shrinks, not grows.

### 2b. Stay-in-Python mitigations — the fallback, and half a fix

- **Typing:** enforceable to a useful degree: `mypy --strict` or pyright strict mode plus ruff's `ANN` rules
  (flake8-annotations) *require* signature annotations and statically check them; wire into the #22 workflow and
  pre-commit. Enforcement is gate-time, not run-time — an unchecked script still executes.
- **Indentation:** no credible fix. **Bython** (the braces-to-indentation preprocessor) last saw a push
  **2020-11-26** with 40 open issues — unmaintained, and it would put a transpile step and an alien dialect
  between the repo and every reader/debugger. Formatters (black/ruff) make indentation *consistent*, not
  *non-semantic*. This half of the complaint is unfixable inside Python.

### 2c. The rest — measured and set aside

| Candidate | Typing | Availability today (dev / CI / Linux server) | Verdict |
| --- | --- | --- | --- |
| PowerShell 7 | Dynamic; `[int]` params coerce rather than enforce; no static checker of substance | absent / preinstalled / install | Fails the typing requirement outright; also splits from Windows PowerShell 5.1 semantics on the dev box |
| Go (`go run tool.go`) | Static, enforced at run like C# | absent / preinstalled / install | Sound option, but adds a second toolchain to every machine where `dotnet` is already mandatory; no advantage over C# for this repo |
| TypeScript on Deno/Bun | Static — but `deno run` skips type-checking by default (needs `--check`); Node 22's type-stripping never checks | absent / absent (Node yes, Deno/Bun no) / install | Install step on every machine *and* type enforcement is opt-in; dominated by C# on both axes |
| Rust | Static | absent / absent / install | Toolchain + per-script compile ceremony (`rust-script` or cargo project); massively over-specced for glue tools |

## 3. Why C# wins on the owner's actual complaints

Both named pains — semantic indentation, unenforced typing — are *language-core* in Python; every stay-in-Python
mitigation is a bolt-on gate that later tooling, contributors, or haste can route around. In a compiled-on-run
language the enforcement is structural: there is no path to executing an ill-typed script. C# is also the one
candidate where the "extra" toolchain requirement is already an unconditional repo prerequisite, and where the
tools speak the same language as the codebase they serve — one language for the repo instead of two.

## 4. Migration shape (decisions for the owner, tracked on #36/#22)

1. **New tools start as C# file-based apps** — `pack.py` and `release.py` are not yet written; under #22 they
   would be born as `pack.cs` / `release.cs`, avoiding two migrations. `steam_check.py` shipped ~a day ago and
   its consumers (workflow, `release`) don't exist yet, making it the natural pilot port while its `--selftest`
   fixtures define equivalence.
2. **Existing tools migrate when next touched** — `vendor.py` and `compare-eval.py` keep working; no big-bang.
3. **Naming/venue unchanged:** `build/tools/*.cs`, doc header comment in place of the docstring, `--selftest`
   convention carried over.
4. If rejected: the fallback is §2b (pyright-strict + ruff ANN in the #22 workflow), accepting that the
   indentation complaint goes unaddressed.

## 5. References

- [Announcing `dotnet run app.cs` (.NET blog)](https://devblogs.microsoft.com/dotnet/announcing-dotnet-run-app/)
- [File-based apps — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) (directives incl. `#:include`, `dotnet project convert`)
- [Andrew Lock: exploring `dotnet run app.cs`](https://andrewlock.net/exploring-dotnet-10-preview-features-1-exploring-the-dotnet-run-app.cs/)
- [ubuntu-latest runner image manifest](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md) (preinstalled SDK/runtime versions)
- [Bython](https://github.com/mathialo/bython) (last push 2020-11-26)
- Local measurements 2026-07-31: SDK 10.0.302; cold/warm timings, `#:include`, arg/exit-code behavior verified in scratchpad.

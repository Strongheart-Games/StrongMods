# StrongMods Formatter Requirements Brief

## Goal

StrongMods should have a **repo-owned executable formatting policy**.

Formatting and safe automatic source-style normalization should not depend on Rider, another IDE, or an individual agent interpreting repository preferences correctly. Contributors should be able to run one command to bring the repository into conformance with that policy.

Consistency is more important than preserving the author's personal style.

The system should favor an established, opinionated formatting baseline with a deliberately small set of StrongMods-specific choices.

## Terminology

The command is named **`format`** because it is familiar and communicates the important expectation that the operation is safe and semantics-preserving. Its responsibilities are intentionally broader than narrow whitespace formatting.

**Normalization** is the broader technical term for what `format` does: safe, deterministic formatting and source-style transformations that bring source files into conformance with the formatting policy.

**Conformance** describes what `format --check` evaluates. A conforming file or repository is one for which `format` would make no further changes.

**Canonical** is reserved for cases where the policy truly prescribes one representation or ordering. Not every conforming file necessarily has one unique textual representation.

**Formatting policy** is the preferred concise name for the rules enforced by `format`. Use the more explicit **formatting and source-style policy** where the distinction adds useful clarity.

An explicit path selects a **target** for formatting or checking. The target determines the **write scope**: `format` may inspect broader project context when necessary for safe analysis, but it must not modify files outside the write scope.

The **worktree boundary** is the current Git worktree. It bounds targets, configuration discovery, ignore discovery, symlink resolution, and writes.

## Core Interface

The user-facing interface is one command:

```bash
format [PATH...]
```

A concise description suitable for `format --help` is:

> Applies StrongMods' safe, automatic formatting and source-style normalization.

With no paths:

```bash
format
```

formats all supported **Git-tracked files in the current worktree**.

With explicit paths:

```bash
format README.md src/ SomeScratchFile.xml
```

formats exactly the requested file targets or recursively formats the requested directories, subject to the worktree and ignore rules below.

The user should not need to know which underlying formatter handles C#, Markdown, XML, or another format.

### Check Mode

```bash
format --check [PATH...]
```

performs the same evaluation without modifying files.

A concise description suitable for `format --help` is:

> Reports whether `format` would make changes without modifying files.

Expected result classes should be distinguishable by exit code:

* `0`: everything evaluated conforms to the formatting policy
* `1`: formatting or normalization changes are required
* another nonzero code: conformance could not be fully evaluated because of an error

Normal check output should concisely identify affected files. Detailed diffs may be available on demand rather than printed by default.

### Staged Check

```bash
format --check --staged
```

checks the **complete staged repository snapshot represented by the Git index**, not merely files with staged changes.

This includes unchanged files and staged versions of formatting configuration such as `.editorconfig` and `.formatignore`.

The `--staged` help text should make this distinction explicit. For example:

> Operate on the complete repository snapshot represented by the Git index, including unchanged files and staged configuration. This does not mean "only files with staged changes."

When `--staged` is used, runtime output should also surface the semantics immediately, for example:

> Checking the complete staged repository snapshot (the Git index), not just files changed in this commit...

The pre-commit hook should use this mode.

## Worktree Boundary

`format` v1 must run from inside a Git worktree.

The **current Git worktree** is the formatting boundary.

There is no CWD-based fallback outside Git in v1.

All of the following must remain inside the current worktree:

* files being modified
* explicit path targets
* formatter configuration discovery
* ignore-file discovery
* resolved symlink targets

Filesystem containment alone is insufficient. Recursive formatting must not cross into another Git repository or worktree nested beneath the current one.

This includes:

* linked worktrees under locations such as `.claude/worktrees/`
* linked worktrees under `.scratch/`
* Git submodules
* other nested Git repositories

To format one of those repositories/worktrees, run its own `format` command from within it.

## Path Semantics

Relative explicit paths are resolved from the caller's current working directory.

`format` may be run from any directory within the current worktree.

For example, when invoked from `mods/Foo/`:

```bash
format
```

still formats the entire current worktree, while:

```bash
format README.md
```

targets `mods/Foo/README.md`.

Explicit paths outside the current worktree are rejected.

### Symlinks

Recursive discovery does not follow symlinks.

An explicitly named symlink may be formatted only if its resolved target remains inside the current worktree.

Formatting a symlinked file modifies the target while preserving the symlink itself.

## Ignore Semantics

### `.formatignore`

`.formatignore` files are hierarchical.

A `.formatignore` applies to its containing directory and descendants. Deeper `.formatignore` files layer on top of inherited rules and may override them using familiar gitignore-style semantics where supported.

For example:

```text
repo/.formatignore
repo/mods/Foo/.formatignore
```

both contribute rules for files beneath `repo/mods/Foo/`.

Recursive directory formatting honors `.formatignore`.

An explicitly named file overrides `.formatignore`.

A future escape hatch such as:

```bash
format --no-ignore some-directory/
```

may bypass ignore rules for recursive discovery if needed.

### `.gitignore`

Recursive discovery also honors `.gitignore` rules **within the current worktree**.

Machine-specific or global Git ignore rules outside the worktree must not affect formatting, because that would make results differ between contributors and CI.

Explicitly named files may still be formatted despite being Git-ignored.

## Configuration

`.editorconfig` should remain the primary human-readable declaration of style where it can express the desired rule.

Formatter-specific configuration should be used only where needed.

The output of `format`, not any individual configuration file, is ultimately authoritative.

Prefer **one source of truth for each formatting decision** rather than duplicating equivalent settings across configuration systems unless compatibility requires it.

Normal hierarchical EditorConfig behavior is allowed. V1 should not add special machinery forbidding nested `.editorconfig` files.

StrongMods currently intends to use one repo-wide style, with file-type-specific differences where useful. Directory-specific style overrides should be introduced only for a concrete reason.

The root StrongMods style must retain:

```ini
indent_style = space
indent_size = 2
```

Two-space indentation using spaces is a must-keep StrongMods convention.

Conflicting configuration systems, such as `.editorconfig` and `.gitattributes` specifying incompatible line-ending policies, should be treated as configuration errors rather than resolved through an undocumented precedence rule.

## Style Philosophy

`format` owns **all safe, deterministic automatic formatting and source-style normalization**.

This includes more than whitespace formatting. It may also include code-style cleanup when the transformation is safe and appropriate.

A separate linting layer may eventually enforce rules that should not be fixed automatically.

The user should not have to decide whether a particular automatic cleanup is technically formatting, style, or lint autofix.

## Safety Invariants

The formatter should feel safe to run at any time.

When safety cannot be established, prefer a clear failure or no transformation over guessing.

### Semantic Safety

Automatic transformations must be intended to preserve program semantics.

Most importantly:

> **Any automatic transformation whose correctness depends on modifying another file is not a formatting rule and must not be supported by `format`.**

The formatter may inspect files outside an explicitly requested target when semantic analysis requires broader project context, but it may not modify anything outside the target's write scope.

Examples of transformations that do **not** belong in `format` include:

* renaming methods, properties, fields, or types when references elsewhere must change
* changing public APIs or signatures
* moving declarations between files
* other refactorings requiring coordinated edits

### Scope Safety

Explicit targets define a hard write boundary.

```bash
format src/Foo.cs
```

may modify only `src/Foo.cs`.

### Determinism

Given the same:

* repository contents
* formatter configuration
* formatter versions

the result must not depend on filesystem traversal order, concurrency, or which files happen to be processed first.

### Idempotence

`format` must be idempotent.

Running:

```bash
format
format
```

must result in the second invocation making no further changes.

Equivalently, once source conforms to the formatting policy, another formatting pass must leave it unchanged.

### Preflight

Before modifying files, `format` should preflight everything it reasonably can, including:

* target resolution
* Git/worktree boundaries
* configuration
* required formatter availability
* applicable project loading
* supported-file detection
* obvious parsing failures

If a known prerequisite prevents part of the requested operation from succeeding, `format` should fail before knowingly performing a partial operation.

Whole-operation filesystem transactionality is **not** required in v1.

If an unexpected runtime failure occurs after formatting has begun, earlier files may already have been successfully reformatted.

Per-file writes should nevertheless be safe.

## Supported and Unsupported Files

Encountering an unsupported file during recursive discovery is not an error; it is skipped.

Explicitly requesting an unsupported file is an error.

Example:

```bash
format .
```

may silently skip a PNG.

But:

```bash
format image.png
```

should report that the explicitly requested file type is unsupported.

## Universal Text Normalization

`format` should own basic text representation rules in addition to language-specific formatting.

Normalized text uses:

* UTF-8
* no BOM
* LF line endings
* no trailing whitespace
* a final newline

UTF-8 files containing a BOM may be safely normalized to UTF-8 without BOM.

Files that are not valid UTF-8 should cause a clear error rather than triggering encoding guesses or implicit conversion.

### Unsupported Text Formats

Universal normalization may eventually apply to text formats without a semantic formatter, such as `.txt` or `.csv`.

V1 should use an explicit allowlist of file types known to be text rather than guessing from arbitrary file contents.

Broader content-based text detection should be added only if a real need arises.

## File Metadata

Formatting changes file contents only.

It must not:

* rename files
* move files
* alter executable status or unrelated permissions
* replace symlinks

If formatting changes a file, its modification time should naturally change.

If a file already conforms to the formatting policy, it should not be rewritten or touched merely because `format` examined it.

## Comments and Human-Authored Text

Comments and other human-authored prose are **content**, not disposable style artifacts.

`format` may change their presentation where safe, such as:

* indentation
* spacing
* line wrapping
* prose reflow

It must not:

* delete comments
* paraphrase comments
* semantically rewrite comments
* remove license headers
* rewrite human-authored explanatory content

## Reordering and Pruning

`format` may reorder or remove structural code only in **explicitly documented situations**.

Undocumented reordering or pruning is considered incorrect behavior.

Useful examples that may be allowed include:

* sorting C# `using` directives
* removing semantically proven unused C# `using` directives
* canonical member/declaration ordering

Data-oriented content should remain more conservative.

For example, absent a deliberate documented rule:

* XML attributes should not be reordered
* XML elements should not be reordered
* Markdown table rows should not be reordered
* JSON/YAML properties should not be sorted

### Declaration Ordering

Top-level/member declaration reordering is allowed when it provides useful consistency and the transformation is semantics-preserving.

The formatter must preserve relative ordering wherever declaration order can affect behavior.

Where the policy prescribes a unique declaration ordering, that **canonical declaration-ordering policy** must be documented.

## Generated Files

Prefer `.formatignore` as the mechanism for excluding generated tracked files.

V1 should not add our own generated-file detection heuristics.

However, an otherwise strong backend formatter may be accepted even if it has clearly documented built-in generated-file exclusion behavior that cannot reasonably be disabled.

Perfect cross-language consistency is less important than keeping v1 practical.

## Merge Conflicts

If Git reports unresolved merge conflicts within the requested target set, `format` should hard-error before modifying anything.

The formatter should rely on Git's unmerged state rather than searching file contents for conflict-marker-looking strings such as:

```text
<<<<<<<
=======
>>>>>>>
```

Those strings may legitimately occur in documentation or test data.

## Git Index Behavior

Normal mutating `format` operates on working-tree files only.

It must never stage changes or modify the Git index.

Partially staged files may be formatted normally, just as an editor may modify them. Their staged snapshot remains unchanged until the user explicitly stages changes again.

`format --check --staged` is the separate mechanism for evaluating the complete staged repository snapshot.

## V1 File-Type Scope

### Required

V1 must support:

* **C#**
* **Markdown**
* **7 Days to Die XML**
* **universal text normalization**

### Nice to Have

If it comes cheaply with XML support:

* MSBuild XML:

  * `.csproj`
  * `.props`
  * `.targets`

### Deferred

These may be added behind the same `format` interface later:

* JSON
* YAML
* Bash
* other text/source formats

Adding a new language should not require contributors to learn a second formatting command.

## C#

C# formatting may require a restored, loadable .NET project or solution when semantic analysis is necessary.

If C# files are in scope and required C# formatting infrastructure is unavailable, `format` should hard-error.

If no C# files are in scope, missing C# tooling is irrelevant.

C# style cleanup may include safe local transformations, but not cross-file refactoring.

### Comment Reflow

Automatic prose reflow in C# comments and XML documentation is strongly desirable.

However, this should **not be a hard requirement for selecting the primary C# formatter** if an otherwise materially better formatter does not support it.

The architecture should permit additional safe formatting passes later.

Markdown prose reflow remains a v1 requirement.

## Markdown

Markdown should be treated as a human-edited source format.

V1 Markdown formatting should include:

* prose paragraph reflow
* normalized line wrapping
* Markdown table alignment
* preservation of structural constructs such as code blocks, lists, tables, and other syntax where line structure carries meaning

Arbitrary manually inserted prose line breaks should not be treated as sacred.

## XML

XML is central to 7 Days to Die modding and is therefore required in v1.

V1 should use **one conservative, generic XML formatter** rather than separate 7DtD-specific and generic XML engines.

The formatter should be XML-syntax-aware but should not understand 7DtD patch semantics such as the meaning of `<append>`, `<set>`, or `<remove>`.

Desired direction:

* normalized indentation
* preserve element ordering
* preserve attribute ordering unless explicitly changed later
* preserve intentional layout where reasonable
* allow wider lines than source code where appropriate
* constrain pathological blank-line accumulation
* allow future refinement of one-line vs multiline attribute layout
* never alter XPath or other string contents

7DtD-specific formatting behavior should be introduced only if generic XML formatting demonstrates a real deficiency.

## Line Width

The existing 120-column preference is not a hard requirement.

Prefer starting with the chosen formatter's established default unless it feels unreasonably narrow.

StrongMods generally prefers relatively wide formatting, so very narrow defaults may warrant adjustment.

Width should generally be treated as a target print width rather than an absolute guarantee for every construct.

## Formatter Versions and Reproducibility

Formatter versions must be repository-controlled.

Major and minor versions should remain fixed until deliberately upgraded.

Automatic patch-level upgrades are acceptable only when upstream provides a trustworthy compatibility/versioning guarantee indicating that patch releases contain compatible fixes.

If upstream's versioning guarantees are unclear, pin exactly.

Repository configuration should determine formatter versions; collaborators should not need to install particular formatter versions manually.

## Dependencies and Onboarding

Requiring a supported .NET SDK is acceptable because .NET is already central to StrongMods development.

Avoid introducing unrelated runtimes or manually installed prerequisites unless they provide substantial value.

Repo tooling may use cross-platform Bash; Git Bash is already a Windows prerequisite.

The previous "maintained scripts must be C#" rule should not constrain this design.

### V1 Dependency Behavior

For v1, `format` does not need to restore or install its own missing dependencies automatically.

A missing required formatter may produce a clear setup error.

This is **not a permanent architectural invariant**.

Easy onboarding is a high-value goal, and dependency/bootstrap behavior should be revisited in the context of the broader StrongMods development workflow.

An attractive eventual onboarding story is:

```text
Install Git + .NET
Clone
dotnet build
```

with repository-local setup performed automatically where it is safe and reasonable.

## IDE Integration

The repo command is authoritative.

IDEs such as Rider should nevertheless be able to reproduce the formatting policy on save where practical.

IDE integration is valuable but should not become architectural glue.

Support it when easy.

Do not make v1 responsible for automatically configuring every possible IDE.

Documentation for common IDEs, particularly Rider, is sufficient if automatic integration becomes complicated.

## Enforcement

Conformance to the formatting policy should be enforced in two places:

### Pre-commit

A repo-owned pre-commit hook provides fast feedback.

It should **fail**, not automatically rewrite files.

The hook should validate the complete staged repository snapshot using:

```bash
format --check --staged
```

Repo setup may configure:

```bash
git config core.hooksPath .githooks
```

to activate version-controlled hooks.

### CI

CI is the authoritative, unskippable enforcement point.

CI should check conformance across the whole repository on every relevant run.

For now, the entire repository should be checked every time rather than optimizing for changed files.

Performance optimization should be driven by measurements, not speculation.

## Explicitly Deferred Mechanisms

V1 does not need:

* formatting outside the current Git worktree
* stdin/stdout formatting
* whole-operation transactional rollback
* `--best-effort` partial formatting
* custom inline `format off/on` directives
* automatic generated-file detection implemented by our wrapper
* 7DtD-semantic XML formatting
* sophisticated IDE auto-configuration
* performance optimization for changed files only

Inline formatter suppression may be considered later if a chosen formatter already supports it cleanly or a concrete need arises.

## Design Principle

When implementation choices are ambiguous, prefer:

1. **Safety**
2. **Determinism**
3. **Consistency**
4. **Low contributor cognitive load**
5. **Easy onboarding**
6. **Established tool behavior over custom mechanism**
7. **Simple v1 behavior that can evolve in response to real use cases**

The formatter is successful when a contributor or agent can run:

```bash
format
```

without needing to know what language a file uses, which formatter handles it, or whether their IDE happens to agree with everyone else's.

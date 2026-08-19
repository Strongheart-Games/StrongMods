---
name: verify-submission-candidate
description: Verify the exact uncommitted files a human may submit before handing off a candidate or a report that names candidate files. Do not use for ordinary whole-working-tree tests.
---

# Verify submission candidate

Use this skill when an agent gives a human a reviewable, uncommitted file set. The candidate is the exact set the
human may submit, not every modification sharing the current working tree.

1. Name every candidate file explicitly, including the report and coupled acceptance-registry changes.
2. Add the verifier markers, then clean and build the file app:
   `dotnet clean build/tools/verify-submission-candidate.cs`; `dotnet build build/tools/verify-submission-candidate.cs`.
   Run `dotnet run --file build/tools/verify-submission-candidate.cs --no-build` from an explicit base revision with
   those files and the intended validation command or build-then-test command sequence (separated by <code>--then</code>).
3. Hand off only after the verifier succeeds. Its generated **Submission candidate (verified)** section is the
   authoritative file list, base SHA, and validation evidence; do not maintain a competing manual inventory.

When the candidate fails, correct its file set or implementation and rerun the verifier. Broader isolation of parallel
workstreams is not a prerequisite for this skill; track that design separately.

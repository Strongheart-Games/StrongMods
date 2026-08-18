# Startup I/O robustness — plan (#89, #90)

A server operator reported StrongMods crashing a Linux dedicated server at startup with "permission denied".
Investigation found two independent defects that together produce that crash, plus two more in the same family.
This plan covers the two that produce the reported crash. The others are filed and left alone.

## 1. Established facts (verified 2026-08-18)

All four verified by measurement on this machine, not by reading alone.

- **A traversable-but-unlistable directory is a real, reachable state on both platforms.** With
  `ListDirectory` denied on a parent via a Windows deny ACE, `Directory.Exists` and `File.Exists` still return
  `true` for paths underneath it, while `Directory.EnumerateFileSystemEntries` on it throws
  `UnauthorizedAccessException`. Linux mode `0711` is the same state.
- **`GameIO.GetGamePath()` is not normalized.** Read from the shipped `Assembly-CSharp.dll` (V3.0.1.4) it is
  `m_UnityDataPath` with the literal string `"/.."` concatenated on the end.
- **`Directory.Exists` collapses `..` lexically.** `Directory.Exists("<real dir>/no-such-directory-at-all/..")`
  is `true` — the cancelled segment is never put to the filesystem.
- **`Path.GetFullPath` throws where `File.Exists` returns `false`** — measured for `""`, `"   "` and a path
  containing a NUL, all `ArgumentException`. Under Mono (what the game runs) the throwing surface is wider
  still.

The reported crash itself was reproduced end to end: the new permission test, run against a `StrongMods.dll`
built from `HEAD`, fails with
`System.UnauthorizedAccessException : Access to the path '<...>\servers\pzdev\Mods' is denied` thrown out of
`CaseSensitiveFilesystem.Exists`. Against the fixed build it passes.

### The causal chain

| Step | What happens |
|------|--------------|
| 1 | `GameIO.GetGamePath()` returns `<install>/7DaysToDieServer_Data/..` |
| 2 | `Config.IsFilesystemCaseInsensitive` flips the case of that whole string |
| 3 | the only segment whose spelling changes is `7DaysToDieServer_Data`, because a Linux install path is normally already all-lowercase |
| 4 | `Directory.Exists` collapses the trailing `/..`, erasing that segment before any syscall |
| 5 | the probe therefore asks "does the install directory exist?", answers **yes**, and reports the filesystem case-**in**sensitive |
| 6 | the feature turns on — on Linux, the exact environment it was written to stay out of |
| 7 | the transpiled `Exists` runs inside `ModManager.LoadLocalizations`, walking a mod path from `/` and enumerating every ancestor |
| 8 | `/srv/refugebot/servers` is traversable but not listable → `IOException: Permission denied`, rethrown as `UnauthorizedAccessException` |
| 9 | nothing catches it, it escapes into game code that never expected `File.Exists` to throw, and `GameManager.Awake` dies |

Steps 7–9 are taken from the operator's own stack trace, attached to #89. The crash is **not** in
`ValidateModInfos`, which is where this plan first placed it: `[MODS] Loading done` is logged one
millisecond before the exception, and the frames below `Exists` are
`ModManager+<LoadLocalizations>d__19.MoveNext()` → `ThreadManager.RunCoroutineSync` →
`GameManager.Awake()`. So the proximate cause is D1 — the replacement throwing where the replaced
method could not — and `ValidateModInfos` is a second, independent route to the same throw.

The same trace settles one implementation question. There is no `MoveNext` frame above
`EnumerateFileSystemEntries`: under Mono it builds the enumerable, the enumerator and the directory
handle synchronously, so the throw comes from the **call**, not from the first step of the loop. D1
keeps both inside one `try`, which is correct under either behavior.

Either fix alone breaks the chain. Both are real defects and both are worth fixing: #90 is why the feature ran
at all, #89 is why running it was fatal.

### A second consequence of walking from the root, found while building the baseline

The first baseline run put the scratch tree under a lower-cased temp path. The pre-fix code did not reach the
blocked directory at all — it returned `false` after reporting a casing mismatch on `Users` vs `users`, eight
segments above anything a mod owns.

That is worth stating plainly: walking from the root makes **the machine's own path spelling** part of the
check. Wherever the path the game reports for itself is spelled differently from the path on disk — a launcher
passing a lower-cased argument, a mount point, a symlink target — every mod fails `ValidateModInfos` and every
mod is unloaded, with an error message blaming the modder's casing. Anchoring at the game directory removes
that class outright, because the anchor supplies the authoritative spelling of everything above it.

## 2. Design

### D1 — `Exists` never throws (#89)

It is transpiled in to *replace* `File.Exists`/`Directory.Exists`/`SdFile.Exists`/`SdDirectory.Exists` inside
four game methods. Those BCL methods swallow every error and return `false`; game code calling them carries no
handler. A replacement for a total function must itself be total. Any unexpected failure logs once and degrades
to the BCL answer.

### D2 — the walk starts at a declared content root, not at `/` (#89)

Casing above a content root is not mod-authored — the game resolved that prefix itself — so verifying it
catches no modder's mistake. It costs one full directory listing per ancestor on a method whose own comments
call it "called frequently", and on a shared host it reads directories belonging to other tenants.

**Which roots, grounded in the game's own code.** The first draft of this plan anchored at
`GameIO.GetGamePath()` alone. Reading `ModManager` showed that is wrong, not merely narrow. `LoadMods()`
loads mods from **two** independently resolved folders and skips the second only when
`GameIO.PathsEquals(first, second, ignoreCase: true)` says they are the same place:

| Root | Resolves to | Where it actually lives |
|------|-------------|-------------------------|
| `ModManager.ModsBasePath` | `GameIO.GetDeviceLocalUserGameDataDir() + "/Mods"` | the **user data** directory, resolved through the platform layer (`Platform.IPlatform.UserDataRoaming`, `UserDataStorageType`) — on a dedicated server, wherever the user data folder points, routinely another disk or mount |
| `ModManager.ModsBasePathLegacy` | `Application.dataPath + "/../Mods"` | the game directory's `Mods` folder |

So the game's *primary* mods folder is outside the game directory by default. Anchoring at the game directory
alone would leave every mod under `ModsBasePath` walking from the filesystem root — the exact behavior #89 is
about. The anchor set is therefore both mod roots plus the game path (for the game's own `Data` and
`Config`), normalized with the game's own `GameIO.GetNormalizedPath`, de-duplicated case-insensitively to
match `PathsEquals`, and sorted deepest-first so a mods folder nested inside the game directory wins.

A path under none of them still falls back to the path root. That is wasteful but no longer fatal, because D1
absorbs whatever the walk hits.

**A related fact worth recording:** the game compares its own two mod roots with `ignoreCase: true`. The game
does not assume case-sensitive matching for mod paths; it deliberately ignores case there.

### D3 — an unlistable directory is *unverifiable*, not *absent* (#89)

Returning `false` is not an acceptable degradation: `ValidateModInfos` unloads a mod on `false`, so a
permission quirk anywhere above a mod folder would silently disable every mod on the server. The segment goes
unchecked, the walk continues below it, and the directory is reported once.

### D4 — normalize before flipping case (#90)

`IsFilesystemCaseInsensitive` normalizes first and then flips **only the leaf segment**, which cannot be
cancelled by a later `..`. It also refuses to conclude anything when the leaf has no letters to flip. The
`DirectoryNotFoundException` at `Config.cs:16` goes: a gate deciding whether an optional feature runs must not
be able to abort mod loading. Unsure now answers `false` — losing a dev-machine diagnostic, rather than
enabling an expensive and destructive walk in production.

### D5 — a seam that is a real function, not a test hook

`IsFilesystemCaseInsensitive()` becomes `IsCaseInsensitiveAt(GameIO.GetGamePath())`, and the flip itself
becomes `OppositeCaseSpelling(path)`. Both are meaningful on their own terms and both are reachable from a test
without a live `GameIO`.

## 3. Verification

- `dotnet build StrongMods/StrongMods.csproj -c Debug`
- `dotnet test StrongMods.sln -c Debug`

New tests in `Tests/ModLogic/CaseSensitiveFilesystemTests.cs`, running the real compiled methods in a
`ModLogicHost`:

| Test | Discriminates on |
|------|------------------|
| a mis-cased segment below the anchor is still caught | both platforms |
| a mis-cased segment above the anchor is ignored | both platforms |
| a mod root outside the game directory anchors too | both platforms |
| the check still bites below that second root | both platforms |
| an unlistable ancestor does not throw and does not report absence | both platforms (Windows deny ACE / Unix mode `--x`) |
| hostile input returns `false` instead of throwing | both platforms |
| the opposite-case spelling survives normalization | both platforms |
| the probe gives the same answer for normalized and unnormalized spellings | case-sensitive filesystems only |

`Tests/Fixtures/UnlistableDirectory.cs` induces the real permission state — a Windows deny ACE on
`ListDirectory`, or `File.SetUnixFileMode(dir, UserExecute)` on Unix — and verifies the induction took effect
before the test relies on it, skipping if it did not (running as root on Unix ignores mode bits). Both are
reversible, so cleanup is safe.

The last row is honest about its limit: on a case-insensitive filesystem every spelling resolves, so the #90
behavior cannot differ there. CI runs `ubuntu-latest`, which is both case-sensitive and the production
platform, so the check does gate. The row above it is white-box precisely so that the defect is caught on the
Windows dev machine too.

## 4. Deliberately out of scope

Found during the sweep, filed, not touched here:

- **#91** — `Exists` answers "an entry with this name exists", so it conflates files and directories in all
  four calls it replaces. Silent wrong answers inside game code. Independent of this plan and survives it.
- **#92** — `CustomChatCommands.InitMod` does unguarded save-directory I/O.
- **#93** — `StrongUtils`' `GameAwake` handler does the same.
- **#94** — the convention test that would have caught #89, #92 and #93 statically.

Lower-value findings not filed: `build/tools/pack.cs:75` and `push.cs:56` omit `UnauthorizedAccessException`
from their top-level exception filter (it derives from `SystemException`, not `IOException`), so a CI-gating
tool reports an unhandled stack trace instead of its `error:` contract. `build/tools/settings_lint.cs` has no
top-level handler at all. `StrongDev/.ai/tools/buildtree.cs:28` trims `'\\'` off `Path.GetPathRoot`, which
degenerates to always-true on Linux. All dev-tooling only.

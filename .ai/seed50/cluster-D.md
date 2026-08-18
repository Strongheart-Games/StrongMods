# Cluster D — chat/command mods + the foundational runtime mod

Method per BRIEFING.md: test ideas first (subject → assertion), then each tagged with the tier/tool it needs.
Scope claims are V3.1.0 (b14). Everything below is read-from-source unless marked inferred. Paper analysis only —
nothing was booted.

Tags: **[T1]** server-side/telnet · **[T2]** client→server protocol · **[IL]** patch-target resolution ·
**[UNIT]** off-game logic · **[LINT]** XML well-formedness · **[IN-GAME]** needs #49 runner / real client.

---

### ChatCommandHelper  (shape: code; side: server)
Harmony-patches `ModEvents.ModEventInterruptible<SChatMessageData>.Invoke` (prefix hides/gates commands before vanilla,
postfix announces async/unrecognized after) and `WorldEnvironment.OnXMLChanged` (loads hidden/privileged/async command
lists + authorized-cvar name from world-environment XML properties). Authorization = sender's `strongsworn` cvar != 0;
server (null player) is always authorized. Read from `ChatCommandHelper.cs`.

- [IL] Test idea: both `[HarmonyPatch]` targets — the **generic nested** `ModEventInterruptible<SChatMessageData>.Invoke`
  and `WorldEnvironment.OnXMLChanged` — resolve against the unit → smoke asserts each binds. Tool: **existing** (SmokeTests
  `[HarmonyPatch]` resolution). The generic-nested target is the fragile one; this is its only guard.
- [UNIT] Test idea: `TryGetCommand("/tp home")` → `"tp"`; `TryGetCommand("/tp")` (no space) → `"tp"`; `"hello"` → false;
  `"/"` and `"/ "` → false (whitespace guard). Tool: **GAP — private-static logic has no seam** (make `internal` +
  `InternalsVisibleTo`, or extract a parser class). The `Substring(1, iSpace>0 ? iSpace-1 : len-1)` index math is a
  bug magnet and deserves a pure test.
- [UNIT] Test idea: `ParseList` on `"a, b ,,c"` → `["a","b","c"]` (comma-split, trimmed, empties dropped); empty string →
  empty list. Tool: **GAP — same private-static seam** as above.
- [T1] Test idea: a player whose `strongsworn`=0 sends a privileged command → gated whisper is returned and vanilla is
  stopped (`StopHandlersAndVanilla`); same command with cvar=1 passes through. Tool: **GAP — telnet chat-injection as a
  named authenticated player with a set cvar, and whisper read-back** (see cross-cluster gap #2). T2 can't: no `entityId`.
- [T1] Test idea: after editing world-environment XML, `OnXMLChanged` postfix reloads the three lists so a newly-hidden
  command starts being suppressed. Tool: **[T1]** boot + trigger reload + observe (or [IN-GAME]); the parse itself is the
  [UNIT] row above.

Notable gaps: private-static parser seam; telnet chat-as-player injection + whisper read-back.

---

### CustomChatCommands  (shape: code; side: server)
Loads command definitions from `Saves/.../StrongMods/custom_chat_commands.xml` (auto-creates a skeleton if absent),
hot-reloads them via `FileSystemWatcher`, matches the first chat token against triggers/aliases (case-insensitive),
gates on admin level + cvar `Requirements`, and runs `console`/`whisper`/`broadcast` actions with `{Name}`/`{EntityId}`/
`{PlatformId}`/`{EOSId}` substitution. No Harmony patches — subscribes via `ModEvents.ChatMessage.RegisterHandler`. Read
from `CommandManager.cs`, `CommandEvaluator.cs`, `CommandProcessor.cs`, `ModApi.cs`, `ChatCommandSender.cs`.

- [UNIT] Test idea: `CommandManager.Init(tempXmlPath)` on a file with `trigger`, `aliases="a,b"`, `minAdminLevel`,
  `Requirements`, `Execute/Action` → `Commands` is keyed (OrdinalIgnoreCase) by trigger **and every alias**, with parsed
  actions and requirements. Tool: **existing (UNIT with a Log seam)** — `Init(path)` is public static; harness already
  shims `Log.*` (README tier 2). Highest-value new [UNIT] in this mod.
- [UNIT] Test idea: `minAdminLevel` defaults to **1000** when the attribute is absent or unparseable; a command with an
  empty `trigger` is skipped. Tool: **existing (UNIT)** — same `Init(tempfile)` seam. Guards the default that governs who
  can run a command.
- [UNIT] Test idea: an `<Action type="bogus">` is dropped (`Enum.TryParse` fails) while `type="Console"`/`"whisper"`
  parse case-insensitively; malformed XML → `Commands` unchanged and an error logged, not a throw. Tool: **existing
  (UNIT)** via `Init` + captured log.
- [T1] Test idea: `CommandEvaluator.CheckRequirements` — a player at admin level 1 fails a `minAdminLevel=0` command
  (lower number = more privileged in 7DtD; the check is `playerAdminLevel > MinAdminLevel`), passes at level 0; a cvar
  requirement matches via `Mathf.Approximately`. Tool: **[T1]** (needs `EntityPlayer`/`AdminTools`/`GameManager`), or
  **GAP** for a constructible player/admin double. This encodes the most bug-prone rule and inverted-scale semantics.
- [UNIT] Test idea: `CommandProcessor.ReplaceVariables("hi {Name} {EntityId}", sender)` substitutes all four tokens.
  Tool: **GAP — needs a constructible `ClientInfo`/`EntityPlayer`** (both game types with populated fields); off-game
  today only the null-ClientInfo branch is reachable, and that path dereferences `GetEntityPlayer()`.
- [T1] Test idea: rewriting `custom_chat_commands.xml` on a live server → the `FileSystemWatcher` reloads and the new
  trigger fires. Tool: **GAP — deterministic hot-reload trigger** (watcher debounce + private `LoadCommandsFromXml` make
  this timing-flaky as [UNIT]); [T1] rewrite-and-observe, or a seam to invoke reload directly.

Notable gaps: constructible `ClientInfo`/`EntityPlayer` for substitution + requirement tests; deterministic hot-reload;
**event-handler API-drift** (see cross-cluster gap #5) — nothing verifies `ModEvents.ChatMessage`/`SChatMessageData`
still exist, because the [IL] smoke suite only resolves `[HarmonyPatch]`/`[PatchTargetManifest]`, not event subscriptions.

---

### StrongFill  (shape: code; side: server)
A craftable terrain-gap filler block. `BlockStrongFill : Block` overrides `LateInit`/`GetTickRate`/`OnBlockAdded`/
`UpdateTick`; on tick it inspects the 8 neighbors of the block below and fills terrain gaps — cardinals if the neighbor
is a `Shape`, diagonals only if the diagonal **and both adjacent cardinals** are shapes — then consumes itself (leaves
itself if nothing to fill). The custom class is bound server-side via StrongMods' `ServerOnlyClass` (XML `ServerOnlyClass`
property copied into `Class` at `BlocksFromXml.CreateBlock`). No Harmony patches of its own. Read from `BlockStrongFill.cs`,
`Config/blocks.xml`, `StrongMods/ServerOnlyClass.cs`.

- [T1] Test idea: booting a server with StrongFill deployed logs `[ServerOnlyClass] Setting class for strong_fill to
  StrongFill.StrongFill, StrongFill` and `strong_fill` resolves to `BlockStrongFill`, not base `Block`. Tool: **[T1]**
  (log-scrape of the ServerOnlyClass line — the briefing's named assertion). Confirming the *bound C# class* beyond the
  log line is a **GAP** (no telnet read-back of a block's runtime class).
- [IL] Test idea: `ServerOnlyClass`'s `BlocksFromXml.CreateBlock` patch resolves against the unit. Tool: **existing**
  (SmokeTests, lives in StrongMods). Note StrongFill itself contributes **no** [IL] rows (no `[HarmonyPatch]`); its
  `Block` base-method overrides and game symbols (`MarchingCubes.DensityTerrainHi`, `EAutoShapeType.Shape`,
  `WorldBase.SetBlocksRPC`) are guarded only by **compilation against the game assemblies** (CI builds both units) — worth
  stating as the actual drift guard rather than a test.
- [UNIT] Test idea: the neighbor rule — with N and E shapes, NE fills; with NE a shape but N or E not, NE does **not**
  fill; a non-terrain center returns no changes. Tool: **GAP — a `WorldBase`/block-grid test double.** `Fill`/`IsShape`
  read `GameManager.Instance.World.GetBlock`, so the richest pure logic in the mod is untestable off-game as written.
- [T1] Test idea: place `strong_fill` one block above a single-block terrain gap flanked by shapes, advance its scheduled
  update, assert the gap is filled with terrain and the filler block is consumed (Air); with nothing to fill, the block
  remains for pickup. Tool: **GAP — telnet block placement + scheduled-block-update advancement + block read-back**;
  otherwise [IN-GAME] (#49).
- [LINT] Test idea: `Config/blocks.xml` (and `recipes.xml`) are well-formed and the `append` xpath is valid. Tool:
  **existing** (build-time XML lint); structural validity of the `ServerOnlyClass` property is #41 territory.

Notable gaps: `WorldBase`/block-grid double for the fill geometry; telnet place-block + tick + read-block harness;
block-class-binding assertion beyond log-scrape.

---

### StrongMods  (shape: code; side: server) — the foundational runtime mod
Two subjects in scope: the **breadth-first XML patcher** (`BreadthFirstXmlPatcher` — replaces `WorldStaticData.LoadAllXmlsCo`
with a mod-major pass; 3-phase design, `LoadAndPatchConfig` prefix serves pre-patched files from a cache) and the
**`<foreach>` templating engine** (`XmlPatchMethodForeach`, spec `StrongMods/Docs/foreach.md`) + `[XmlPatchFunction]`.

#### Existing coverage (read from `Tests/`)
- **Foreach\ (6 files) is a thorough spec-conformance suite** keyed to `foreach.md`: loop basics (document order, body-xpath-
  targets-file, nesting reads outer bindings, `as` validation, `xpath`/`as` required, name-reuse error); `<bind>` (inline
  rows, xpath-without-source, resolved-once/constant, inline+xpath error, name collision, no-match/dup-key skip, default
  row); `<function>` (callable, args-are-expressions, null-skip, null-falls-through, throw-skip, untagged rejected,
  omit-mod → Assembly-CSharp, unloaded-mod, malformed-ref, name collision, wrong arg-count/signature, no nested/no-`?:`
  args); interpolation (attr+text, scalar-never-skips, zero/2+-match skip, `?:` fallback, `foreach-name`, doubled braces,
  empty-attr-is-a-match, one-`?:`-only); failure modes (invalid substituted element name → skip, unknown source → error,
  bad xpath, unbound name, unknown function, malformed expression, `Extends`-not-resolved); cross-file (`source` on
  foreach and bind, `.xml`-suffix error).
- **Patcher\**: `PatcherCacheTests` covers the `LoadAndPatchConfig` prefix + cache seam (case-insensitive lookup, serve +
  suppress vanilla, `.xml`-tolerant key, consumed-entry removal, out-of-pipeline fall-through, failed-base-load marker).
  `PatchApplicationTests` replays every mod's real `Config\` patches against real vanilla per declared label.
- **[IL] smoke**: all StrongMods `[HarmonyPatch]` targets (`LoadAllXmlsCo`, `LoadAndPatchConfig`, `ServerOnlyClass`,
  `CaseSensitiveFilesystem`, `ModUnloader`) resolve.
- **Explicitly NOT covered** (stated in `PatcherCacheTests`/`PatchPipeline` comments): the phase-1/phase-2 **coroutine**
  itself — real file loading, the loaded-mod list, and frame timing — is called in-game territory; the replay fixture runs
  mods in **directory order, not deploy load order**.

#### Gaps — highest-value tests NOT yet covered
- [IN-GAME]/[T1] Test idea: **cross-mod load-order visibility** — mod A (earlier) adds an item; mod B's `<foreach
  source>` sees it, and a mod C loaded *after* B is invisible to B's loop (the spec's headline "mods after you are
  invisible" table). Tool: **GAP — the patcher's central guarantee is unverified.** The replay fixture runs directory-order,
  so it cannot assert load-order semantics; needs either a **seam to drive phase 2 with a synthetic ordered mod list** or
  a multi-mod [IN-GAME] boot. This is the deepest gap in the repo's most foundational mod.
- [UNIT] Test idea: **mid-patch, same-file visibility** (foreach.md Example 1) — an `<append>` adds `example_item_3`, then
  a later `<foreach>` in the same file selects it and gives it properties. Tool: **existing PatcherHost `Apply`** (no new
  tool). Asserts "reads the file as it stands right now, mid-patch," which no current test covers.
- [UNIT] Test idea: **body-command variety** — `remove`, `set`, `insertBefore`, `insertAfter` used as foreach bodies
  behave per-iteration (only `append`/`setattribute` are exercised today, though the spec lists all six). Tool: **existing
  PatcherHost `Apply`**.
- [T1]/[IN-GAME] Test idea: **malformed-patch resilience in phase 2** — one mod's malformed `items.xml` is caught, logged,
  and the loop continues to that mod's other files and to later mods (the phase-2 `try/catch` per file). Tool: **GAP —
  same synthetic-mod-list/coroutine seam** as the load-order gap; the per-`Apply` harness can't span mods/files.
- [UNIT]/[IN-GAME] Test idea: **eligibility filters** — `GameConfigMod == false` mods are skipped in phase 2; a file with
  `IgnoreMissingFile` absent is skipped without error; `onlyXmls`/`LoadAtStartup` narrow the set. Tool: **GAP — coroutine
  seam** (these filters live in `LoadAllXmlsBreadthFirstCo`, above the tested prefix/cache layer).
- [UNIT] Test idea: **`<bind>`/`<function>` must be a direct child of `<foreach>`** — one placed elsewhere is rejected;
  and a loop combining a `<bind>` and a `<function>` in the same scope resolves both. Tool: **existing PatcherHost
  `Apply`** — small structural gaps at the edge of the current suite.

Notable gaps: a **seam to exercise the breadth-first coroutine / phase-2 ordering** with a synthetic, ordered, multi-file
mod list — the single missing capability behind load-order visibility, cross-mod malformed-patch resilience, and the
eligibility filters. Everything downstream of the cache is already well covered; everything *at and above* the coroutine
is not.

---

## Distinct tool gaps this cluster exposes
1. **Breadth-first coroutine / phase-2 ordering seam** (StrongMods) — drive the mod-major pass with a synthetic ordered
   multi-mod, multi-file set to assert load-order visibility ("mods after you are invisible"), cross-mod malformed-patch
   resilience, and eligibility filters (`GameConfigMod`, `LoadAtStartup`, `IgnoreMissingFile`, `onlyXmls`). The patcher's
   headline guarantees are its least-tested code. Alternative: multi-mod [IN-GAME] boot (#49).
2. **Telnet chat-injection as an authenticated named player** — inject a chat message as a player with a chosen cvar /
   admin level and read the resulting whisper/console effect back. Needed by both chat mods' [T1] end-to-end tests; T2
   can't reach it (no `entityId`/spawn).
3. **Private-static logic seams** — ChatCommandHelper's `TryGetCommand`/`ParseList` are pure parsing with no test hook
   (make `internal` + `InternalsVisibleTo`, or extract). Cheap [UNIT] wins blocked purely by visibility.
4. **`WorldBase`/block-grid test double** — StrongFill's `Fill`/`IsShape` neighbor+diagonal geometry (the mod's richest
   logic) reads `GameManager.Instance.World.GetBlock`, so it's untestable off-game without a fake block world.
5. **Event-handler / game-API-usage drift coverage** — CustomChatCommands depends on `ModEvents.ChatMessage`/
   `SChatMessageData` via `RegisterHandler`, not `[HarmonyPatch]`, so the [IL] smoke suite (which only resolves Harmony/
   manifest targets) never checks that API survives a game update. A resolver for non-Harmony game-API usage, or a
   manifest for event subscriptions, would close it.
6. **Telnet block lifecycle harness** — place a named block at a position, advance its scheduled block-update, and read
   the resulting block values back (StrongFill end-to-end, and block-class-binding confirmation beyond log-scrape).
7. **Constructible `ClientInfo`/`EntityPlayer` doubles** — CustomChatCommands' `ReplaceVariables` token substitution and
   `CheckRequirements` need populated game player/admin objects; only the degenerate null-ClientInfo path is reachable
   off-game today.

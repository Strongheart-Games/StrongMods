# Does a mod's Localization.csv merge on a dedicated server, and reach the client? — static analysis

**Scope of every claim below: 7 Days to Die `V3.1.0 (b14)`, dedicated server + game client.** This is the **static
(IL) half** of wayfinder ticket #85. Every finding here is read from the shipped assemblies; the runtime half (boot a
dedicated server with a localization test mod, observe the merge, join a client) is a **separate** task that stays with
the parent — see *The static / runtime boundary* at the end. Findings that read compiled behavior can change with any
game update.

Basis assemblies:

- Dedicated server: `packages/7dtd.assemblies.dedicatedserver/3.1.0.14/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll`
- Game/client: `packages/7dtd.assemblies.game/3.1.0.14/7DaysToDie_Data/Managed/Assembly-CSharp.dll`

Technique: `ilspycmd` v11 whole-method / whole-type decompiles (dumps under `.scratch/loc85/`), the read technique of
`StrongDev/.ai/headless-server-testing.md` and the sibling ledgers.

## Evidence labels

Each finding is tagged **[IL]** (read directly from the shipped assemblies, with type + method + decompiled line cited)
or **[inferred]** (reasoned from IL + architecture). Nothing here is **[measured]** — no server was booted; this is the
static half by design.

## Verdict

**The `isLoadingInGame` hypothesis from #79 is REFUTED.** [IL] The gate on `ModManager.LoadLocalizations` is
`if (_isLoadingInGame) yield break;` — it skips localization loading **only while loading *into* a running game**, and
**proceeds** otherwise. A dedicated server's boot path (`GameManager.Awake` → `LoadPatchStuff(_isLoadingInGame: false)`)
calls it with `false`, so **a dedicated server does load mod localization at boot.** The gate is a general
in-game-only guard, **not** a dedicated-server exclusion (contrast `LoadUiAtlases`, which *is* explicitly
`GameManager.IsDedicatedServer`-gated). Client and server `LoadLocalizations` are byte-identical.

**The real reason #79 saw no `[MODS] Loading localization from mod:` line is the file location, not a server gate.**
[IL] The only mod-localization load path reads **`mod.Path + "/Config/Localization.csv"`** — the `Config/`
subdirectory, *not* the mod root. #79 staged its `Localization.csv` at the mod root (and, in the second run, added a
`Config/` dir containing `blocks.xml`, still not the csv). `SdFile.Exists(mod.Path + "/Config/Localization.csv")` was
therefore `false`, so `loadCsv` was never entered and no log line printed. **#79's negative is a test artifact, not a
characterization of dedicated-server behavior.** [inferred, from the #79 record + the IL path]

**The server→client stream is wired to the same dictionary.** [IL] `NetPackageLocalization` streams
`Localization.PatchedData`, which `Localization.WriteCsv()` builds by serializing + deflating `Localization.patchedCells`
— the exact dictionary that `LoadLocalizations` → `LoadPatchDictionaries` → `loadCsv(_patch: true)` populates. The stream
fires **unconditionally** on every client join (`ConnectionManager.RequestToEnterGame`). So **if** a mod's localization
merges server-side, it **will** reach a joining client.

**Failure-mode-2 verdict (server-only POI name blank / raw key):** static analysis points to **(c) something else** —
specifically that the vanilla load-and-stream path works end-to-end **provided the mod's `Localization.csv` is in its
`Config/` folder**, and #79's null result is explained by file placement rather than a broken or dedicated-server-gated
code path. This is therefore **not a StrongMods-fixable brokenness in the vanilla merge/stream path** and **not** an
obvious Fun-Pimps bug from the static reading. The residual real-world symptom, if any, is most likely a **mod-authoring
pitfall** (placing `Localization.csv` at the mod root, where nothing loads it). Static analysis **cannot** confirm the
path actually fires at runtime or that the merged key renders on a real client — that is the runtime half.

---

## Q1 — Confirm or refute the `isLoadingInGame` hypothesis

**Refuted.** The gate does the opposite of the hypothesis: it skips loading when *in-game*, and a dedicated boot is not
in-game.

`ModManager.LoadLocalizations` [IL, server `srv_ModManager.cs` lines 290-314]:

```csharp
public static IEnumerator LoadLocalizations(bool _isLoadingInGame)
{
  if (_isLoadingInGame)
  {
    yield break;                       // skips ONLY when loading into a running game
  }
  for (int i = 0; i < loadedMods.Count; i++)
  {
    Mod mod = loadedMods.list[i];
    string text = mod.Path + "/Config";
    if (SdDirectory.Exists(text))      // mod must have a Config/ directory
    {
      try { Localization.LoadPatchDictionaries(mod.Name, text, _isLoadingInGame); }
      ...
    }
  }
  Localization.WriteCsv();             // rebuilds PatchedData from patchedCells
}
```

Who calls it, and with what argument [IL, server `srv_GameManager.cs`]:

| Caller | Argument | Line | Runs on dedicated server? |
|--------|----------|------|---------------------------|
| `GameManager.Awake()` → `ThreadManager.RunCoroutineSync(ModManager.LoadPatchStuff(_isLoadingInGame: false))` | **false** | 821 (inside `Awake`, lines 690-826) | **Yes** — `Awake` is unconditional; the `if (IsDedicatedServer)` branch at line 825 only chooses whether *static data* loads synchronously |
| `GameManager.startGameCo(...)` → `yield return ModManager.LoadPatchStuff(_isLoadingInGame: true)` | **true** | 1037 | Yes, but this is the call the gate skips |
| `ModManager.GameEnded()` → `RunCoroutineSync(LoadLocalizations(_isLoadingInGame: false))` | **false** | `srv_ModManager.cs` 344 | end-of-game reload |

`LoadPatchStuff` [IL, `srv_ModManager.cs` 235-239] simply chains `LoadUiAtlases` then `LoadLocalizations`, forwarding
its `_isLoadingInGame` argument.

So on a dedicated boot the flow is `Awake` → `LoadPatchStuff(false)` → `LoadLocalizations(false)` → the
`if (_isLoadingInGame) yield break;` is **not** taken → the per-mod loop runs. The gate does **not** skip localization
on a dedicated server; it skips it only during the in-game `startGameCo` pass (`isLoadingInGame: true`), which exists on
both client and server.

**What #79 actually hit — the `Config/` requirement** [IL]. `LoadLocalizations` passes `mod.Path + "/Config"` to
`Localization.LoadPatchDictionaries`, which appends the filename [IL, `srv_Localization.cs` 222-237]:

```csharp
public static bool LoadPatchDictionaries(string _modName, string _folder, bool _loadingInGame)
{
  checkLoaded(_throwExc: true);
  string text = _folder + "/Localization.csv";       // => mod.Path + "/Config/Localization.csv"
  if (SdFile.Exists(text))
  {
    Log.Out("[MODS] Loading localization from mod: " + _modName);   // the line #79 looked for
    if (!loadCsv(text, _patch: true)) { Log.Error("[MODS] Could not load localization from " + text); }
  }
  ...
}
```

The `[MODS] Loading localization from mod:` line and the subsequent `loadCsv` fire **iff**
`mod.Path + "/Config/Localization.csv"` exists. #79 placed the csv at the mod root; even after adding a `Config/` dir
(with `blocks.xml`), the csv was still not at `Config/Localization.csv`, so `SdFile.Exists` returned false and neither
the log line nor `loadCsv` ran. This fully accounts for #79's negative without invoking any dedicated-server gate.
[inferred, combining #79's staging record with the IL path]

**No mod-root fallback exists.** [IL] A sweep of the whole server assembly for `Localization.csv` /
`LoadPatchDictionaries` / `LoadLocalizations` finds exactly one mod load path — the `Config/`-scoped one above. The only
other references are the vanilla base load (`GameIO.GetGameDir("Data/Config") + "/Localization.csv"`, `srv_Localization`
around line 211) and `Mod.DetectContents()` [IL, server dump lines 218433-218450], which scans `Path + "/Config"` and
explicitly recognizes `Localization.csv` as a `Config/`-folder file. There is no code anywhere that loads a mod's
`Localization.csv` from the mod root.

## Q2 — Dedicated-server-specific, or a general in-game-only guard?

**A general in-game-only guard.** [IL] `LoadLocalizations` has no `IsDedicatedServer` term at all. The contrast is the
sibling method in the same class, `LoadUiAtlases` [IL, `srv_ModManager.cs` 242-247]:

```csharp
public static IEnumerator LoadUiAtlases(bool _isLoadingInGame)
{
  if (GameManager.IsDedicatedServer || _isLoadingInGame) { yield break; }   // atlases: dedicated-server-gated
  ...
}
```

UI atlases are client rendering assets, so the game deliberately short-circuits them on a dedicated server. Localization
is **not** short-circuited that way — its only guard is `_isLoadingInGame`. The design intent is legible in the pairing:
the author gated atlases on `IsDedicatedServer` and chose *not* to gate localization on it.

**Client vs server are identical.** [IL] The game/client assembly's `ModManager.LoadLocalizations` is logically
byte-identical to the server's (same `if (_isLoadingInGame) yield break;`, same `mod.Path + "/Config"` scan, same
`LoadPatchDictionaries` call, same trailing `WriteCsv`). So there is no client/server divergence in the load logic to
explain a server-only failure.

**Does a dedicated server ever call the mod-localization load path, and under what condition?** Yes — at **boot**, via
`GameManager.Awake` → `LoadPatchStuff(false)` → `LoadLocalizations(false)`, for every loaded mod that has a `Config/`
directory containing `Localization.csv`. It does **not** call it during the in-game `startGameCo` pass
(`isLoadingInGame: true`), which is the only pass the gate suppresses. [IL]

## Q3 — Trace the server→client localization stream

The dictionary that gets streamed is exactly the one `LoadLocalizations` populates. Chain, all [IL]:

1. **Populate.** `LoadLocalizations` → `LoadPatchDictionaries` → `loadCsv(text, _patch: true)`
   [`srv_Localization.cs` 617-758] writes into the static `Localization.patchedCells`
   (`Dictionary<string, bool[]>`, declared line 40; populated at lines 723-758).
2. **Serialize.** `LoadLocalizations` ends with `Localization.WriteCsv()` [`srv_Localization.cs` 383-449], which iterates
   `patchedCells` (line 398), writes CSV, deflates it, and stores the bytes in
   `Localization.PatchedData` (`PatchedData = pooledMemoryStream2.ToArray();`, line 449). `PatchedData` is a static
   auto-property with a private setter (lines 129-134) whose **only** writer is `WriteCsv`.
3. **Chunk + send.** `NetPackageLocalization.StartSendingPacketsToClient(ClientInfo)`
   [`srv_NetPackageLocalization.cs` 116-130] reads `byte[] patchedData = Localization.PatchedData;` (line 118), then
   `prepareDataPackets(patchedData)` (line 133, logs `"Preparing Localization chunks for clients"` and
   `"Localization size: {0} B, chunk count: {1}"`) splits it into 128 KiB chunks, and `sendPacketsToClient` (line 169,
   logs `"Starting to send Localization to ..."`) sends each as a `NetPackageLocalization` (`PackageDirection =>
   ToClient`, `Compress => false` because the payload is already deflated by `WriteCsv`).
4. **Trigger on join.** `ConnectionManager.RequestToEnterGame(ClientInfo)` [IL, server dump line 518411] calls
   `yield return NetPackageLocalization.StartSendingPacketsToClient(_cInfo);` **unconditionally** during every player
   join, right after the block/item id-mapping packages and before `WorldStaticData.SendXmlsToClient`.
5. **Apply on client.** Client `NetPackageLocalization.ProcessPackage` [IL, game asm; server-side twin at
   `srv_NetPackageLocalization.cs` 79-109] reassembles the chunks, deflate-decompresses, and calls
   `Localization.LoadServerPatchDictionary(byte[])` [`srv_Localization.cs` 239-249] →
   `loadCsv(_data, _patch: true, _serverData: true)`, merging the server's patched cells into the client's own
   localization dictionary.

So the answer to "what populates the dictionary `prepareDataPackets` reads, and is it the same dictionary
`LoadLocalizations` would populate?" is: **yes, the same one.** `prepareDataPackets` reads `PatchedData`; `PatchedData`
is written only by `WriteCsv`; `WriteCsv` serializes `patchedCells`; `patchedCells` is populated by `loadCsv(_patch:
true)`, which is exactly what `LoadLocalizations` → `LoadPatchDictionaries` calls for each mod's `Config/Localization.csv`
(and also what the vanilla base load and any XML `<set>`-style localization patches feed). **IF a mod's localization
merges server-side, it is carried to a joining client by construction.** [IL]

## Failure-mode-2 disposition (server-only POI name blank / raw key)

Ranked against the ticket's three options:

- **(a) server never loads mod localization at all** — **rejected by IL.** The dedicated boot calls
  `LoadLocalizations(false)`, which runs the per-mod loop; the gate does not exclude dedicated servers.
- **(b) server loads it but does not stream it** — **rejected by IL.** The stream reads the same `PatchedData` that the
  load path writes, and fires unconditionally on join.
- **(c) something else** — **selected.** The static evidence is consistent with the vanilla path working end-to-end
  *when the mod's `Localization.csv` lives in its `Config/` folder*, and with #79's negative being a **file-placement
  test artifact** (csv at mod root, where no loader looks). [inferred]

**StrongMods-fixable, or vanilla behavior?** From static reading, the vanilla merge-and-stream machinery is intact and
not dedicated-server-gated, so there is **no broken vanilla path for StrongMods to patch around** for the basic
"server-side mod name reaches client" case. If a real-world blank-name symptom persists after placing the csv correctly,
it would be a *new* finding requiring runtime evidence; nothing in the b14 IL predicts it. The one genuinely actionable
static observation is the **`Config/Localization.csv` requirement** — a mod that follows the (common) convention of
putting `Localization.csv` at its root will silently never merge. That is a **documentation / mod-authoring** matter, not
a code fix, and worth capturing wherever StrongMods advises mod layout. It is **not** flagged as a Fun-Pimps bug from
static analysis (the behavior looks deliberate — `Config/`-scoped, with `DetectContents` treating `Localization.csv` as
a known `Config/` file).

## The static / runtime boundary

**What this static half establishes:**

- The `isLoadingInGame` gate refuted; the actual gate semantics and the boot-time (`isLoadingInGame: false`) call on a
  dedicated server.
- The exact required file location (`mod.Path + "/Config/Localization.csv"`) and that no mod-root fallback exists — the
  mechanical explanation for #79's null result.
- The full server→client wiring, and that the streamed payload is the same `patchedCells` → `PatchedData` the load path
  fills, sent unconditionally on join.

**What still needs a runtime run (parent's server lane — do NOT do this here):**

1. Boot a dedicated server (V3.1.0-b14) with a test mod that has a valid **`Config/Localization.csv`**, and confirm the
   log emits `[MODS] Loading localization from mod: <name>` followed (on join) by `Preparing Localization chunks for
   clients` / `Localization size: ... chunk count: ...` / `Starting to send Localization to ...`. This closes the
   "does the boot path actually fire and populate `PatchedData`" question that static analysis can only predict.
2. Join a **real graphical client** and confirm the custom key (e.g. a server-only POI's display name) resolves to the
   streamed text rather than showing blank / the raw key. Per #79's boundary note, a Tier-2 protocol client can observe
   the `NetPackageLocalization` bytes on the wire but cannot render UI, so full confirmation of the *displayed* name
   needs a graphical client.
3. Optional but recommended: re-run #79's POI scenario with the csv **moved into `Config/`** to confirm the mode-2
   symptom disappears — directly validating the file-placement explanation above.

## Reproduction (static, this session)

```bash
# from repo root — decompile the relevant types (dumps land under .scratch/loc85/, gitignored):
SRV="packages/7dtd.assemblies.dedicatedserver/3.1.0.14/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll"
GAME="packages/7dtd.assemblies.game/3.1.0.14/7DaysToDie_Data/Managed/Assembly-CSharp.dll"
ilspycmd -t ModManager "$SRV"            # LoadLocalizations / LoadPatchStuff gate
ilspycmd -t Localization "$SRV"          # LoadPatchDictionaries / patchedCells / WriteCsv / PatchedData
ilspycmd -t NetPackageLocalization "$SRV"# StartSendingPacketsToClient / prepareDataPackets
ilspycmd -t GameManager "$SRV"           # Awake (false) vs startGameCo (true) callers
ilspycmd -t ModManager "$GAME"           # client LoadLocalizations — identical to server
# whole-assembly dump used to locate the RequestToEnterGame join caller and Mod.DetectContents:
ilspycmd --nested-directories -o .scratch/loc85/srvdump "$SRV"
```

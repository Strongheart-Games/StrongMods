# Empirical mod-side + EAC-off classifier: prototype and validation

**Scope of every claim below: 7 Days to Die V3.1.0 (b14), dedicated-server unit, Windows.** Every game-behaviour
claim was read directly out of the shipped assemblies with Mono.Cecil — no wiki prose, no community inference.
Findings that read compiled behaviour can change with any game update.

Deliverables: the classifier is [`StrongDev/.ai/tools/modclassify.cs`](../StrongDev/.ai/tools/modclassify.cs); this
document is its evidence base and validation ledger.

## Authorization and scope

Authorized defensive research on 7 Days to Die server-side modding, by the server owner (Strongheart / the
Stronghold community, server "Hades") against their own server and licensed game files. Third-party mods appear
here only as read-only classification subjects taken from the owner's own licensed installs; nothing was modified,
redistributed, or run.

Assemblies read:

- Dedicated server: `packages/7dtd.assemblies.dedicatedserver/3.1.0.14/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll`
- Client / game: `packages/7dtd.assemblies.game/3.1.0.14/7DaysToDie_Data/Managed/Assembly-CSharp.dll`

**This wave is static analysis only.** No dedicated server was launched, and nothing under
`.scratch/tier1/server/Mods/` was created, modified or removed — two other efforts were driving that shared tree.
The runtime half is specified in [§6](#6-the-runtime-validation-plan-specified-not-executed) as a ready-to-run,
**unexecuted** plan. Claims are labelled throughout: **read-from-IL**, **measured**, **inferred**, or
**awaits-runtime**.

## The question

> Can a mod's **side** (client-only / server-only / both) and its **EAC-off requirement** be determined
> empirically — accurately enough to trust — from its artifacts plus server-side runtime observation?

**Verdict: side is decidable in the majority of real cases, and the EAC-off axis is decidable outright once side
is.** The EAC clause is not re-derived here — #77 settled
it against the IL: a mod requires EAC off **iff** a `.dll` lands in the *client's* `Mods` folder, absent
`SkipWithAntiCheat`. That makes EAC-off a pure **function of side**, which is why the classifier computes side
first and EAC second, and why the whole difficulty of the ticket lives in the side axis.

The honest limit is stated up front, because it bounds every accuracy number below: the validation set is
**class-degenerate**. 33 of the 34 labelled mods are server-side, so the ledger demonstrates *absence of false
client calls*, not discrimination. See [§5](#5-validation-ledger).

## 1. Signal sources, and what each actually proves

### 1a. The game's own answer to "does this reach the client?"

`WorldStaticData.xmlsToLoad` is an array of `XmlLoadInfo(string _xmlName, bool _loadAtStartup, bool _sendToClients,
…)`. `WorldStaticData.SendXmlsToClient(ClientInfo)` walks that array and sends a `NetPackageConfigFile` for exactly
the entries whose `SendToClients` is set. **read-from-IL.**

On b14 there are **49 entry points, and 7 have `SendToClients == false`**:

| Entry point | `LoadAtStartup` | Who consumes it | Consequence for a mod that patches it |
|---|---|---|---|
| `rwgmixer` | true | server (world generation) | server-side patch is sufficient |
| `gamestages` | false | server (difficulty/spawn scaling) | server-side patch is sufficient |
| `spawning` | false | server (spawn authority) | server-side patch is sufficient |
| `signs` | false | server | server-side patch is sufficient |
| `loadingscreen` | true | **client** (loading UI) | patch has no effect unless the client has the mod |
| `subtitles` | true | **client** (playback) | patch has no effect unless the client has the mod |
| `videos` | true | **client** (playback) | patch has no effect unless the client has the mod |

The split inside that list is the whole point: `SendToClients == false` alone does not mean "client install
required". Four of the seven are simply data the client never needed. Only the three client-consumed ones are a
client-need signal.

The other 42 — including **all six `XUi_Common/*` and `XUi_InGame/*` entry points** — are sent. So an in-game HUD
or window mod that is pure XML is, mechanically, server-distributable. That is a surprising-sounding result, and it
is independently corroborated: two third-party HUD/inventory mods in the owner's install declare themselves
"Server-Side (EAC-Friendly)" in their own shipped documentation (`AGF-HUDPlus-1Main-v6.4.0`,
`AGF-BackpackPlus-119Slots-v2.0.0`). IL flag and author declaration agree.

**`XUi_Menu` is not in `xmlsToLoad` at all** — it is main-menu UI, loaded by a different path, never sent. A mod
shipping `Config/XUi_Menu/**` therefore needs a client install for that part. This is the strongest single
client-need signal the classifier has.

### 1b. Localization is streamed too

`Localization.csv` looks like it should be client-required, and it is not. `NetPackageLocalization
.StartSendingPacketsToClient(ClientInfo)` reads `Localization.PatchedData` — the runtime, mod-patched dictionary —
chunks it, and streams it to each client; the client's `ProcessPackage` feeds it to
`Localization.LoadServerPatchDictionary`. It is driven from `GameManager.RequestToEnterGame` (and
`NetPackageWorldFolder.ProcessPackage`). **read-from-IL.** A server-side mod's localization reaches players.

### 1c. The game's `GameConfigMod` flag is *not* a side classifier — a rejected signal

`Mod.DetectContents()` sets `Mod.GameConfigMod = true` when the mod's `Config/` contains `XUi_Menu`,
`loadingscreen.xml`, or `Localization.csv`. That reads like a ready-made client-need marker, and it is not one:
its single consumer is `ModManager.AnyConfigModActive()`, whose single caller is
`GameServerInfo.BuildGameServerInfo()`, where it sets `GameInfoBool.ModdedConfig` (value 9) — **a server-browser
advertisement**, not a load requirement. **read-from-IL.**

Using it as written would have mislabelled **ten** of this repo's server-side mods as `both`, purely for shipping a
`Localization.csv` that §1b shows is streamed anyway. The classifier therefore uses only the `XUi_Menu` and
`loadingscreen` members of that trigger set, and for the reason in §1a rather than because the game groups them.

(Aside, consistent with the "registration is not evidence of a live feature"
pattern: `GameInfoBool.RequiresMod` (value 10) exists in the enum, and a scan of every
`GameServerInfo.SetValue(GameInfoBool, …)` call site in the server assembly found writers for values 8 and 9 only.
Nothing sets `RequiresMod`. It cannot be used as a signal.)

### 1d. Two signals that look decisive and are dead

Both were tested and both fail. They are recorded because they are the first two things anyone reaching for this
problem will try.

| Candidate signal | Measurement | Verdict |
|---|---|---|
| Type-set differencing between the game and dedicated-server `Assembly-CSharp.dll` — "the type isn't in the server build, so the mod is client-side" | 7559 vs 7555 types; 32 game-only and 28 server-only, **every one** a Burst codegen artifact (`…$BurstDirectCall`, `…$PostfixBurstDelegate`) under `WorldGenerationEngineFinal` | **Dead.** `XUiC_*`, `EntityPlayerLocal`, `LocalPlayerUI` are all present in both. The predicate never fires. |
| Assembly-reference differencing — "it references a client-only Unity module" | Both `Managed/` directories hold the same **154** file names; `UnityEngine.UIModule.dll`, `UnityEngine.CoreModule.dll` and `UnityEngine.AudioModule.dll` are **byte-identical** (SHA-256) between units | **Dead.** A mod DLL referencing the UI module loads fine on a dedicated server. |

**measured.** Consequence: client-side code has to be identified by a *marker set over game types*, which is a
curated claim, not by anything the two units disagree about. That is the single biggest reason this classifier is a
prototype needing review rather than a finished oracle.

### 1e. The client-only marker set the classifier does use

- **Derived (597 types on b14):** everything in the XUi hierarchy — every type deriving from `XUiController` or
  `XUiView` (505 of them), plus every type whose name starts with `XUi`. The game's whole UI layer only ever
  instantiates inside a `LocalPlayerUI`.
- **Curated (4), one justification each:** `EntityPlayerLocal` (the local player entity; `WorldBase
  .GetLocalPlayerFromID` returns null on a server), `LocalPlayerUI`, `GUIWindowManager`, `PlayerMoveController`.

Kept deliberately short. An over-eager marker set is exactly how a server-side mod gets mislabelled — see §1f.

### 1f. Why a client-only API *call* is evidence, not a verdict

Two real cases, both in mods that are server-side:

- `StrongUtils.BackpackItemsOnEnterGame.TryGiveItem` calls `WorldBase.GetLocalPlayerFromID`, whose return type is
  `EntityPlayerLocal`. The source shows why: it is a `data.IsLocalPlayer`-guarded fast path for a listen-server
  host, with a `ClientInfo` path right below it for everyone else.
- `ANZ_Quantum_Elevator` (third-party, in the owner's install) calls `EntityPlayerLocal::TeleportToPosition`
  directly — and its own shipped install notes declare the mod server-side, spelling out that only the server or
  host installs it.

A null-tolerant listen-server branch and a genuinely client-side one are **indistinguishable in IL** without
guard-flow analysis. The classifier therefore records client-API contact as a *caveat that lowers confidence*,
never as a client-need. Had it been made decisive, both mods above would be false positives.

### 1g. `ServerOnlyClass` — a declaration, and the strongest signal available

`StrongFill` ships a DLL with **no** `[HarmonyPatch]` and **no** `IModApi`: a single `Block` subclass, wired in via
`Config/blocks.xml`. Nothing about that shape says which side needs it — `blocks.xml` is `SendToClients`, so the
client parses the same `class=` attribute the server does.

Except the property used is `ServerOnlyClass`, which is **not vanilla**: a scan of every `ldstr` in the server
assembly finds `"Class"` in five parsers and `"ServerOnlyClass"` nowhere. It is a StrongMods extension
(`StrongMods/ServerOnlyClass.cs`) — a `BlocksFromXml.CreateBlock` prefix that rewrites `ServerOnlyClass` into
`Class`, so only a machine running StrongMods binds the custom class at all. **read-from-IL + source.**

That makes it an *author's declaration of side*, and the classifier treats it as such: `certain` confidence, no
inference required. It is worth noting as a general principle — where the ecosystem gives authors a way to declare
side, reading the declaration beats every inference in this document.

### 1h. Signal summary

| Signal | Source | Decides | Strength |
|---|---|---|---|
| `Config/XUi_Menu/**` present | file layout + absence from `xmlsToLoad` | client-need | certain |
| `Config/{loadingscreen,subtitles,videos}.xml` | `SendToClients == false`, client-consumed | client-need | strong |
| Asset file (`.unity3d`, image, audio, font) under `Config/`, `Resources/`, `UIAtlases/`, `ItemIcons/` | file layout | client-need | strong (assets are never transferred) |
| Harmony target type in the client marker set | Cecil over `[HarmonyPatch]` attributes | client-need | strong |
| `ServerOnlyClass` in `blocks.xml` | author declaration (StrongMods extension) | server-need | certain |
| Any `Config/<entry point>.xml` | `xmlsToLoad` membership | server-need | strong (the server is the config authority) |
| `IModApi` implementer in a shipped DLL | Cecil | server-need | strong (`Mod.LoadAssemblies` loads every DLL on a dedicated server — #77 clause 2a) |
| `Prefabs/**`, `Worlds/**` | file layout | server-need | strong, but see §7 |
| Call to / return of a client-marked type | Cecil over IL | **nothing** — caveat only | weak (§1f) |
| `Localization.csv` present | — | **nothing** (§1b) | rejected |
| `Mod.GameConfigMod` trigger set | — | **nothing** (§1c) | rejected |
| Type-set / assembly-reference differencing | — | **nothing** (§1d) | dead |

## 2. What the classifier is

`StrongDev/.ai/tools/modclassify.cs`, a file-based C# app in the shape of its siblings `buildtree.cs` and
`tier1slice.cs`: a header comment explaining *why*, derived paths, no user- or machine-specific state.

```bash
dotnet build StrongMods.sln -c Debug                            # stage every mod to bin\Debug — touches no install
dotnet run StrongDev/.ai/tools/modclassify.cs                   # classify every staged mod
dotnet run StrongDev/.ai/tools/modclassify.cs -- <dir>...       # classify arbitrary mod folders (deployed, third-party)
dotnet run StrongDev/.ai/tools/modclassify.cs -- --evidence     # add the per-mod evidence dump
```

It reads a mod folder exactly as the game would — the staged `bin\Debug` folder *is* the shippable mod folder — and
resolves the game tree from `build/GameVersions.props` (`SdtdDevVersion` looked up in `SdtdGameVersionMap`), so
adopting a new version moves the classifier with the repo. The repo root is found by walking up to
`StrongMods.sln`, so it runs from any subdirectory. Mod DLLs are read with Mono.Cecil and never loaded.

Structurally it decides two independent needs — does the **server** need this installed, does the **client** — and
the side is their conjunction, because "both" is not a third thing to detect. Output is a markdown table plus an
explicit *shapes the classifier cannot fully call* section, which is the part §7 is built from.

## 3. Where static analysis suffices, and where it does not

| Question | Static analysis | Why |
|---|---|---|
| Does the mod ship a `.dll`? | **sufficient** | file layout |
| Would that `.dll` force EAC off *if installed on a client*? | **sufficient** | #77's rule plus `SkipWithAntiCheat` in `ModInfo.xml` |
| Does a Config patch reach clients without a client install? | **sufficient** | the `SendToClients` flag is in the IL |
| Does the mod ship content the server never transmits? | **sufficient** | file layout against the known-synced set |
| Does the mod's code patch client-only types? | **sufficient** *modulo the marker set* | Cecil over `[HarmonyPatch]`; the marker set is curated (§1d/§1e) |
| Does the mod actually load on a dedicated server? | **needs runtime** | dependency resolution, load order, `0_TFP_Harmony` presence |
| Did the mod's XML patch actually apply? | **needs runtime** | patch application is a runtime pipeline |
| Is a client-API call a real client-need or a listen-server branch? | **needs guard-flow analysis or runtime** | §1f |
| Does the client actually rebuild its UI from server-sent XUi XML? | **needs a real client** (Tier 2 / manual) | Tier 1 has no client |
| Does a client need local prefab files for a custom POI? | **needs a real client** | §7 |

The boundary in one sentence: **static analysis decides what a mod *contains* and what the engine *would* do with
it; runtime decides whether the mod loaded and whether its patches took.** Tier-1 (telnet) runtime observation can
only ever confirm the *server* half — it has no client — so it validates `serverNeed` and can never falsify
`clientNeed`.

## 4. Method

Mono.Cecil 0.11.6 with a `DefaultAssemblyResolver` pointed at each `Managed/` directory (required before any method
body can be read), plus the unit's `Mods/0_TFP_Harmony` on the search path so mod DLLs' Harmony attributes resolve.
Method bodies read as raw IL: `ldstr` literals, `call`/`callvirt` operands, `ldfld`/`stfld` operands, and the
`ldc.i4*` constants inside `WorldStaticData`'s initializer. Same technique as
[`headless-server-testing.md`](../StrongDev/.ai/headless-server-testing.md) §5c and #76, and the same technique
`Tests/Fixtures/EntryPoints.cs` already uses in production test code — that file is where the `xmlsToLoad` read
came from, extended here to also capture the two flags.

Exploratory probes were written as disposable file-based apps under `.scratch/`; everything they
established that the classifier depends on is restated above with its deciding code named.

## 5. Validation ledger

### 5a. Ground-truth sources, ranked

1. **Observational (strongest).** The Fun Pimps' own placement of their vanilla mods across the two licensed
   installs. `Mods_Vanilla` in the game install holds exactly one mod; `Mods_Vanilla` in the dedicated-server
   install holds three. A mod shipped into both units is `both`; one shipped only into the server unit is `server`.
   This is direct observation of where the vendor puts the files, not inference.
2. **Author-declared.** A third-party mod whose own shipped documentation states its side.
3. **Repo policy.** `CONTEXT.md`: *"I focus on server-side-only mods"* and *"Hades uses only server-side mods."*
   This labels all 28 staged repo mods `server`. It is a policy label, not an independent measurement, and it is
   why the set is class-degenerate.

### 5b. Independent controls (6 labelled + 1 unlabelled)

| Mod | Ground truth | Source | Predicted | Confidence | Result |
|---|---|---|---|---|---|
| `0_TFP_Harmony` | **both** | shipped in `Mods_Vanilla` of *both* installs | server | likely | **MISS** |
| `TFP_CommandExtensions` | server | `Mods_Vanilla` of the server install only | server | likely | hit |
| `Xample_MarkersMod` (`TFP_MarkersExample`) | server | `Mods_Vanilla` of the server install only | server | likely | hit |
| `ANZ_Quantum_Elevator` | server | author-declared in its shipped install notes | server | low | hit |
| `AGF-HUDPlus-1Main-v6.4.0` | server | author-declared ("Server-Side (EAC-Friendly)") | server | low | hit |
| `AGF-BackpackPlus-119Slots-v2.0.0` | server | author-declared ("Server-side (EAC-friendly)") | server | low | hit |
| `ISI_WorldGenTweaker-3.0.0.2` | *(none)* | — | **both** | certain | unlabelled |

The `ISI_WorldGenTweaker` call is the only non-`server` verdict the classifier produced across all 38 folders it
was pointed at. It is unvalidated but well-founded: the mod patches `rwgmixer` (server, world generation) *and*
ships `Config/XUi_Menu/windows.xml`, which rewrites the world-size combobox in the main menu's world-generation
screen — a screen a dedicated server never renders. **inferred**, from artifact semantics.

`AGF-HUDPlus-1Main` and `AGF-BackpackPlus-119Slots` are the two rows that matter most: they are XUi HUD/inventory
mods, exactly the category folklore calls client-side, and both the IL (`SendToClients == 1` for every XUi entry
point) and the authors' own documentation say server-side. The classifier agreed with both.

### 5c. Repo mods (28 rows, all labelled `server` by repo policy)

| Mod | Predicted | Confidence | EAC-off | Deciding signal | Result |
|---|---|---|---|---|---|
| AECInternationalMarketFixes | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| AECVehiclesFixes | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| AuthZ | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| AutoCloseDoors | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| AutoCollectLoot | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| BloodRain | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| BountifulQuests | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| ChatCommandHelper | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| CustomChatCommands | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| DisableLAN | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| DynamicFeralSense | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| DynamicLandClaimCount | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| Hades | server | **low** | no — no `.dll` at all | Config XML (server-distributed) | hit, caveated (world content) |
| LootDiagnostics | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| PlayerSpawnedTraders | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| PootPavillion | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| ProgressiveBiomes | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| ProjectZFixes | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| QuestUnlockFixes | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| RefugeHordeBaseS11 | server | **low** | no — no `.dll` at all | Config XML (server-distributed) | hit, caveated (world content) |
| StrongBoxes | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| StrongFill | server | **certain** | no — no client-side `.dll` | `ServerOnlyClass` declaration | hit |
| StrongholdTweaks | server | likely | no — no `.dll` at all | Config XML (server-distributed) | hit |
| StrongHorns | server | likely | no — no client-side `.dll` | Config XML (server-distributed) | hit |
| StrongLocks | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| StrongMining | server | **low** | no — no `.dll` at all | Config XML (server-distributed) | hit, caveated (world content) |
| StrongMods | server | likely | no — no client-side `.dll` | DLL patch targets | hit |
| StrongUtils | server | **low** | no — no client-side `.dll` | Config XML (server-distributed) | hit, caveated (client-API call, §1f) |

### 5d. Accuracy, stated honestly

- **34 labelled mods, 33 correct, 1 miss** — 97%.
- **33 of the 34 labels are `server`.** A classifier that returned the constant `server` would score 33/34 on this
  set. **The ledger therefore demonstrates the absence of false client/both calls; it does not demonstrate
  discrimination.** Claiming 97% accuracy without that sentence would be a lie by omission.
- The one non-`server` verdict produced (`ISI_WorldGenTweaker` → `both`) has no independent label.
- The one miss is the only `both`-labelled mod in the set. The classifier's client-need detector has therefore
  **never fired on a mod with an independent label**, in either direction.
- 7 of the 34 labelled verdicts carry a confidence downgrade (`low`) from a caveat; 1 is `certain`.
- The EAC-off column is correct for all 34 by construction — every one of them is server-side and none ships a
  client-side `.dll` in its declared deployment — which means the EAC axis is likewise **untested against a
  positive case**. The mechanism behind it is nonetheless independently corroborated: `ANZ_Quantum_Elevator`'s own
  install notes state that a dedicated server runs it with EAC on while a player-hosted machine needs EAC off,
  which is precisely #77's clause 1 + clause 2a.

### 5e. The one miss, and what it teaches

`0_TFP_Harmony` is predicted `server` and is truly `both`. Nothing in its artifacts says otherwise: it ships
thirteen DLLs, implements `IModApi`, declares no Harmony patches on client-only types, and carries no Config, no
assets and no world content. Its side is not a property of its contents at all — **its side is the union of its
dependents' sides**, because it is the Harmony bootstrap every code mod implicitly needs
(`headless-server-testing.md` §3b: a tree without it loads no code mods while looking perfectly healthy).

The general shape is *facility mods*: a mod that exists so that other mods work. `StrongMods` is the same shape
inside this repo — it is predicted `server`, and that is right for how the repo uses it today, but the artifacts
alone would not distinguish it from `0_TFP_Harmony`. Any production version of this tool needs a dependency-closure
pass over `ModInfo.xml`'s `<Dependencies>` before it can call a facility mod's side.

## 6. The runtime validation plan, and its results

**Specified in a first wave, executed in a second.** When this plan was written the shared tree at
`.scratch/tier1/server` was in use by two other efforts and this ticket's runtime half would have required swapping its
`Mods/` contents. R1–R4 were run once the tree came free; **§6e records what actually happened, including one
prediction that was falsified.** R5 remains unrunnable at Tier 1 by construction.

### 6a. What Tier 1 can and cannot settle

Telnet `version` lists the loaded mods (`headless-server-testing.md` §4b), and the server log carries
`Mod.LoadAssemblies`' own lines. So Tier 1 settles **serverNeed**: did the server load this mod, did its DLL load,
did its XML patch take. It has no client, so it can neither confirm nor falsify **clientNeed**. Run R5 below is the
part that has to wait for a real client.

### 6b. Standing preconditions

- The tree must be rebuilt only when free: `dotnet run StrongDev/.ai/tools/buildtree.cs` deletes and recreates
  `.scratch/tier1/server`.
- **Never relocate the tree.** Windows Firewall rules key on the executable's full path
  (`headless-server-testing.md` §6); a new path prompts, and a prompt stalls an unattended run.
- Every mod set must include `0_TFP_Harmony` (§3b of the same doc) — copied, never hardlinked.
- Every repo mod's `ModInfo.xml` declares `<Mod name="StrongMods" version="1.0" />`, and StrongMods unloads mods
  whose declared dependencies are unmet. **Every set below therefore includes `000000-StrongMods`** (the `First`
  load tier's literal prefix), or the run measures dependency unloading rather than side.
- `HideCommandExecutionLog=0` in the config, or the telnet response anchor breaks (§4b).
- Mod content is **copied** into `Mods/`, never hardlinked — hardlinks would write through to the install.

### 6c. The runs

Each run: place the mod set, execute `dotnet run StrongDev/.ai/tools/tier1slice.cs` (which drives `version`,
`gettime`, `listplayers`, `listents`, then `shutdown`), then assert against `.scratch/tier1/FINDINGS.md` and the
run's own `.scratch/tier1/logs/server-<timestamp>.log`.

| Run | Mod set (under `.scratch/tier1/server/Mods/`) | Assertions | What it validates |
|---|---|---|---|
| **R1 — control** | `0_TFP_Harmony` | `version` output lists `Mod TFP_Harmony: <ver>` **and nothing else** | The tree is controlled; later runs' mod lists mean something |
| **R2 — server-side DLL mods load** | `0_TFP_Harmony`, `000000-StrongMods`, `StrongLocks`, `DisableLAN`, `AuthZ` | (a) `version` lists all five mods; (b) log contains a `[MODS]     Loaded assembly` line naming each of `StrongMods`, `StrongLocks`, `DisableLAN`, `AuthZ` — count the four by name, not in total, since `0_TFP_Harmony` alone contributes thirteen; (c) log contains **no** `AntiCheat needs to be disabled`; (d) log contains no `[MODS]` failure line | The classifier's `server` call for four DLL mods, and #77 clause 2a end to end |
| **R3 — XML patches apply server-side** | `0_TFP_Harmony`, `000000-StrongMods`, `StrongFill`, `ProgressiveBiomes`, `AECVehiclesFixes` | (a) `version` lists all five; (b) log contains `[ServerOnlyClass] Setting class for` at least once; (c) the `Block IDs total N` line differs from R2's | One log line proves three things at once: StrongMods loaded, StrongFill's `blocks.xml` patch applied, and the server bound the custom block class — the `certain` verdict of §1g, confirmed |
| **R4 — the facility-mod shape** | `000000-StrongMods`, `StrongLocks` — **no** `0_TFP_Harmony` | (a) `version` still lists both mods; (b) log contains **no** `Loaded assembly` for either; (c) no patch-effect log lines | Demonstrates concretely that `0_TFP_Harmony`'s side follows its dependents (§5e). Re-confirms `headless-server-testing.md` §3b as a classifier control rather than a harness note |
| **R5 — the XUi question** | — | — | **Cannot be run at Tier 1.** Whether a client rebuilds its in-game UI from server-sent XUi XML needs a real client joining a server carrying `AGF-HUDPlus-1Main` and no client-side copy. Tier 2 or a manual two-machine check |

R2's assertion (c) is sharper with `EACEnabled=true` in `.scratch/tier1/config/serverconfig.xml`, but it holds
either way: the gate in `Mod.LoadAssemblies` keys on `GameManager.IsDedicatedServer`, not on the server's EAC
preference (#77 clause 2a). Flip it only if the shared config can be restored afterwards.

### 6d. Exact command sequence for one run

```bash
# once, when the tree is free — deletes and rebuilds .scratch/tier1/server
dotnet run StrongDev/.ai/tools/buildtree.cs

# per run: place the set (copy, never link), R2 shown
cp -r StrongMods/bin/Debug            .scratch/tier1/server/Mods/000000-StrongMods
cp -r StrongLocks/bin/Debug           .scratch/tier1/server/Mods/StrongLocks
cp -r DisableLAN/bin/Debug            .scratch/tier1/server/Mods/DisableLAN
cp -r AuthZ/bin/Debug                 .scratch/tier1/server/Mods/AuthZ

dotnet run StrongDev/.ai/tools/tier1slice.cs

# assertions
grep "Loaded assembly" .scratch/tier1/logs/server-*.log |
  grep -Ec "StrongMods|StrongLocks|DisableLAN|AuthZ"                               # expect 4
grep -c "AntiCheat needs to be disabled" .scratch/tier1/logs/server-*.log          # expect 0
grep -A8 '### `version`'                 .scratch/tier1/FINDINGS.md                # expect all five mods
```

Between runs, remove only the mod folders this plan created — never the tree, never `0_TFP_Harmony`.

### 6e. Results — executed 2026-08-17, lane A, all four runs exit 0

The disposable run artifacts were not retained. The conclusions and measured results are preserved in this report.
Driver `StrongDev/.ai/tools/tier1slice.cs` was unmodified. The tree was never rebuilt and never relocated;
`0_TFP_Harmony` was moved aside for R4 and restored immediately after.

| Run | Assertions | Outcome |
|---|---|---|
| **R1 — control** | `version` lists `Mod TFP_Harmony: 1.1.0.4` and nothing else | **Pass.** The tree is controlled; the later runs' mod lists mean something |
| **R2 — server-side DLL mods load** | five mods listed; four named assemblies loaded; no `AntiCheat needs to be disabled`; no `[MODS]` failure | **Pass, all four.** 17 `Loaded assembly` lines = 13 from `0_TFP_Harmony` + `StrongMods`, `StrongLocks`, `DisableLAN`, `AuthZ`. #77 clause 2a holds end to end |
| **R3 — XML patches apply server-side** | five mods listed; `[ServerOnlyClass] Setting class for`; `Block IDs total` differs from R2 | **Pass, all three.** `[ServerOnlyClass] Setting class for strong_fill to StrongFill.StrongFill, StrongFill`, and `Block IDs total` 24809 (R2) → 24810 (R3) — exactly the one added block |
| **R4 — the facility-mod shape** | (a) both mods still listed; (b) **no** `Loaded assembly` for either; (c) no patch-effect lines | (a) **pass**, (c) **pass**, **(b) FALSIFIED** — see below |

#### R4 falsified its own prediction, and corrects `headless-server-testing.md` §3b

§3b states that a tree without `0_TFP_Harmony` loads **"no code mods at all while looking perfectly healthy — no
error, just silence."** This plan's R4(b) inherited that claim. Measured, it is wrong in both halves:

- **The assemblies do load.** `[MODS] Loaded assembly StrongMods`, `[MODS] Loaded assembly StrongLocks`, and
  `[MODS] Loaded Mod: …` for both.
- **It is not silent — it is loud.** `ERR [MODS] Failed initializing ModAPI instance on mod 'StrongMods' in assembly
  StrongMods` (and the same for `StrongLocks`), each followed by
  `EXC Could not load file or assembly '0Harmony, Version=2.13.0.0, Culture=neutral, PublicKeyToken=null'`, plus
  `ERR Error loading types from assembly StrongLocks` and a `Can't find custom attr constructor` line from
  `XmlPatcher:.cctor()`.

What §3b gets right is the *outcome*: no code mod functions. What it gets wrong is the mechanism and the diagnosis
cost — a harness can detect this condition from the log in one grep, rather than being fooled by a healthy-looking run.

**The consequence for this classifier is the sharper finding: `version` listing a mod proves the folder was read, not
that the mod's code runs.** R4 is the counter-example — both mods appear in `version` while neither `IModApi` ever
initialized. Any runtime `serverNeed` assertion must therefore pair the `version` list with a
`Loaded assembly`/`ERR [MODS]` check on the log; `version` alone is a load-*attempt* signal.

#### A trap in the plan's own assertion command

§6d's `grep -Ec "StrongMods|StrongLocks|DisableLAN|AuthZ"` over whole log lines returns **17, not 4** — every
`Loaded assembly` line embeds the absolute path, and this repo's root directory is itself named `StrongMods`. Count on
the extracted assembly name (`grep -oE 'Loaded assembly [^ ]+'`), never on the whole line.

## 7. Mod shapes the classifier cannot yet call

This list is the deliverable's other half. Each entry is a shape, a concrete instance, and what it would take.

| # | Shape | Instance | Why static analysis stalls | What would resolve it |
|---|---|---|---|---|
| 1 | **Facility mod** — exists so other mods work; side is the union of its dependents' | `0_TFP_Harmony` (the one miss), `StrongMods` | Its own artifacts carry no side information at all | A dependency-closure pass over `ModInfo.xml` `<Dependencies>`, classifying the closure and unioning |
| 2 | **World / prefab content** | `Hades`, `RefugeHordeBaseS11`, `StrongMining` | POIs are placed server-side, but whether a client needs local prefab files for distant imposters or dynamic mesh is not readable from the mod's artifacts | Read the client's prefab/imposter load path from IL, or a two-machine join test |
| 3 | **Guarded client-API use** | `StrongUtils` (`GetLocalPlayerFromID`), `ANZ_Quantum_Elevator` (`EntityPlayerLocal::TeleportToPosition`) | A listen-server fast path and a genuine client-need are identical in IL (§1f) | Guard-flow analysis: is the call dominated by an `IsDedicatedServer` / `IsLocalPlayer` / `IsRemote` test, or null-checked? |
| 4 | **In-game XUi patches** | `AGF-HUDPlus-1Main`, `Z_HUD`, `AGF-BackpackPlus-119Slots` | `SendToClients == 1` says the XML reaches clients; whether the client *rebuilds its UI* from it is a runtime property | Run R5 — a real client. Author declarations currently agree with the IL, which is why these are called `server` today |
| 5 | **Programmatic Harmony targets** | none in this repo's staged set, but `[PatchTargetManifest]` exists for exactly this | `TargetMethod` / `TargetMethods` / `harmony.Patch(...)` targets are computed at runtime | Invoke the provider headlessly, as `Tests/TargetResolver.cs` already does |
| 6 | **String-named patch targets** | — | `AccessTools.TypeByName("…")` hides the type from Cecil's operand graph | Constant-propagate `ldstr` into the resolver call, or invoke headlessly |
| 7 | **XML-instantiated DLL classes without a declaration** | none in this repo (`StrongFill` declares `ServerOnlyClass`) | `blocks.xml` is `SendToClients`, so the client parses the same `class=` attribute and will fail to resolve a type it does not have; whether that failure is benign is unknown | Read `BlocksFromXml.CreateBlock`'s unresolved-class behaviour from IL, then a client test |
| 8 | **Client-only mods that ship neither DLL nor asset** | none observed | A purely cosmetic client-side XML mod is artifact-identical to a server-side one | Nothing static can distinguish them; needs the author's declaration |
| 9 | **Split-deployment mods** | — | A mod shipped as two folders (server part, client part) is two classifications, and the tool sees one folder at a time | A grouping convention, or reading the mod's install documentation |

Two further limits, not per-mod:

- **The client-only marker set is curated, and §1d proves it has to be.** 597 types is a claim, not a measurement.
  Every wrong entry is a potential false `client`; every missing entry is a potential false `server`.
- **`EACEnabled` is a property of the deployment, not of the mod.** The classifier's EAC column answers "installed
  as its side says". Install a server-side DLL mod into a client's `Mods` folder anyway — as a single-player or
  listen-server setup does — and that client needs EAC off. The mod did not change side; the operator put a DLL
  somewhere the mod does not require it.

## 8. What stands between this and a shipped StrongDev tool

Engineering-wise, little. The gap is review of the judgement calls, in this order:

1. **The curated client-only marker set** (§1e) — 597 types, of which 4 are hand-picked. Every name is a claim.
2. **The rejected signals** (§1c, §1d) — three signals were dropped on evidence; confirm the reasoning holds.
3. **The caveat-not-verdict choice for client-API contact** (§1f) — deliberate conservatism, with two concrete
   mods that would otherwise be false positives.
4. **The `server` default for a DLL with an `IModApi` and no client markers** — right for this repo, and the source
   of the `0_TFP_Harmony` miss.
5. **The unexecuted runtime plan** (§6) — four Tier-1 runs, ready to go.

Nothing on that list is more engineering. Items 1–4 are decisions a human should agree with before the tool's
output is trusted; item 5 is a second wave of the same effort.

# Headless server testing: what we know

The empirical record behind StrongDev's test harness. Everything here was measured or read out of the game's own
assemblies — no community documentation, no inference from wiki prose.

**Scope of every claim below: 7 Days to Die V3.1.0 (b14), dedicated server, Windows.** Findings that read the
game's compiled behavior can change with any game update. The *How these findings were obtained* section at the
end exists so any of them can be re-derived in minutes rather than re-discovered in hours.

## 1. Two tiers of headless driving

| Tier | What it is | Needs | Good for |
|---|---|---|---|
| **1 — console** | Spawn a dedicated server, drive it over its telnet console, assert on server-side state | Nothing beyond the server itself | Anything whose subject is server-side behavior — which is most mods in this repo |
| **2 — protocol client** | A managed client speaking the game's own protocol at the server | A reimplemented handshake (see §5) | Tests whose subject *is* the client-side path: packet rejection, cheat detection, ownership derived from a real authenticated player |

Tier 1 is proven end to end (§4). Tier 2 is researched but unbuilt (§5).

The choice is per-test, not global. Worked example — StrongLocks, whose two patches are both
ownership-independent (`GameManager.ChangeBlocks` locks any `TEFeatureLockable`; `World.SpawnEntityInWorld` locks
any `EntityVehicle`): no player identity is needed to assert either, so it is entirely a Tier-1 subject. An
AuthZ-style "server rejects a client claiming another user's id" test is inherently Tier 2, because the thing
under test is the validation that only a real sender can exercise.

## 2. Controlling the mod set requires its own game tree

This is the finding that shapes the whole harness.

`ModManager` resolves **two** mod directories:

```
ModsBasePath        (property) = GameIO.GetDeviceLocalUserGameDataDir() + "/Mods"
ModsBasePathLegacy  (static)   = Application.dataPath + "/../Mods"      // "/../../Mods" on the other layout
```

and `ModManager.LoadMods` scans **both** whenever they differ:

1. `loadModsFromFolder(ModsBasePath)`
2. if `!GameIO.PathsEquals(ModsBasePath, ModsBasePathLegacy)` → `loadModsFromFolder(ModsBasePathLegacy)`

**Consequence: redirecting user data can only *add* a mods directory, never exclude the install's.** There is no
launch-argument override for the mods path.

`UserDataFolder` *does* work for what it covers — it is a real `LaunchPref`, and setting it (we used the
`serverconfig.xml` property) redirects saves, logs and generated worlds. Confirmed in a live log at 0.035s:
`INF Overriding default user data path to <scratch>\userdata`. It just doesn't move mods, and it does **not**
move where the engine finds its assemblies: `Application.dataPath` is Unity's own and follows the *executable*,
so the two roots are genuinely independent.

Since `ModsBasePathLegacy` derives from the executable's location, **giving the exe a new home with a real
`Mods/` beside it is what actually controls the mod set.**

> Cost of learning this the hard way: a first run against the live install hung at 3.2s because a third-party mod
> in the live deploy entered an interactive first-boot setup mode and startup never progressed. A test server must
> not inherit whatever happens to be deployed.

## 3. The harness recipe

### 3a. Build the tree with hardlinks, not junctions

`.ai/tools/buildtree.cs` builds a scratch server tree: **17,897 files hardlinked in 1.8 seconds**, consuming no
meaningful disk.

**Hardlinks over directory junctions, deliberately.** A recursive delete of a hardlinked tree removes only those
names — the install's own directory entries keep the data alive. A recursive delete *through a junction* can
destroy the target, which here is a 17 GB game install. Windows tools disagree about whether to follow junctions
when deleting, so this is not a hypothetical.

The inherent hardlink caveat: linked files **are** the install's bytes, so anything writing to one *in place*
writes to the install too. Safe for `Data/` and the engine assemblies, which the game only reads. It is exactly
why mod content is **copied**, not linked.

Verification that it worked and destroyed nothing: `fsutil hardlink list <file>` shows both names, and the
install's `ls -la` link count going 1→2 with every size, timestamp and permission unchanged is the proof.

What the tree contains: the root files, and `7DaysToDieServer_Data`, `Data`, `Dependencies_3.0`,
`MonoBleedingEdge`, `Licenses`, `Logos` linked from the install — plus a **real `Mods/`** holding only what the
test needs. Deliberately absent: the live `Mods/`, and any third-party mod state.

### 3b. `0_TFP_Harmony` is mandatory

Every code mod depends on it implicitly. A tree without it loads **no code mods at all while looking perfectly
healthy** — no error, just silence. `Mods_Vanilla/` in the install holds a pristine copy; that is the better
source than the live `Mods/`.

The minimum viable test mod set is therefore `0_TFP_Harmony` + whatever is under test. This is a rule the harness
should encode, not something left to whoever assembles a mod set by hand.

### 3c. Server configuration

Copy the stock `serverconfig.xml` and edit the copy; launch with `-configfile=<copy>`. Arguments follow the
stock `startdedicated.bat`: `-logfile <path> -quit -batchmode -nographics -configfile=<path> -dedicated`.

Overrides that matter: `UserDataFolder` → scratch (uncomment it; it ships commented out), non-default
`ServerPort`/`TelnetPort` so nothing collides with a real server, `GameWorld=Navezgane`,
`TerminalWindowEnabled=false`. Already correct in stock config: `EACEnabled=false`, `ServerVisibility=0`.

**`-logfile` truncates its target**, so every run needs its own filename or the previous run's evidence is lost.

Telnet with an empty `TelnetPassword` listens on loopback only — which is what a local harness wants.

## 4. The Tier-1 slice: results

A full cycle — cold start, four commands, clean shutdown — in **30.87 seconds**, exit code 0, no orphaned process.

| Milestone | Elapsed |
|---|---|
| server process started | 0.01s |
| telnet accepts a connection | 2.04s |
| `gettime` answers | 17.29s |
| readiness marker appears | 23.37s |
| four commands run, `shutdown`, process exited | 30.87s |

### 4a. The readiness trap

**The console answers commands before the world is loaded.** Evidence from one run: `gettime` returned
`Day 1, 07:00` at 16.9s, while `createWorld() done` was 21.6s and `StartGame done` 22.4s.

A test treating "a command replied" as readiness would query a half-built world and fail intermittently in ways
that look like flakiness. Telnet merely *accepting* (2.04s) means even less — only that the listener is up.

The marker used during the slice was `Dymesh door replacement: imposterBlock` (23.37s), which lands safely after
`StartGame done`. It works, but it was found empirically and its name says nothing about what it guarantees.

**Known-insufficient signals**, in increasing order of safety: telnet accepting → a command answering →
`ModEvents.GameStartDone` (the repo owner reports this is still not enough for operations such as scanning all
blocks in the world) → the dymesh marker.

That progression is why readiness is best modelled as a **capability ladder** rather than a single boolean —
different operations become safe at different points, and a test should wait for the level it actually needs.
Emitting our own staged markers is tracked as work; see the architecture doc.

### 4b. Telnet output is parseable

The stream interleaves command responses with ongoing server log output, but the framing is clean:

- **Server log lines carry an ISO timestamp prefix. Command output lines do not.**
- Every execution is announced by `INF Executing command '<cmd>' by Telnet from <ip>:<port>`.

So the parse is: anchor on the announcement, then take following *untimestamped* lines as the response.

```
2026-08-04T17:31:51 16.956 INF Executing command 'version' by Telnet from 127.0.0.1:<port>
Game version: V 3.1.0 (b14) Compatibility Version: V 3.1.0
Mod TFP_Harmony: 1.1.0.4
```

**Dependency: `HideCommandExecutionLog=0`.** Higher values hide the announcement from telnet and break the anchor.

Bonus: `version` lists loaded mods, so **the mod set is assertable over telnet** — the excerpt above is also the
proof that §3a's controlled tree worked and that nothing from the live deploy loaded.

### 4c. Startup cost breakdown

| Phase | Cost |
|---|---|
| Engine + static data init (telnet opens at 1.6s) | ~1.6s |
| EOS login, permissions, title storage | ~3s |
| **Config/block loading — silent, no log output**, ending in `Block IDs total 24809` | **~9s** |
| World load, hashes, chunk groups | ~7s |
| Dymesh warmup to the readiness marker | ~1.8s |

**The dominant cost is content loading, not the world.** Consequences: shrinking the world touches only the ~7s
world phase; a pre-made save avoids `save folder does not exist` at 4.8s but creation is cheap next to loading;
the floor is roughly 10-12s regardless. **Amortizing startup across tests beats shrinking it** — which sets
fixture granularity at per-session or per-mod-set, never per-test.

Also note the ~9s phase logs *nothing*, which is its own argument for emitting our own markers.

### 4d. The server phones home

Even at `ServerVisibility=0`, a run performs EOS server registration, Steam `GameServer.LogOn` (which logs the
machine's public IP), a Discord global-lobby registration, and a LAN announcer on multicast 239.192.0.1.

Three independent reasons to suppress this, beyond tidiness: it is ~3s of the startup budget; a harness running
frequently would repeatedly announce itself on public infrastructure; and it is what triggers the firewall prompt
in §6 — which has no one to answer it on a second machine or in CI. (The repo's own `DisableLAN` mod already
addresses one part.)

## 5. Tier 2: the protocol client (researched, unbuilt)

### 5a. What is reusable

| Layer | Type(s) | Reusable headlessly? |
|---|---|---|
| Transport | `NetworkClientLiteNetLib` → `LiteNetLib.NetManager` (`LiteNetLib.dll` ships in Managed) | **Yes** — plain class |
| Framing / serialization | `INetConnection` → `NetConnectionSimple`; 194 `NetPackage*` types with `read`/`write(PooledBinary*)` | **Yes** — plain classes; length framing, GZip compression, pooled readers/writers |
| **Orchestration** | `ConnectionManager`, `GameManager` | **No — MonoBehaviours.** This is the part that must be reimplemented |

`IProtocolManagerProtocolInterface`, the callback interface the transport needs, has only five members
(`IsServer`, `IsClient`, `InvalidPasswordEv`, `ConnectionFailedEv`, `DisconnectedFromServerEv`) — trivial to
stand in for. `NetManager.Connect(ip, port, key)` takes the **server password** as the key (empty string when
unset), so there is no magic handshake token.

### 5b. The handshake

`NetPackage.AllowedBeforeAuth` is effectively the specification — exactly ten packages are accepted pre-auth:
the four encryption ones, `AuthConfirmation`, `AuthState`, `EAC`, `PackageIds`, `PlayerDenied`, `PlayerLogin`.

Client sequence, traced by finding which type pulls each package from the pool: LiteNetLib connect →
`NetPackagePackageIds` (mod-added package ID negotiation) → `NetPackagePlayerLogin` → server auth →
`NetPackageClientInfo` → `NetPackageRequestToEnterGame` → `NetPackageWorldInitInfoRequest` →
`NetPackagePlayerData` → `NetPackageRequestToSpawnPlayer`.

`NetPackagePlayerLogin` is a plain six-field payload (player name, platform user+token, crossplatform
user+token, version, compVersion, discordUserId), landing server-side in `GameManager.PlayerLoginRPC` →
`AuthorizationManager.Authorize`.

### 5c. The auth gate with EAC off

`AuthorizationManager` runs a chain of ~15 `IAuthorizer`s. The three that decide feasibility:

- **`VersionAuthorizer`** — a single `Equals`. Send the matching version string.
- **`AntiCheatEncryptionAgreementAuthorizer`** — keyed on `AntiCheatServer.EncryptionAvailable`; with EAC off,
  encryption is skipped and the protocol is plaintext.
- **`NativePlatformAuthorizer`** — resolve `PlatformManager.InstanceForPlatformIdentifier(platform)`; **null →
  deny** (kick reason 20); that platform's `AuthenticationServer` **null → `SyncAllow` immediately**; otherwise
  real authentication.

`Platform.Local.UserIdentifierLocal` exists alongside Steam/EOS/XBL/PSN, so the escape hatch is a `Local`
identity whose platform has no `AuthenticationServer`.

> **The one runtime unknown**, unanswerable from metadata: whether `InstanceForPlatformIdentifier(Local)` returns
> non-null on a *running* server. Non-null → early allow and Tier 2 is open; null → denied, and the approach
> needs rethinking. First thing any Tier-2 spike should check.

### 5d. Traps found while mapping the block-placement path

- **`NetPackageLockRequest`/`NetPackageLockResponse` are not StrongLocks-style locks.** They carry
  `ILockTarget[]`/`ILockContext` and are generic access arbitration. Easy to grab by name and lose a day.
- **`ConsoleCmdPlaceBlockShapes` bypasses the interesting path** — it calls `WorldBase.SetBlocksRPC`, *not*
  `GameManager.ChangeBlocks`, so it never fires a `ChangeBlocks` patch.
- **`ConsoleCmdSpawnEntityAt` (`spawnentityat`/`sea`) does not** — it calls `World.SpawnEntityInWorld`, so the
  vanilla command exercises that patch target with no custom code at all.
- **Replaying a `NetPackage` server-side by calling `ProcessPackage()` is a trap.**
  `NetPackage.ValidUserIdForSender` dereferences `Sender.PlatformId`, and `Sender` only exists for a genuinely
  connected client — a synthetic package throws. Fabricating a `ClientInfo` would forge exactly what the
  validation exists to verify. The clean seam is one level down: `GameManager.ChangeBlocks(persistentPlayerId,
  blocksToChange)` is what the handler calls *after* validation, skipping only the anti-spoofing checks, which
  belong to the network path rather than to any mod's logic.

## 6. Machine state a harness must know about

- **Windows Firewall rules are keyed to the executable's full path.** The scratch tree's exe counted as a new
  program despite being the same bytes as the install's, and prompted. Rebuilding the tree **at the same path**
  is free; **relocating it re-prompts** — so the tree's location is semi-permanent state worth pinning, and never
  somewhere ephemeral or machine-specific.
- Loopback is not filtered, so telnet (and a future Tier-2 client on 127.0.0.1) works regardless of the prompt.
  What needs the rules is the *external* surface — which §4d proposes removing anyway.
- Runs create a save under `<UserDataFolder>/Saves/<world>/<GameName>/` and **reuse it on subsequent runs**. Tests
  wanting a pristine world must delete it; that is a knob the harness should own rather than leave implicit.

## How these findings were obtained

Read the game's compiled behavior directly rather than trusting documentation:

- **Mono.Cecil** (`net40` build from the NuGet cache, loaded into Windows PowerShell 5.1 via `Add-Type`) reading
  `Assembly-CSharp.dll` from the declared version's `packages/` tree. A `DefaultAssemblyResolver` with the
  Managed directory added is required before any method body can be read — without it, `ReadAssembly` throws on
  an unresolved reference. Enumerating types, then dumping a method's `ldstr` literals and `call` targets,
  answers most behavioral questions on sight.
- Packages are **pooled, not constructed** — searching for `newobj` finds nothing; search `call` instructions
  whose operand is a `GenericInstanceMethod` with the package as a generic argument.
- PowerShell driven through a heredoc: **multi-line constructs silently fail** in stdin command mode, so every
  statement must be on one line (a multi-line function body produces no output and no error).
- See also the repo's `.ai/` docs on the same technique, and `Tests/Fixtures/EntryPoints.cs`, which uses Cecil
  the same way in production test code.

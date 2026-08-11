# 0002. Compile against the game's own assemblies, including framework types

**Status:** Accepted **Date:** 2026-08-10 — retro-filed; the decision was settled by a pilot on 2026-07-28
(`.ai/f1-sdk-migration.md` §3).

## Context

Moving to SDK-style projects forced a choice the legacy build never had to state. An SDK-style `net4x` project demands
reference assemblies for the targeting pack — `GetReferenceAssemblyPaths` fails with `MSB3644` without them — and most
machines do not have the net481 pack installed standalone. Two mechanisms were available, and both were piloted on
`StrongHorns`:

|                 | (a) `Microsoft.NETFramework.ReferenceAssemblies.net481`     | (b) `FrameworkPathOverride` → `$(SdtdManagedDir)`             |
|-----------------|-------------------------------------------------------------|---------------------------------------------------------------|
| Mechanism       | NuGet package supplies official net481 reference assemblies | Targeting-pack resolution points at the game's own assemblies |
| Ecosystem       | The standard, well-travelled answer                         | Common in game modding, less travelled generally              |
| Compile surface | Official reference assemblies                               | The game's actual runtime surface                             |

Option (a) was the expected answer going in. It does not work.

`StrongHorns` failed under it with `CS1061` on `ConcurrentDictionary.GetValueOrDefault` — an API the game's Unity Mono
runtime provides but the official net481 reference assemblies do not contain. The repo's shipping code is written
against the game's actual surface, so official reference assemblies are not a viable compile target at all. **This is a
hard failure, not a fidelity preference.**

## Decision

Everything compiles against the game's own assemblies — game types, `0Harmony.dll`, *and framework types*.
`build/Mod.props` sets `FrameworkPathOverride` to the game's Managed folder, so no .NET Framework targeting pack is
needed anywhere.

`Microsoft.NETFramework.ReferenceAssemblies` is not an option for this repo, and reaching for it to "fix" a missing
targeting pack will reintroduce the failure above.

## Consequences

Under this mechanism the pilot's post-RAR `ReferencePath` is **exactly the legacy set, every path from the game
install** — including `mscorlib`, which option (a) would have swapped for the package's copy. What compiles is what the
runtime actually has.

Two references the legacy build injected invisibly from the machine's targeting pack are now explicit `GameAssembly`
entries:

- `System.Core` — the legacy C# targets added it implicitly; it is now referenced from the game's copy.
- `netstandard` — the legacy build passed **115** references to `csc`: ten explicit, plus `System.Core`, plus **104
  facade DLLs** injected by `ImplicitlyExpandDesignTimeFacades`. The SDK does no facade expansion, and game assemblies
  type-forward through netstandard, so the game's own 2.1 shim is referenced explicitly.

A further facade need surfaces as a readable `CS0012` naming the assembly, and takes the same one-line
`<GameAssembly Include="..." />` fix.

Net effect: SDK projects need **no targeting pack at all** — strictly *less* machine-dependent than the legacy build,
which needed one invisibly.

Knock-on effects recorded at the time:

- CI reference assemblies must be Refasmer'd from the game's own DLLs rather than taken from
  `Microsoft.NETFramework.ReferenceAssemblies` (#15).
- Every SDK-style project requires a NuGet restore even with zero packages (`NETSDK1004` without one). Under this
  mechanism that restore is local-only, no network (#11).
- A game tree must be present to build at all — which is what makes the restored package tree and the vendored trees
  load-bearing infrastructure rather than a convenience.

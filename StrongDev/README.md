# StrongDev

Development-time tooling for building and validating 7 Days to Die mods — **not a shipped mod.** Where StrongMods
is the library your mods depend on *at runtime* (breadth-first patcher, `<foreach>`, `<ensure>`, dependency
validation), StrongDev is the toolbox you reach for *while developing*: headless test harnesses, and eventually
asset browsers (sound, icon, model) and other authoring aids. The intent is that other modders — especially those
already building on StrongMods — can use it too.

## Status: genesis

**This directory is the seed of the project, not the project.** It exists so the work that started StrongDev is
committed, organized, and visibly in-flight rather than stranded in a scratch directory or a chat log. There is no
buildable project here yet — no `.csproj`, so the build and the convention tests ignore it entirely.

Everything real lives under [`.ai/`](.ai), following this repo's convention that `.ai/` holds AI-generated
artifacts that have not yet graduated into the repo proper through thorough human review:

- [`.ai/headless-server-testing.md`](.ai/headless-server-testing.md) — the empirical record: how to run a
  controlled dedicated server headlessly and drive it, and everything learned about the game's networking, mod
  loading, and startup while proving it. Version-scoped to what was tested.
- [`.ai/mod-testing-architecture.md`](.ai/mod-testing-architecture.md) — the design record: how per-mod behavioral
  tests should be structured, the StrongDev product boundary, and an explicit ledger of what was decided versus
  what is deferred.
- [`.ai/tools/`](.ai/tools) — the working code from the first vertical slice (`buildtree.cs`, `tier1slice.cs`),
  kept as proof-of-shape. Disposable-quality; graduates into real project code through review.

The GitHub issue tracker carries the work and its status (see the umbrella issue for headless mod-behavior
testing); these docs carry the *why* and the *what we know*.

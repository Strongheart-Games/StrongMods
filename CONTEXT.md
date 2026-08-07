# StrongMods

A collection of 7 Days to Die mods and mod-development tools from the mind of Strongheart--me.

This file represents my thoughts--in my own words--on the goals of the repo and the vocabulary I use to talk about it.
`AGENTS.md`, on the other hand, describes the workflow and rules for working in this repo.

## Vision

My goal is to make 7 Days to Die more fun and engaging through mods for players and tools that help modders build better
mods. I host a 7 Days to Die community called Stronghold, which includes a public server called Hades.

I focus on server-side-only mods--those that only need to be installed on the server and not downloaded to every
player's client. Because there's no additional setup needed for players, server-side mods are more broadly accessible
and can reach more players. Hades uses only server-side mods.

## Vocabulary

### Product Matrix

The important axes for thinking about 7 Days to Die are:

* **Product:** Game or Dedicated Server
* **Operating System (OS):** Linux or Windows
* **Version:** e.g. 2.6 b222 or 3.0.1 b4

Most players run the Game on Windows while most Dedicated Servers run on Linux.

### Important Project Types

* **Modlet:** An XML-only mod, though it may also contain documentation.
* **Mod:** Any mod project that is not a modlet.
* **Overlay:** Content deployed into a directory this repo does not fully manage.

Not every project fits one of these types; these are the distinctions important for understanding this repo.

### Core Projects

These projects describe the intended structure:

* **StrongDev:** Shippable build and test infrastructure for mod development. It is not intended for live servers.
* **StrongMods:** A mod containing reusable features for other mods, such as new XML patch methods. It is published for
  other modders to use.
* **StrongCore:** An opinionated counterpart to StrongMods used as the foundation for other mods in this repo. It is not
  intended for use by outside modders.

#### Relationships

* StrongDev's distribution includes StrongMods.
* StrongCore depends on StrongMods.
* StrongMods and StrongCore are distributed separately so mods can declare dependencies on them.
* Other projects may depend on StrongCore and therefore indirectly on StrongMods.

#### Naming Ambiguities

* **StrongMods** names both this repo and a project/product within it.
* **Mod** means a project type distinct from a modlet within this repo, but 7 Days to Die treats both as mods after
  deployment.

## Current State

As of 2026-08-07, the intended structure is still being built:

* StrongCore does not exist.
* StrongDev contains only documentation and AI artifacts.
* Test infrastructure lives outside the StrongMods project.
* The project structure and development workflow remain in flux.

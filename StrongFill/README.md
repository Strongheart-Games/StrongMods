# StrongFill

Fill gaps between your builds and the surrounding terrain. Just craft and place on the adjacent terrain block.

* Crafted from 4 crushed sand.
* Place it directly on top of the terrain block you want filled; it fires once after a short delay and then removes
  itself.
* Fills the four cardinal neighbours of that terrain block, but only where the neighbouring block is a solid,
  non-water, non-child auto-shape block (i.e. a cube or near-cube building shape).
* Diagonals are only filled when the diagonal *and* both of its adjoining cardinals qualify, so corners stay clean.
* If there is nothing to fill it does nothing and stays put, so you can pick it up and place it elsewhere.
* The delay is driven by the block's `FillRate` property in `Config/blocks.xml` (default `0.06`).

## Installation

* Copy the `StrongFill/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongFill/ModInfo.xml`, otherwise the mod
  won't be loaded
* Dedicated servers:
  * Server-side only — the filling logic is bound to the block through a server-side `ServerClass` property, so
    clients never need the DLL
  * EAC-friendly
  * Clients that don't have the mod installed will still see and place the block (block, recipe and item XML are
    synced by the server) but will see the raw localization key instead of the block's name and description
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now

## Changelog

### 1.0.0

* Initial public release
* Only works against 7DtD v3.x

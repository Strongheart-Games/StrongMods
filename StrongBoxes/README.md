# StrongBoxes

Change storage box behavior based on the box's sign label.

**This mod is a work in progress and is not yet functional. Do not install it on a live server expecting it to do
anything.**

The intended design is that you write a keyword on a storage box's sign and the box gains that behaviour. The only
keyword implemented so far is `sort`, which is meant to push the box's contents into nearby storage.

What actually exists today:

* A Harmony hook on `TEFeatureStorage.OnUnlockedServer` that reads the box's signed text and dispatches to any
  registered listener whose keyword matches (case-insensitive).
* The `sort` listener scans the box's own chunk and the eight surrounding chunks for tile entities and ranks the
  lootable ones by distance, up to 15 meters.
* **The actual item transfer is an empty stub**, so labelling a box `sort` currently moves nothing.
* Ownership, lock, password and "someone is in the box right now" checks are all still `TODO`, so nothing prevents
  a future implementation from touching boxes it should not.
* No new blocks, items or recipes are added.

## Installation

* Copy the `StrongBoxes/` directory into `Mods/`
* Make sure the `ModInfo.xml` appears one folder below `Mods/`, i.e. `Mods/StrongBoxes/ModInfo.xml`, otherwise the mod
  won't be loaded
* Dedicated servers:
  * Server-side only
  * EAC-friendly
* All other deployments:
  * Deploy to host (in single-player this is your game)
  * EAC must be disabled
* There are no configuration options for now

## Changelog

### 0.0.1

* Work in progress; not yet functional
* Only works against 7DtD v3.x

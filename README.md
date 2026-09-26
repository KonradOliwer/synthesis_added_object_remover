# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. By default it also removes the target's objects that were touching a removed one,
and the target's invisible objects (lights, sounds, insect spawners, ...) left behind once the
objects around them are gone.

Removal never deletes anything: the object is disabled and moved far below the world. The output
plugin is `AddedObjectRemover.esp`.

## How to use

1. Add this repository to a Synthesis group as a Git patcher, with the group placed after the target plugin.
2. Set *Target plugin*.
3. Run the group and keep `AddedObjectRemover.esp` enabled after the target plugin.

## Settings

### What to check

| Setting | Default | Meaning |
| --- | --- | --- |
| Target plugin | *(empty)* | File name of the plugin whose added objects are checked, e.g. `SomeMod.esp`. If it is empty or not in the load order, nothing is changed. |
| Size multiplier | `0.5` | How far past its own edges a target object reaches, as a fraction of its size (0-5). Another mod's object whose centre falls inside that range makes the target object too close. Larger removes more; 0.25-1 is typical. |

### What to ignore

| Setting | Default | Meaning |
| --- | --- | --- |
| Excluded plugins | *(empty)* | Plugins whose objects never count as a conflict. |
| Ignore the target's masters | `true` | Also ignore objects from the plugins the target plugin was built on. |

### Follow-up removal

| Setting | Default | Meaning |
| --- | --- | --- |
| Follow-up removal mode | `AnyTouch` | What happens to target objects touching a removed one (see below). |
| Touch distance | `8` | Largest gap, in game units (0-64), between two surfaces for them to count as touching. |
| Anchoring threshold | `50` | `Anchoring` only: percentage (1-100) of what an object rests on or touches that must be removed for it to be removed. |

- **Off**: only the too-close objects are removed.
- **AnyTouch**: every target object connected to a removed one through touching target objects is removed too. This can spread through floors and walls to whole rooms.
- **Anchoring**: a touching target object is removed only when at least *Anchoring threshold* % of what it rests on or touches was removed. The ground and objects of any plugin, the base game included, count as support.

### Leftover invisible objects

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove leftover invisible objects | `true` | Remove the target's invisible objects once the target's visible objects around them were removed. Works in every follow-up mode. |
| Search radius | `1024` | Distance in game units (more than 0) from an invisible object to the edges of the target's visible objects that count as its surroundings. |
| Removed surroundings percentage | `50` | An invisible object is removed when at least this percentage (1-100) of its surroundings was removed, including the nearest object and at least one object on every side (north-east, north-west, south-west, south-east). |
| Protected types | `None` | Invisible object types that are always kept (see below). |
| Custom protected types | *(empty)* | The types to keep when *Protected types* is `Custom`. |

Only the target plugin's own objects count as surroundings. Protected types presets:
- **None**: nothing is protected.
- **Markers**: map markers, X markers (XMarker, XMarkerHeading), idle, furniture and door markers.
- **MarkersAndLights**: *Markers* plus lights.
- **MarkersLightsAndSounds**: *MarkersAndLights* plus sound markers and acoustic spaces.
- **Custom**: the types listed in *Custom protected types*: `MapMarkers`, `XMarkers`, `IdleMarkers`,
  `FurnitureMarkers`, `DoorMarkers`, `OtherMarkers`, `Lights`, `SoundMarkers`, `AcousticSpaces`,
  `CritterSpawners`, `TriggerBoxes`, `Decals`.

### Diagnostics

| Setting | Default | Meaning |
| --- | --- | --- |
| Detailed log | `false` | Log every removed and kept object with its reason, per-space counts and unreadable meshes. |
| Diagnostics folder | *(empty)* | Folder for diagnostics spreadsheets (CSV files). Leave empty to write nothing. Use an absolute path; a relative one is resolved against Synthesis's working folder. These files never change the result. |

Invalid values are replaced, with a warning in the log: numbers out of range by the nearest valid
value, a search radius of 0 or less by its default. A follow-up mode or protected type name that
Synthesis does not know stops the run.

## Safety

- Teleport doors, and objects that any other record links to (placed objects, quests and their
  aliases, AI packages, locations, factions, navmeshes, dialogue, scripts, ...), are never
  removed by any step, so nothing the game or a script relies on goes missing.

## What is never counted as a conflict

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** ignored automatically;
  add them to *Excluded plugins* if needed.
- Invisible objects, such as markers, lights without a lamp, sounds and trigger boxes.
- Objects the target plugin overrides or replaces (e.g. a tree the target plugin swaps for its own
  version at the same spot).
- Objects added by an earlier Added Object Remover run in the same Synthesis group.

## Diagnostics files

Written only when a *Diagnostics folder* is set. Shares and thresholds in these files are
fractions: 0.5 = 50%.

- **`anchoring.csv`** (*Anchoring* only): one row per object *Anchoring* evaluated, with the share
  of support held by removed objects, kept objects, other plugins and the ground, and the decision.
- **`mesh-origins.csv`** (*Anchoring* only): for each mesh the target uses, whether the object's
  placement point is at its bottom. *Anchoring* works best when it is; check this if *Anchoring*
  removes or keeps unexpected objects.
- **`edges.csv`** (*AnyTouch* only): pairs of target objects found touching, with their distance.
- **`components.csv`** (*AnyTouch* only): one row per chain of touching objects removed together,
  with the too-close object(s) that started it.
- **`leftover-invisible-objects.csv`**: one row per invisible target object checked, with its
  surroundings on each side, how much of them was removed, and the decision.

## Known limitations

- Objects are compared by an approximate bounding box, so two large objects that only overlap at
  their edges may not be detected as too close.
- Object size comes from the mesh (or its Object Bounds as a fallback), which can differ slightly
  from what you see in game.
- Touching is based on visible mesh surfaces, not game collision. In *AnyTouch* mode an object
  fully inside another one without their surfaces meeting does not count as touching; *Anchoring*
  counts it.
- Only the target plugin's own, unmodified objects are checked; objects a later plugin overrides
  are skipped.

## Upgrading from an earlier version

Settings were regrouped and renamed, so settings saved by an older version may reset to their
defaults. Re-enter the target plugin and check the other values after updating.

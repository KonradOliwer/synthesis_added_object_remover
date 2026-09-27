# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. By default it also removes the target's objects that were touching a removed one,
and the target's invisible objects (lights, sounds, critter spawners, ...) that sit inside another
mod's object or are left behind once the objects around them are gone.

Removal never deletes anything: the object is disabled and moved far below the world. The output
plugin is `AddedObjectRemover.esp`.

## Safety

- Teleport doors, and objects that another mod's objects or any other record links to (quests and
  their aliases, AI packages, locations, factions, navmeshes, dialogue, scripts, ...), are never
  removed by any step, so nothing the game or a script relies on goes missing.
- Objects of the target plugin linked together (enable parent, linked references, ...) are removed
  together, or all kept.
- Objects that a script finds only by FormID at run time (for example with GetFormFromFile)
  cannot be detected.

## How to use

1. Add this repository to a Synthesis group as a Git patcher, with the group placed after the target plugin.
2. Set *Mod to clean up*.
3. Run the group and keep `AddedObjectRemover.esp` enabled after the target plugin.

The log ends with a *Possible manual patch needed* section listing removed markers and kept objects worth checking by hand.

## Settings

### Mod to clean up

| Setting | Default | Meaning |
| --- | --- | --- |
| Mod to clean up | *(empty)* | The plugin whose added objects may be removed, e.g. `SomeMod.esp`. If it is empty or not in the load order, nothing is changed. |
| Removal distance (× object size) | `0.5` | Removes the cleaned mod's object when another mod's object is this close, measured in multiples of the object's own size (0-5). Larger removes more; 0.25-1 is typical. |

### Mods that never count as clashing

| Setting | Default | Meaning |
| --- | --- | --- |
| Mods to ignore | *(empty)* | Objects from these plugins never cause removals. |
| Ignore the mod's own masters | `true` | Objects from mods it requires never cause removals. |
| Ignore mods sharing a patch | `true` | If a patch combines both mods, they don't clash. The log lists every detected patch and the mods it causes to be ignored. |
| Patch master limit | `10` | Plugins with more masters than this aren't treated as patches (1-100; the base game and the cleaned mod's masters do not count). Plugins that depend on many mods, such as `DynDOLOD.esp` or a Bashed Patch, are therefore not treated as patches; the log lists them as skipped. |

### Objects resting on removed ones

| Setting | Default | Meaning |
| --- | --- | --- |
| Also remove | `EverythingTouching` | What else goes with a removed object (see below). |
| Touch gap | `8` | Gap still counted as touching, in game units (0-64). |
| Support lost (%) | `50` | `ObjectsSupportedByIt` only: remove an object once this much of its support is gone (1-100). |

- **Nothing**: only the too-close objects are removed.
- **EverythingTouching**: every object connected to a removed one through touching objects is removed too. This can spread through floors and walls to whole rooms.
- **ObjectsSupportedByIt**: a touching object is removed only when at least *Support lost (%)* of what it rests on or touches was removed. The ground and objects of any plugin, the base game included, count as support. It works best for objects whose mesh origin is at their base (true for most plants and many props); `mesh-origins.csv` (see *Diagnostics files*) shows how well that holds for your target plugin.

*This setting resets to its default once when upgrading, because its options were renamed.*

### Invisible objects left behind

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove leftover sounds and markers | `true` | Removes invisible objects whose surroundings were removed. Works with any *Also remove* setting. |
| Look-around distance | `1024` | How far around to check, in game units (64-8192). A light, sound or trigger box that reaches less far uses its own reach. |
| Direction cleared at (%) | `50` | Share removed for a direction to count as cleared. |
| Cleared directions needed (%) | `60` | Share of directions that must be cleared. |
| Minimum directions with objects (%) | `50` | Of the 8 directions, this share must contain the cleaned mod's objects before deciding. |
| Never remove | `None` | Invisible object types to always keep (see below). |
| Types to never remove (Custom) | *(empty)* | Used when *Never remove* is `Custom`: the types to keep. |
| Move kept markers out of other mods' objects | `false` | Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Only its position changes. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved. |

The three percentages take values from 10 to 100 in steps of 10.

An invisible object is removed when:
- it sits inside a building or cave of another mod — not merely under a bridge or overhang, or
- its surroundings were removed: the area around it is split into 8 directions (north, north-east,
  east, ...), and at least *Occupied directions required* % of the directions hold target objects,
  and at least *Removed directions required* % of those lost at least *Removed area per direction* %
  of their ground area. A target object the invisible object sits in counts in every direction.

Only the target plugin's own objects count as surroundings. Objects that something depends on
(see *Safety*) are kept in any case, protected types unless they are linked to a removed object. *Never remove* presets:
- **None**: nothing is protected.
- **Markers**: map markers, X markers (XMarker, XMarkerHeading), idle, furniture and door markers.
- **MarkersAndLights**: *Markers* plus lights.
- **MarkersLightsAndSounds**: *MarkersAndLights* plus sound markers and acoustic spaces.
- **Custom**: the types listed in *Types to never remove (Custom)*: `MapMarkers`, `XMarkers`, `IdleMarkers`,
  `FurnitureMarkers`, `DoorMarkers`, `OtherMarkers`, `Lights`, `SoundMarkers`, `AcousticSpaces`,
  `CritterSpawners`, `TriggerBoxes`, `Decals`.

### Logs and reports

| Setting | Default | Meaning |
| --- | --- | --- |
| Detailed log | `false` | Lists every removed object and why, plus per-space counts and unreadable meshes. |
| Write report files | `false` | Saves CSV files for checking the results (see *Diagnostics files*). |
| Report folder | `AddedObjectRemover Reports` | Folder for report files: a full path, or relative to the patch output folder. Only used when *Write report files* is on. |

Invalid values are replaced, with a warning in the log: numbers out of range by the nearest valid
value, percentages that are not a step of 10 by the nearest step. An *Also remove* or *Never remove*
value that Synthesis does not know stops the run.

## What is never counted as a conflict

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** ignored automatically;
  add them to *Excluded plugins* if needed.
- Invisible objects, such as markers, lights without a lamp, sounds and trigger boxes.
- Objects the target plugin overrides or replaces (e.g. a piece of clutter the target plugin swaps
  for its own version at the same spot).
- Objects added by an earlier Added Object Remover run in the same Synthesis group.
- Mods linked to the target only by a compatibility patch (a plugin that masters both the target
  and a few other mods), and that patch itself.

## Diagnostics files

Written only when *Write report files* is on, into the *Report folder*. Shares and thresholds in
these files are fractions: 0.5 = 50%.

- **`anchoring.csv`** (*ObjectsSupportedByIt* only): one row per object evaluated, with the share
  of support held by removed objects, kept objects, other plugins and the ground, and the decision.
- **`mesh-origins.csv`** (*ObjectsSupportedByIt* only): for each mesh the target uses, whether the
  object's placement point is at its bottom. *ObjectsSupportedByIt* works best when it is; check
  this if it removes or keeps unexpected objects.
- **`edges.csv`** (*EverythingTouching* only): pairs of target objects found touching, with their distance.
- **`components.csv`** (*EverythingTouching* only): one row per chain of touching objects removed together,
  with the too-close object(s) that started it.
- **`leftover-invisible-objects.csv`**: one row per invisible target object checked, with the
  other mod's object it sits inside, the ground area around it and how much of it was removed in
  each direction, the decision, and where it was moved to, if it was.
- **`manual-patch-hints.csv`**: the *Possible manual patch needed* section of the log.

## Known limitations

- Objects are compared by an approximate bounding box, so two large objects that only overlap at
  their edges may not be detected as too close.
- Object size comes from the mesh (or its Object Bounds as a fallback), which can differ slightly
  from what you see in game.
- Touching is based on visible mesh surfaces, not game collision. In *EverythingTouching* mode an
  object fully inside another one without their surfaces meeting does not count as touching;
  *ObjectsSupportedByIt* counts it.
- Only the target plugin's own, unmodified objects are checked; objects a later plugin overrides
  are skipped.
- A kept marker is moved only up to 2048 units, an exterior one usually only within its own cell,
  and in tight interiors the navmesh and floor often lie inside room pieces, so it may be left
  where it is (the log says so).

## Upgrading from an earlier version

Settings were regrouped and renamed, so settings saved by an older version may reset to their
defaults. Re-enter the target plugin and check the other values after updating. The *Also remove*
setting resets to its default once, because its options were renamed.

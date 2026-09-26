# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. By default it also removes the target's objects that were touching a removed one,
and the target's invisible objects (lights, sounds, insect spawners, ...) that sit inside another
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
2. Set *Target plugin*.
3. Run the group and keep `AddedObjectRemover.esp` enabled after the target plugin.

The log ends with a *Possible manual patch needed* section listing removed markers and kept objects worth checking by hand.

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
| Ignore mods patched with the target | `true` | If a plugin depends on both the target and another mod, treat it as a compatibility patch: ignore that patch and the other mod. The log lists every detected patch and the mods it causes to be ignored. |
| Maximum other masters for a patch | `10` | A plugin counts as a compatibility patch only if it depends on the target plus at most this many other mods (1-100; the base game and the target's masters do not count). Plugins that depend on many mods, such as `DynDOLOD.esp` or a Bashed Patch, are therefore not treated as patches; the log lists them as skipped. |

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
| Remove leftover invisible objects | `true` | Remove the target's invisible objects that sit inside another mod's object or whose surroundings were removed. Works in every follow-up mode. |
| Protected types | `None` | Invisible object types that are always kept (see below). |
| Custom protected types | *(empty)* | The types to keep when *Protected types* is `Custom`. |
| Search radius | `1024` | Largest distance in game units (64-8192) from an invisible object to the edges of the target's visible objects that count as its surroundings. A light, sound or trigger box that reaches less far uses its own reach. |
| Removed area per direction | `50` | A direction counts as removed when at least this percentage of the ground area of the target objects in it was removed. |
| Removed directions required | `60` | An invisible object is removed when at least this percentage of the directions holding target objects are removed. |
| Occupied directions required | `50` | Invisible objects with target objects in fewer than this percentage of the 8 directions around them are kept. |
| Move kept markers out of other mods' objects | `false` | Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Only its position changes. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved. |

The three percentages take values from 10 to 100 in steps of 10.

An invisible object is removed when:
- it sits inside a building or cave of another mod — not merely under a bridge or tree, or
- its surroundings were removed: the area around it is split into 8 directions (north, north-east,
  east, ...), and at least *Occupied directions required* % of the directions hold target objects,
  and at least *Removed directions required* % of those lost at least *Removed area per direction* %
  of their ground area. A target object the invisible object sits in counts in every direction.

Only the target plugin's own objects count as surroundings. Objects that something depends on
(see *Safety*) are kept in any case, protected types unless they are linked to a removed object. Protected types presets:
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
value, percentages that are not a step of 10 by the nearest step. A follow-up mode or protected
type name that Synthesis does not know stops the run.

## What is never counted as a conflict

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** ignored automatically;
  add them to *Excluded plugins* if needed.
- Invisible objects, such as markers, lights without a lamp, sounds and trigger boxes.
- Objects the target plugin overrides or replaces (e.g. a tree the target plugin swaps for its own
  version at the same spot).
- Objects added by an earlier Added Object Remover run in the same Synthesis group.
- Mods linked to the target only by a compatibility patch (a plugin that masters both the target
  and a few other mods), and that patch itself.

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
- **`leftover-invisible-objects.csv`**: one row per invisible target object checked, with the
  other mod's object it sits inside, the ground area around it and how much of it was removed in
  each direction, the decision, and where it was moved to, if it was.
- **`manual-patch-hints.csv`**: the *Possible manual patch needed* section of the log.

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
- A kept marker is moved only up to 2048 units, an exterior one usually only within its own cell,
  and in tight interiors the navmesh and floor often lie inside room pieces, so it may be left
  where it is (the log says so).

## Upgrading from an earlier version

Settings were regrouped and renamed, so settings saved by an older version may reset to their
defaults. Re-enter the target plugin and check the other values after updating.

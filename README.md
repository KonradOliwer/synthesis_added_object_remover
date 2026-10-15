# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. Optionally it also removes the target's objects that stand on terrain another mod
changed (see *Terrain changed by other mods*). By default it also removes the target's objects that were touching a removed one,
and the target's invisible objects (lights, sounds, critter spawners, ...) that sit inside another
mod's object or are left behind once the objects around them are gone.

Removal never deletes anything: the object is disabled and moved far below the world, the same way
xEdit's *Undelete and Disable References* does. If it had an enable parent, that is replaced so the
object stays disabled. The changes are written to your Synthesis group's output plugin.

Teleport doors, and objects that another mod's objects or any other record links to (quests and
their aliases, AI packages, locations, factions, navmeshes, dialogue, scripts, ...), are never
removed by any step, so nothing the game or a script relies on goes missing. Objects of the target
plugin linked together (enable parent, linked references, ...) are removed together, or all kept.
A linked partner of an object removed for being too close, for standing on changed terrain, or by
*Also remove*, is itself treated as removed, so what rests on or touches it can go too.

## How to use

1. Add this repository to a Synthesis group as a Git patcher, with the group placed after the target plugin.
2. Set *Mod to clean up*.
3. Run the group and keep the group's output plugin enabled after the target plugin.

Near the end of the log, a *Possible manual patch needed* section lists removed markers and kept objects worth checking by hand.

## Settings

### Mod to clean up

| Setting | Default | Meaning |
| --- | --- | --- |
| Mod to clean up | *(empty)* | The plugin whose added objects may be removed, e.g. `SomeMod.esp`. If it is empty or not in the load order, nothing is changed. |

### Terrain changed by other mods

Removes the cleaned mod's objects when another mod changed the terrain under or near them, for
example raised or flattened the ground or turned grass into a road, so the objects no longer fit.
Off by default. When on, it runs before the too-close check, so an object found by both is listed as removed for changed terrain. Objects in interiors, and in worlds without terrain, are not checked.

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove objects on changed terrain | `false` | Switches this removal on. |
| Objects checked | `VisibleObjectsOnTheGround` | Which of the cleaned mod's objects are checked: `VisibleObjectsOnTheGround` only visible objects that reach the ground (objects on roofs, bridges or high shelves are skipped); `AllVisibleObjects` every visible object, at any height; `AllObjects` also invisible ones such as markers, lights and sounds. |
| Count height changes | `true` | Whether a change of the ground's height counts. |
| Height change tolerance | `16` | A height change of up to this many game units is ignored (0-256). 0 counts every change. Small smoothing edits are common and rarely visible under an object. |
| Count terrain type changes | `true` | Whether a change of the kind of ground counts (see below). |
| Terrain distance measured in | `GameUnits` | How the area around an object is measured: `GameUnits` uses *Terrain distance (game units)*, `ObjectSize` uses *Terrain distance (× object size)*. |
| Terrain distance (game units) | `64` | How far around the object the terrain is checked, in game units (0-1024). The terrain points right around the object are always checked. |
| Terrain distance (× object size) | `0.5` | The same, in multiples of the object's own size (0-5). |

**What counts as a terrain type.** The terrain type is the kind of ground: grass, dirt, stone,
snow and so on. A road counts by its ground type: grass changed to a road counts as a change, but a
dirt patch repainted as a dirt road of the same ground type does not. A different texture of the
same ground type (one grass texture replaced by another) is never a change. Where several types
are mixed, the one that covers most of a point counts.

**What is compared.** For each piece of terrain an object stands on, the reference terrain is the
first of these that has terrain there: the last plugin that requires the cleaned mod (a patch or an
add-on for it), then the cleaned mod itself, then the base game with its official updates and DLC
(Creation Club content is not included). A change counts only when the terrain the game shows
differs from the reference by more than the switched-on checks allow, and a mod that is not part of
the reference and not one of the *Mods that never count as clashing* made the change. Changes
made by the cleaned mod, or by a plugin that requires it, are never counted against it. The
detailed log names the mod that changed the terrain for each removed object.

If *Remove objects on changed terrain* is on but both *Count height changes* and *Count terrain
type changes* are off, nothing is removed for changed terrain and the log warns about it. Switched
on with the other defaults, only visible objects on the ground are checked, within 64 units, for
height changes over 16 units and for terrain type changes.

### Too close to other mods' objects

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove when too close (× object size) | `true` | Removes the cleaned mod's object when another mod's object is within *Removal distance (× object size)* of it. |
| Removal distance (× object size) | `0.5` | How close counts as too close, measured in multiples of the object's own size (0-5). Larger removes more; 0.25-1 is typical. Only used when *Remove when too close (× object size)* is on. |
| Remove when too close (game units) | `false` | Removes the cleaned mod's object when another mod's object is within *Removal distance (game units)* of it, however big the object is. |
| Removal distance (game units) | `32` | How close counts as too close, in game units (0-1024). 0 means only objects that touch or overlap. Only used when *Remove when too close (game units)* is on. |
| Removal zone | `ObjectShape` | The zone that other mods' objects must reach, for either removal distance: `ObjectShape` uses the object's own shape, enlarged by the removal distance, and checks the other object's shape against it; `BoundingBox` (faster, less exact) uses the object's enlarged box and the other object's centre. |

Each of the two *Remove when too close* switches works on its own, and both can be on together; an
object found by either is removed once. With both off, nothing is removed for being too close (and
NPCs stuck in objects are not checked either). The defaults switch on only the check by object size.

### Mods that never count as clashing

| Setting | Default | Meaning |
| --- | --- | --- |
| Mods to ignore | *(empty)* | Objects from these plugins never cause removals. |
| Ignore the mod's own masters | `true` | Objects from mods it requires never cause removals. |
| Ignore mods sharing a patch | `true` | The other mods a detected patch requires don't clash with the target. The log lists every detected patch and the mods it causes to be ignored. |
| Patch master limit | `10` | A patch is a plugin that requires the target plugin and at most this many other mods (1-100; the base game and the target's masters do not count). Only used when *Ignore mods sharing a patch* is on. Plugins that depend on many mods, such as a merged patch or a Bashed Patch, are therefore not treated as patches; the log lists them as skipped. |
| NPCs and creatures | `OnlyWhenStuckInObject` | Whether other mods' NPCs and creatures cause removals: `CountLikeObjects` treats them like any other object, `OnlyWhenStuckInObject` only when they are stuck in the object at its real size, `Ignore` never. An NPC is stuck when its body sinks more than 8 units into the object, or when it stands inside the object (for example inside a boulder, or in a room of a building); standing on or leaning against the object does not count. An NPC placed through a leveled list counts when any of the NPCs it can be is stuck. |

### Objects resting on removed ones

| Setting | Default | Meaning |
| --- | --- | --- |
| Also remove | `EverythingTouching` | What else goes with a removed object (see below). |
| Touch gap | `8` | Gap still counted as touching, in game units (0-64). |
| Support lost (%) | `50` | `ObjectsSupportedByIt` only: remove an object once this much of its support is gone (1-100). |

- **Nothing**: only the objects found too close or on changed terrain are removed.
- **EverythingTouching**: every object connected to a removed one through touching objects is removed too, spreading outward one touching object at a time until nothing more is added. This can spread through floors and walls to whole rooms. Only the target plugin's visible objects are removed this way; a kept object (see *What is never removed*) is not removed and the spread does not pass through it.
- **ObjectsSupportedByIt**: a touching object is removed only when at least *Support lost (%)* of what it rests on or touches was removed. The ground and objects of any plugin, the base game included, count as support. It works best for objects whose mesh origin is at their base (true for most free-standing props); `mesh-origins.csv` (see *Reports and log*) shows how well that holds for your target plugin.

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
| Move kept markers out of other mods' objects | `false` | Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Only its position changes. Needs *Remove leftover sounds and markers* on; markers are kept when *Never remove* protects them or something depends on them. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved. |

The three percentages take values from 10 to 100 in steps of 10.

An invisible object is removed when:
- it sits inside another mod's object, such as a building or a boulder — not merely under a bridge or overhang, or
- its surroundings were removed: the area around it is split into 8 directions (north, north-east,
  east, ...), and at least *Minimum directions with objects (%)* of the directions hold target objects,
  and at least *Cleared directions needed (%)* of those lost at least *Direction cleared at (%)*
  of their ground area. A target object the invisible object sits in counts in every direction.

Only the target plugin's own objects count as surroundings. Objects that something depends on
(quests, scripts, and the other cases listed under *What it does*) are kept in any case, protected
types unless they are linked to a removed object. *Never remove* presets:
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
| Detailed log | `false` | Lists every removed object and why, plus per-space counts, unreadable meshes, timings and processing statistics. |
| Write report files | `false` | Saves CSV files for checking the results (see *Reports and log*). |
| Report folder | `AddedObjectRemover Reports` | Folder for report files: a full path, or relative to the folder containing the output plugin. Created if missing; empty means the default. If it can't be used, the log warns and the run continues. Only used when *Write report files* is on. |

Numbers out of range are corrected to the nearest valid value (a value that is not a number becomes
the default), and percentages are rounded to the nearest step of 10, each with a warning in the log.
A setting with an invalid value (for example text that is not one of the dropdown choices) stops the
run; the log names the setting and what is allowed there.

## What is never removed / never counts

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** ignored automatically;
  add them to *Mods to ignore* if needed.
- Plugins listed in *Mods to ignore*, and the plugins the target plugin requires (with *Ignore the
  mod's own masters* on).
- Invisible objects, such as markers, lights without a lamp, sounds, trigger boxes and decals, and
  objects whose base record is missing: they never cause removals.
- Other mods' NPCs and creatures, except as set in *NPCs and creatures*.
- Effect meshes, such as fog, light rays and water spray: they never cause removals.
- Objects the target plugin overrides or replaces (e.g. a piece of clutter the target plugin swaps
  for its own version of about the same size at the same spot).
- Objects that start disabled: the target plugin's are never checked or removed; other mods' never
  cause removals, unless a parent object can enable them in game.
- Objects added by patchers that run earlier in the same Synthesis group.
- With *Ignore mods sharing a patch* on: every compatibility patch (a plugin that requires the target
  plugin and at most *Patch master limit* other mods) and the other mods it requires. A plugin that
  requires only the target plugin, such as an add-on for it, is not a patch; its objects can cause removals.
- Teleport doors, and any object linked to by a quest, script, AI package, location, faction,
  navmesh or dialogue.

## Reports and log

The normal log shows only what you need: the settings in use, warnings and errors, the final summaries of removed, kept and moved objects, one line per kept object, a summary of unreadable meshes, and where report files were written. Turn on *Detailed log* for every removed object and why, per-space counts, unreadable-mesh details, timings and processing statistics. Unreadable meshes are listed together near the end of the log, after the patch is written.

Near the end of the log, a *Possible manual patch needed* section lists removed door, map, idle and furniture markers (which actors or the map use), and objects that would have been removed but were kept, grouped by why (part of a linked group, kept because something depends on it, or a teleport door). With report files on, the locations of the written files follow.

If something goes wrong that the patcher did not expect, the log shows a line starting with "Unexpected error" that names the part that failed, the error, and what is missing from the results because of it. The run carries on and the patch is still written. When it happens for single objects (a target object that is too close or on changed terrain, an other-mod object that can cause removals, or a target object that might have to be kept), only that object is skipped: a target object whose check failed is kept and not removed, and an other-mod object that failed causes no removals. Objects with the same error are grouped into one line with their number and a few examples; turn on the detailed log to list all of them. The run stops only if the load order can't be read, the target plugin is unusable, or the patch can't be written.

When such an error stops a whole part of the run (*Also remove*, *Remove leftover sounds and markers*, *Move kept markers*, the report files, or the details in them), the log says so with a line like "Also remove: not performed because of an unexpected error.", and the rest of the run still goes on.

With *Write report files* on, CSV files (UTF-8, comma-separated, header in the first row) are written to the *Report folder*. At the start of each run the plugin's report files from earlier runs are deleted from that folder. Which files appear depends on your settings:

- **`anchoring.csv`**: why each object was removed or kept. Only with *Also remove* `ObjectsSupportedByIt` and something removed for being too close or on changed terrain.
- **`mesh-origins.csv`**: how each target mesh's origin sits (near the bottom, near the centre, or elsewhere); check it if `ObjectsSupportedByIt` removes or keeps unexpected objects. Same conditions as `anchoring.csv`.
- **`edges.csv`** and **`components.csv`**: which objects were found touching and which chains were removed together. Only with *Also remove* `EverythingTouching` and something removed for being too close or on changed terrain.
- **`leftover-invisible-objects.csv`**: why each invisible object checked was removed or kept, and where it was moved. Only with *Remove leftover sounds and markers* on.
- **`manual-patch-hints.csv`**: the rows of the *Possible manual patch needed* section, always written. Columns: hint, formKey, editorId, space, cell, detail.

## Known limitations

- With *Removal zone* `BoundingBox`, objects are compared by an approximate bounding box, so two
  large objects that only overlap at their edges may not be detected as too close.
- Changed terrain is judged from the terrain data and the ground types of the texture records; the
  check does not know which textures look like a road, only what ground type each texture is set to.
- Object size comes from the mesh, or its Object Bounds when the mesh can't be read, which can
  differ slightly from what you see in game.
- The target plugin's objects that a later-loading plugin, or an earlier patcher in the same group,
  changes are left alone, even if they still clip with something in game.
- The target plugin's effect-only objects (such as fog or light rays) are never removed unless linked
  to a removed object. An object whose mesh can't be read is never removed by *Also remove*; the log
  lists unreadable meshes.
- A kept marker is moved only up to 2048 units and prefers staying in its own cell; in tight
  interiors a free spot often can't be found, so it may be left where it is (the log says so).

# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. By default it also removes the target's objects that stand on terrain another mod
changed (see *Remove objects on terrain changed by other mods*) and the target's objects that lose their support when one is removed,
and the target's invisible objects (lights, sounds, critter spawners, ...) that sit inside another
mod's object or are left behind once the objects around them are gone.

Removal never deletes anything: the object is disabled, the same way xEdit's *Undelete and Disable
References* does. An object that is not persistent is also moved far below the world; a persistent
one stays where it is. If it had an enable parent, that is replaced so the object stays disabled.
The changes are written to your Synthesis group's output plugin.

It can clean up one mod or, in Batch mode, several mods in one run (see *Batch mode*).

Some objects are always kept, whatever the settings, and objects of the target plugin linked
together are always removed together or all kept (see *What is always kept*).
A linked partner of an object removed for being too close, for standing on changed terrain, or by
*Also remove what rests on removed objects*, is itself treated as removed, so what rests on or touches it can go too.

## How to use

1. Add this repository to a Synthesis group as a Git patcher, with the group placed after the target plugin.
2. Leave *Mode* on `Standard` and set *Mod to clean up* under *Standard*. To clean up several mods in one run, see *Batch mode*.
3. Run the group and keep the group's output plugin enabled after the target plugin.

Near the end of the log, a *Possible manual patch needed* section lists removed markers and kept objects worth checking by hand.

## Settings

### Mode

| Setting | Default | Meaning |
| --- | --- | --- |
| Mode | `Standard` | `Standard` cleans up the one mod set under *Standard*. `Batch` cleans up the mods of the config files listed in a group file, one after another (see *Batch mode*). |
| Detailed log and reports | `false` | Lists every removed object and why in the log, plus per-space counts, unreadable meshes, timings and processing statistics, and saves CSV report files for checking the results (see *Reports and log*). Applies in `Standard` and `Batch` mode. Does not change the patch. |

### Batch

| Setting | Default | Meaning |
| --- | --- | --- |
| Group file name | *(empty)* | The name of the group file, with or without `.json`; spaces around it are ignored (see *Batch mode*). Only used in `Batch` mode; it must be set there. |

### Standard

Every setting below is in the *Standard* section of the settings screen. Only used in `Standard` mode; a config file of `Batch` mode holds the same settings.

#### Mod to clean up

| Setting | Default | Meaning |
| --- | --- | --- |
| Mod to clean up | *(empty)* | The plugin whose added objects are checked, picked from your load order, e.g. `SomeMod.esp`. If it is empty or not in the load order, nothing is changed. |

#### Other mods that never cause removals

Objects and terrain changes from these mods are never a reason to remove the cleaned mod's objects.

| Setting | Default | Meaning |
| --- | --- | --- |
| Never count these mods | *(empty)* | Plugins, picked from your load order, whose objects and terrain changes never cause removals. |
| Never count the mods the cleaned mod requires | `true` | The plugins the cleaned mod lists as its masters never cause removals. |
| Compatibility patches: Never count mods that have a compatibility patch with it | `true` | The other mods a detected compatibility patch requires don't clash with the cleaned mod. The log lists every detected patch and the mods it causes to be ignored. |
| Compatibility patches: A patch requires at most this many other mods | `10` | A compatibility patch is a plugin that requires the cleaned mod and at most this many other mods (1-100; the base game and the cleaned mod's masters do not count). Only used when the switch above is on. Plugins that depend on many mods, such as a merged patch or a Bashed Patch, are therefore not treated as patches; the log lists them as skipped. |

#### Remove objects too close to other mods' objects

An object of the cleaned mod is removed when an object from another mod comes within a margin around it. There are two margins; switch on one or both.

| Setting | Default | Meaning |
| --- | --- | --- |
| Margin by object size: Use this margin | `true` | Switches this margin on. Bigger objects get a bigger margin. |
| Margin by object size: Margin on every side (× object size) | `0.5` | The margin is this multiple of the object's own size, added on every side (0-5): 0.5 adds half the object's size on every side, so the checked area is twice as large as the object; 1 adds a whole size on every side (three times as large). Larger removes more; 0.25-1 is typical. |
| Margin by fixed distance: Use this margin | `true` | Switches this margin on. Every object gets the same margin, however big it is. |
| Margin by fixed distance: Margin on every side (game units) | `50` | The margin in game units, added on every side (0-1024). 0 means only objects that touch or overlap. |
| Use rough boxes instead of exact shapes (faster) | `false` | Off: the object's own shape, enlarged by the margin, is compared with the other object's shape. On: the object's box, enlarged by the margin, is compared with the other object's centre point (faster, less exact). Applies to both margins. |
| Other mods' NPCs and creatures cause removal | `OnlyWhenStuckInsideTheObject` | Whether other mods' NPCs and creatures cause removals: `AlwaysLikeAnyObject` treats them like any other object, `OnlyWhenStuckInsideTheObject` only when they are stuck in the object at its real size, `Never` never. An NPC is stuck when its body sinks more than 8 units into the object, or when it stands inside the object (for example inside a boulder, or in a room of a building); standing on or leaning against the object does not count. An NPC placed through a leveled list counts when any of the NPCs it can be is stuck. |

The two margins work on their own and both can be on together; an object found by either is removed once. With both off, nothing is removed for being too close (and NPCs stuck in objects are not checked either), and the log warns about it. The defaults switch on both margins.

#### Remove objects on terrain changed by other mods

Removes the cleaned mod's objects when another mod changed the terrain under or around them, for
example raised or flattened the ground, turned grass into a road, or raised, lowered, added or
removed the water there, so the objects no longer fit (they float, sink, stand under water or on
dry land where water was).
When on, it runs before the too-close check, so an object found by both is listed as removed for changed terrain. Objects in interiors, and in worlds without terrain, are not checked.

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove objects on changed terrain | `true` | Switches this removal on. |
| Which of the cleaned mod's objects are checked | `AllObjectsIncludingInvisibleOnes` | `VisibleObjectsOnTheGround` only visible objects that reach the ground (objects on roofs, bridges or high shelves are skipped); `VisibleObjectsAtAnyHeight` every visible object; `AllObjectsIncludingInvisibleOnes` also markers, lights and sounds. |
| Margin by object size: Use this margin | `true` | Switches this margin on. Bigger objects get a bigger margin. |
| Margin by object size: Margin on every side (× object size) | `0.5` | The margin is this multiple of the object's own size, added on every side (0-5): 0.5 adds half the object's size on every side, so the checked area is twice as large as the object. |
| Margin by fixed distance: Use this margin | `true` | Switches this margin on. Every object gets the same margin. |
| Margin by fixed distance: Margin on every side (game units) | `64` | The margin in game units, added on every side (0-1024). |
| Count height changes | `true` | Whether a change of the ground's height counts. |
| Ignore height changes up to (game units) | `16` | A height change of up to this many game units is ignored (0-256). 0 counts every change. Small smoothing edits are common and rarely visible under an object. Only used when *Count height changes* is on. |
| Count ground type changes (for example grass to road) | `true` | Whether a change of the kind of ground counts (see below). |
| Remove only when this share of the ground points changed type (%) | `20` | A ground type change removes an object only when at least this share of the terrain points checked around it changed type (0-100). Small touch-ups at the edges of painted patches therefore don't remove objects. 0 removes on any one changed point. Height changes are not affected. Only used when *Count ground type changes* is on. |
| Count water height changes | `true` | Whether another mod raising or lowering a cell's water counts, when that puts ground around the object under water or out of it (see below). Water over ground that stays covered is not seen (see *Known limitations*). |
| Ignore water height changes up to (game units) | `16` | A water height change of up to this many game units is ignored (0-256). 0 counts every change. Only used when *Count water height changes* is on. |
| Count water added/removed changes | `true` | Whether another mod adding water to a cell or taking its water away counts, when that puts ground around the object under water or out of it (see below). Water over ground that stays covered is not seen (see *Known limitations*). |
| Remove only when this share of the ground got covered/uncovered (%) | `20` | A water change removes an object only when at least this share of the terrain points checked around it went under water or came out of it (0-100). 0 removes on any one such point. Only used when one of the two water switches is on. |

The terrain under the object plus the margin around it is checked, for height, ground type and water changes alike. The terrain points right around the object are always checked. As with the too-close margins, the two margins work on their own and both can be on together: an object is removed when the terrain within either margin holds a change. One point whose height changed beyond the tolerance is enough; a ground type change and a water change each need the share of points set above. With both margins off, nothing is removed for changed terrain and the log warns about it.

**What counts as a water change.** A terrain point is under water when the cell's water surface is
above the ground there. A water change counts at a point when the point went under water or came out
of it, and the new water level alone does that over the ground the cleaned mod was made for. A
point that only went under water or dried up because another mod raised or lowered the ground is
judged by the height check, not counted again as a water change. Water that moves but stays above
(or below) the ground everywhere around the object changes nothing. A cell's water level is its own
water height, or its world's default water height where the cell uses the default; a cell without
water has none. A world that uses its parent world's water data takes the default from the parent
world. A cell that uses a default water height no copy of its world stores is not checked for water,
since its water level is unknown. Water types, colours and other looks of the water are not compared.

**What counts as a terrain type.** The terrain type is the kind of ground: grass, dirt, stone,
snow and so on. A road counts by its ground type: grass changed to a road counts as a change, but a
dirt patch repainted as a dirt road of the same ground type does not. A different texture of the
same ground type (one grass texture replaced by another) is never a change. Where several types
are mixed, the one that covers most of a point counts.

**What is compared.** For each piece of terrain an object stands on, the reference terrain is the
terrain the cleaned mod was made for: its own terrain there, or else the terrain of the last of its
required mods (its masters) that has terrain there. Where neither has terrain, the object is not
checked. A change counts only when the terrain the game shows differs from the reference by more
than the switched-on checks allow, and the mod that made the difference is not the cleaned mod, a
plugin that requires it (a patch or an add-on for it) or one of the *Other mods that never cause
removals*. That mod's own change must also be larger than the checks allow: several small changes
that only add up to a larger one through a mod that does not count do not count. A mod that only
copies another mod's terrain did not make the change: copying a change made by a mod that does not
count does not count either, and a patch that carries another mod's terrain change does not hide
it. Only the points changed by mods that count make up the share of changed ground points. Water
is compared the same way: the water the cleaned mod was made for is the water of the cell, and the
world's default water height, as the cleaned mod or else the last of its masters has them; the mod
that changed the water must count. The detailed log names the mod that made the change for each
removed object, and what changed: height, terrain type, water height, water added or water removed
(for example `terrain changed by SomeMod.esp (water height)`); for a ground type or water change,
that is the mod that changed the most points. Where several changes count, the later loading of
their mods is named.

If *Remove objects on changed terrain* is on but no height, ground type or water change is counted,
nothing is removed for changed terrain and the log warns about it. With the default settings, all
objects (invisible ones included) are checked, within half their size and 64 units around them, for height changes over
16 units, for ground type changes at 20% of the points or more, and for water changes (height
changes over 16 units, water added or removed) that put 20% of the points or more under water or
out of it.

#### Also remove what rests on removed objects

| Setting | Default | Meaning |
| --- | --- | --- |
| When an object is removed, also remove | `OnlyWhatLosesItsSupport` | What else goes with a removed object (see below). |
| Gap that still counts as touching (game units) | `8` | Two objects this close or closer count as touching (0-64). Not used with `Nothing`. |
| OnlyWhatLosesItsSupport: remove when this much support is gone (%) | `50` | `OnlyWhatLosesItsSupport` only: remove an object once this much of its support is gone (1-100). |

- **Nothing**: only the objects found too close or on changed terrain are removed.
- **EverythingTouchingIt**: every object connected to a removed one through touching objects is removed too, spreading outward one touching object at a time until nothing more is added. This can spread through floors and walls to whole rooms. Only the target plugin's visible objects are removed this way; a kept object (see *What is always kept*) is not removed and the spread does not pass through it.
- **OnlyWhatLosesItsSupport**: a touching object is removed only when at least *remove when this much support is gone (%)* of what it rests on or touches was removed. The ground and objects of any plugin, the base game included, count as support. It works best for objects whose mesh origin is at their base (true for most free-standing props); `mesh-origins.csv` (see *Reports and log*) shows how well that holds for your target plugin.

#### Invisible objects left behind

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove invisible objects left inside other mods' objects or alone | `true` | Removes the cleaned mod's invisible objects (sounds, lights, markers, spawners and the like) that sit inside another mod's object or whose surroundings were removed. Works with any *When an object is removed, also remove* setting. |
| When the cleaned mod's visible objects around it were removed: Look this far around it (game units) | `1024` | How far around to look for the cleaned mod's visible objects (64-8192). A light, sound or trigger box that reaches less far uses its own reach. |
| When the cleaned mod's visible objects around it were removed: Decide only if this share of the 8 directions has objects (%) | `50` | Of the 8 directions, this share must contain the cleaned mod's visible objects before deciding. |
| When the cleaned mod's visible objects around it were removed: A direction is cleared when this much of it was removed (%) | `50` | Share removed for a direction to count as cleared. |
| When the cleaned mod's visible objects around it were removed: Remove when this share of those directions is cleared (%) | `60` | Share of the directions with objects that must be cleared. |
| Never remove these as left behind (ticked = kept) | *(none ticked)* | One tick box per kind (see below). A ticked kind is kept by this step, an unticked kind can be removed by it. Does not affect the too-close or changed-terrain removals. |
| Move kept markers out of other mods' objects | `true` | Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Only its position changes. Needs *Remove invisible objects left inside other mods' objects or alone* on; markers are kept when *Never remove these as left behind* ticks them or something depends on them. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved. |

The three percentages take values from 10 to 100 in steps of 10.

An invisible object is removed when:
- it sits inside another mod's object, such as a building or a boulder — not merely under a bridge or overhang, or
- its surroundings were removed: the area around it is split into 8 directions (north, north-east,
  east, ...), and at least *Decide only if this share of the 8 directions has objects (%)* of the directions hold target objects,
  and at least *Remove when this share of those directions is cleared (%)* of those lost at least *A direction is cleared
  when this much of it was removed (%)* of their ground area. A target object the invisible object sits in counts in every direction.

Only the target plugin's own objects count as surroundings. The objects listed under *What is always
kept* are kept in any case, and so are the kinds ticked under *Never remove these as left behind*,
unless they are linked to a removed object.
The tick boxes are: map markers, position markers (XMarker, XMarkerHeading), idle animation spots, furniture use spots,
invisible marker doors (doors flagged as markers that lead nowhere), lights without a model, sound sources, echo areas (acoustic spaces), critter spawners,
invisible trigger areas, surface decals (dirt, blood) and, last, other invisible objects.

Numbers out of range are corrected to the nearest valid value (a value that is not a number becomes
the default), and percentages are rounded to the nearest step of 10, each with a warning in the log.
A setting with an invalid value (for example text that is not one of the dropdown choices) stops the
run; the log names the setting and what is allowed there.

## Batch mode

Batch mode cleans up several mods in one run, one after another, all into the same output plugin. Each mod has its own **config file** with its own settings. The **group file** is the list of config names to run, in order; it has nothing to do with a Synthesis group.

1. Set *Mode* to `Batch` and *Group file name* to the name of your group file.
2. Put the files in this folder structure inside the game's Data folder (with Mod Organizer 2 this can be a mod of its own, which then appears in the Data folder; start Synthesis from Mod Organizer 2 so it sees that mod):

```
synthesis_added_object_remover\
    groups\<group file>.json a list of config names, in run order
    configs\<config>.json    the settings for one mod
```

The patcher never creates or changes these files. The `Example mod` folder in the repository is a ready example (a group file and two configs) to copy into Mod Organizer 2.

Names in the group file and the group file name are plain file names, not paths; `.json` is added when missing, and names are matched without regard to case.

**Group file** (`groups\my_town_mods.json`): a JSON list of config names.

```json
["town_walls", "town_market"]
```

**Config file** (`configs\town_walls.json`): the same settings as the *Standard* section, written under their key names. **A config does not take any value from *Standard*; a setting it leaves out uses the built-in default.** Only `TargetPlugin` is required; list just the settings you want to change.

To find the key names, copy them from the entries inside `Standard` in the patcher's `settings.json` (in its Synthesis folder `...\Synthesis\Data\<profile>\<patcher name>`) or from the configs in the `Example mod` folder, which list every setting. Dropdown choices are written as text (`"NpcHandling": "Never"`), plugins as `"Name.esp"`, lists as arrays (`["A.esp", "B.esp"]`).

```json
{
  "TargetPlugin": "SomeTownMod.esp",
  "TooClose": { "BySize": { "SizeMultiplier": 1.0 } }
}
```

- *Detailed log and reports* is set once, next to *Mode*, and applies to every config of the batch.
- Upper and lower case in keys do not matter.
- With *Detailed log and reports* on, each config's log section also lists the settings that file changes from the default.
- Comments and trailing commas are allowed in the files.

**Order is priority.** The configs run in the listed order. A later config treats the objects an earlier one removed as gone, and the markers an earlier one moved as standing at their new position. An object that only clashed with something an earlier config removed is therefore kept.

**What stops the whole run.** Before anything is written, the patcher reads the group file and every config and checks them against the load order. If any of these is wrong, the run stops with a message that names the file and the problem, and no patch is written:
- no group file name is set, or a name is a path or has characters a file name cannot have;
- the group file or a listed config file is missing;
- a file is not valid JSON, the group file is not a list of names or the config is not an object;
- the group file lists nothing, or lists the same config twice;
- a setting has a value that cannot be read (for example text that is not one of the dropdown choices);
- a config sets no *Mod to clean up*, or the mod is not in the load order;
- two configs clean up the same mod.

The message ends with *Nothing was run and no patch was written.*

**What does not stop the run.**
- A key the patcher does not know is ignored and listed in the log, so a misspelling is easy to find.
- A setting a config leaves out uses its default; the log lists those settings with the default used.
- A number out of range is corrected, with a warning in that config's log section.
- A mod with nothing to check ends its own section with *Nothing to check for this config. No changes made for it; the batch goes on.*

## What is never removed / never counts

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** ignored automatically;
  add them to *Never count these mods* if needed.
- Plugins listed in *Never count these mods*, and the plugins the target plugin requires (with *Never count the
  mods the cleaned mod requires* on).
- Invisible objects, such as markers, lights without a lamp, sounds, trigger boxes and decals, and
  objects whose base record is missing: they never cause removals.
- Other mods' NPCs and creatures, except as set in *Other mods' NPCs and creatures cause removal*.
- Effect meshes, such as fog, light rays and water spray: they never cause removals.
- Objects the target plugin overrides or replaces (e.g. a piece of clutter the target plugin swaps
  for its own version of about the same size at the same spot).
- Objects that start disabled: the target plugin's are never checked or removed; other mods' never
  cause removals, unless a parent object can enable them in game.
- Objects added by patchers that run earlier in the same Synthesis group.
- With *Never count mods that have a compatibility patch with it* on: every compatibility patch (a plugin that requires the target
  plugin and at most *A patch requires at most this many other mods* other mods) and the other mods it requires. A plugin that
  requires only the target plugin, such as an add-on for it, is not a patch; its objects can cause removals.
- The target plugin's objects listed under *What is always kept*.

## What is always kept

Whatever the settings, the target plugin's objects of these kinds are never removed:
- teleport doors, and the doors they lead to;
- anything another mod's object, a quest, an AI package, a scene, a location or a navmesh points to
  (or any other record, such as a faction, a form list or dialogue);
- objects whose own script names a quest or a scene, or whose base object comes from the target plugin and
  has a script that names one. A base object from another plugin also serves that plugin's own copies, so
  its script does not keep the target plugin's copies;
- objects with a location role: a location ref type of their own. Map markers are not kept for this reason alone.

Objects of the target plugin that are linked to each other (enable parent, linked references, script
properties, ...) are always removed together or kept together. The log names the reason each object was kept.

**Map markers** stay removable. A removed map marker is disabled where it stands instead of being
moved, so it is hidden from the world map. Markers a location or quest uses are always kept.

## Reports and log

The normal log shows only what you need: the settings in use, warnings and errors, the final summaries of removed, kept and moved objects, one line per kept object and a summary of unreadable meshes. Turn on *Detailed log and reports* for every removed object and why, per-space counts, unreadable-mesh details, timings, processing statistics and the report files. Unreadable meshes are listed together near the end of the log, after the patch is written.

In `Batch` mode the banner prints once, then each config has its own section that starts with a line like `=== Config 1 of 2: town_walls.json, mod to clean up: SomeTownMod.esp ===`. Inside it come that config's notes (keys that were ignored, settings left at their defaults) and then the usual log of that run, ending with its own *Done* line. The notes look like this:

```
Ignored in town_walls.json, because this patcher has no such setting (check the spelling and the section it is in): Colour.
Not set in town_walls.json, defaults used: TooClose.UseRoughBoxes = false, TooClose.NpcHandling = "OnlyWhenStuckInsideTheObject", FollowUpRemoval.TouchDistance = 8.0
```

Every setting the config leaves out is listed with its key and the default used, written as the config file would hold it, in the order of the settings screen.

When an earlier config removed or moved objects, the scan summary says how many were ignored or checked at their new position.

Right after the kept summary, each object kept for a reason of its own gets a line saying why: *Kept, script names a quest or scene* (its own script, or its base object's from the target plugin) or *Kept, location role* (a location ref type of its own). They are kept on purpose and need no manual patch.

Near the end of the log, a *Possible manual patch needed* section lists removed door, map, idle and furniture markers (which actors or the map use), and objects that would have been removed but were kept, grouped by why (part of a linked group, kept because something depends on it, or a teleport door). With *Detailed log and reports* on, the locations of the written files follow.

Before that section, the log warns about removed objects that can still affect the game:
- each removed unique NPC that a quest fills an alias with (as its *Unique Actor*), when that alias is neither *Optional* nor *Allow Disabled*: that quest role may not fill, or the quest may not start;
- the number of removed objects in their worldspace's large-reference list: distant LOD may flicker, or full models may not load, in those cells until LOD is generated again (for example with DynDOLOD).

The last section of the log says where these objects are: the removed objects, the kept ones (one tree for each reason they were kept), the ones to check for a manual patch, removed unique NPCs and removed large references, each only when there are any. Each is shown as a tree of three levels, each level indented further:
- **worldspace**: the worldspace (for example *Skyrim*), or for an object in an interior, the interior's name;
- **hold**: the hold-level location the object's cell belongs to (for example *The Pale hold*): the nearest location of the cell's location chain that the game marks as a hold; without one, the location just below the top of the chain;
- **place**: the most specific name: the cell's location (*Loreius Farm*), else the cell's own name, else *near* the location of the nearest named cell within 3 cells (*near Loreius Farm*), else the cell grid (*cell 7,6*).

Names are what the game shows (the location or cell name), or their editor ID when they have none. A cell without a location takes the hold of the nearest cell within 3 cells that has one; when there is none, the hold is *unnamed area*. Objects in a worldspace's persistent cell are counted in the grid cell they stand in.

Every area of the tree is listed. Each line ends with two numbers: the objects of that list in the area, and all of the target mod's objects there (for the removals: *removed / target objects there*). The first line of each tree gives the same two numbers for the whole tree, e.g. *Removed 1284/9850 (removed/all mod objects):*; the other trees have matching headers (*Kept 12/9850 (kept/all mod objects), teleport door:*, *Possible manual patch needed for 7/9850 (needing a patch/all mod objects):*). Areas with the most objects come first; areas with the same number come in order of fewer target objects there (a larger share removed), then by name. A place and the *near* place of the same name (e.g. *Loreius Farm* and *near Loreius Farm*) stay together, the place first, and are ranked as one by their combined numbers. A worldspace or hold with only one area below it gets no line of its own: its name is joined to that area's line with *›*, and the joined line sits at the level of its first name. A name that repeats an earlier name of the joined line is left out.

```
Removed 1284/9850 (removed/all mod objects):
  Skyrim 1223/9850
    The Pale hold 402/1310
      Loreius Farm 41/52
      Dawnstar 30/95
      ...
    Whiterun hold 311/2007
      ...
    Falkreath Hold › near Halted Stream Camp 25/25
  Solstheim › Solstheim hold › Raven Rock 61/430
```

Both logs show the same tree. In the detailed log, each removed object's line also names its place, hold and grid cell.

If something goes wrong that the patcher did not expect, the log shows a line starting with "Unexpected error" that names the part that failed, the error, and what is missing from the results because of it. The run carries on and the patch is still written. When it happens for single objects (a target object that is too close or on changed terrain, an other-mod object that can cause removals, or a target object that might have to be kept), only that object is skipped: a target object whose check failed is kept and not removed, and an other-mod object that failed causes no removals. Objects with the same error are grouped into one line with their number and a few examples; turn on the detailed log to list all of them. The run stops only if the load order can't be read, the target plugin is unusable, or the patch can't be written.

When such an error stops a whole part of the run (*Also remove what rests on removed objects*, *Invisible objects left behind*, moving kept markers, the report files, or the details in them), the log says so with a line like "Also remove: not performed because of an unexpected error.", and the rest of the run still goes on.

With *Detailed log and reports* on, CSV files (UTF-8, comma-separated, header in the first row) are written to the `Reports` folder inside this patcher's own folder in Synthesis, `...\Synthesis\Data\<profile>\<patcher name>\Reports` (the folder that also holds its `settings.json`), where they last between runs. The log's settings lines show the full path. At the start of each run the plugin's report files from earlier runs are deleted from that folder. In `Batch` mode each config writes to a subfolder of it named after the config (`town_walls` for `town_walls.json`), and only that subfolder's earlier files are deleted. Which files appear depends on your settings:

- **`anchoring.csv`**: why each object was removed or kept. Only with *When an object is removed, also remove* `OnlyWhatLosesItsSupport` and something removed for being too close or on changed terrain.
- **`mesh-origins.csv`**: how each target mesh's origin sits (near the bottom, near the centre, or elsewhere); check it if `OnlyWhatLosesItsSupport` removes or keeps unexpected objects. Same conditions as `anchoring.csv`.
- **`edges.csv`** and **`components.csv`**: which objects were found touching and which chains were removed together. Only with *When an object is removed, also remove* `EverythingTouchingIt` and something removed for being too close or on changed terrain.
- **`leftover-invisible-objects.csv`**: why each invisible object checked was removed or kept, and where it was moved. Only with *Remove invisible objects left inside other mods' objects or alone* on.
- **`removals.csv`**: every removed object with where it is, always written. Columns: formKey, editorId, base, reason, worldspace, hold, place, cellFormKey, cellEditorId, gridX, gridY, x, y, z. Sorted by worldspace, hold and place, then by form key.
- **`manual-patch-hints.csv`**: the rows of the *Possible manual patch needed* section, always written. Columns: hint, formKey, editorId, space, cell, detail.

## Known limitations

- With *Use rough boxes instead of exact shapes (faster)* on, objects are compared by an approximate bounding box, so two
  large objects that only overlap at their edges may not be detected as too close.
- Changed terrain is judged from the terrain data and the ground types of the texture records; the
  check does not know which textures look like a road, only what ground type each texture is set to.
- Water changes are judged only at the terrain points around an object: a point counts only when it
  went under water or came out of it. An object over water that stays deep enough to keep the ground
  under it covered (a boat or a jetty over a lake bed) is therefore not found by the water check when
  another mod raises or lowers that water, whatever *Which of the cleaned mod's objects are checked*
  is set to. Water placed as an object (a water plane) is not seen as water.
- Object size comes from the mesh, or its Object Bounds when the mesh can't be read, which can
  differ slightly from what you see in game.
- The target plugin's objects that a later-loading plugin, or an earlier patcher in the same group,
  changes are left alone, even if they still clip with something in game.
- The target plugin's effect-only objects (such as fog or light rays) are never removed unless linked
  to a removed object. An object whose mesh can't be read is never removed by *Also remove what rests on removed objects*; the log
  lists unreadable meshes.
- A kept marker is moved only up to 2048 units and prefers staying in its own cell; in tight
  interiors a free spot often can't be found, so it may be left where it is (the log says so).

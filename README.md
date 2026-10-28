# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *cleaned mod*, set as *Mod to clean up*) when they sit too close to objects
added by other mods, a common source of clipping and duplicated clutter when several mods edit
the same area. By default it also removes the cleaned mod's objects that stand on terrain another mod
changed (see *Remove objects on terrain changed by other mods*) and the cleaned mod's objects that lose their support when one is removed,
the cleaned mod's visible objects whose surroundings were removed (see *Visible objects left behind*),
and the cleaned mod's invisible objects (lights, sounds, critter spawners, ...) that sit inside another
mod's object or are left behind once the objects around them are gone.

Before an object is removed for being too close, the patcher tries to keep it by shrinking or turning it where it stands
(see *Shrink or turn objects to make them fit*).

Objects of the base game, of the cleaned mod's masters and of mods you trust (a patch made for the cleaned mod, for example)
that rest on an object of the cleaned mod are not left floating when that object is removed: by default they are moved straight
down onto what is below them, and the object they rest on is never shrunk or turned (see *Other mods' objects left without support*).

Removal never deletes anything: the object is disabled, the same way xEdit's *Undelete and Disable
References* does. An object that is not persistent is also moved far below the world; a persistent
one stays where it is. If it had an enable parent, that is replaced so the object stays disabled.
The changes are written to the patch: your Synthesis group's output plugin.

It judges the world the game will load: every plugin enabled in your plugin list, including the plugins that load
after the patch. The patch file a previous run left in your Data folder is not read; every run builds the patch anew.

It can clean up one mod or, in Batch mode, several mods in one run (see *Batch mode*).

Some objects are always kept, whatever the settings, and objects of the cleaned mod linked
together are always removed together or all kept (see *What is always kept*).
A linked partner of an object removed for being too close, for standing on changed terrain, as a visible object left behind, or by
*Also remove what rests on removed objects*, is itself treated as removed, so what rests on or touches it can go too.

## How to use

1. Add this repository to a Synthesis group as a Git patcher.
2. Leave *Mode* on `Standard` and set *Mod to clean up* under *Standard*. To clean up several mods in one run, see *Batch mode*.
3. Run the group and keep the patch enabled, below the cleaned mod in your plugin list.

Near the end of the log, a *Removed markers that actors or the map use* list and a *Kept objects that would have been removed, to check by hand* list name the objects you may want to check (each only when there are any).

The cleaned mod must be enabled in your plugin list and load above the patch. Before anything is changed, the patcher checks this and stops with one of these messages (positions count from 0 in your plugin list):
- *SomeMod.esp is not in your plugin list*
- *SomeMod.esp is disabled*
- *SomeMod.esp loads after the patch AddedObjectRemover.esp (position 300 > 250): move AddedObjectRemover.esp below it*
- *SomeMod.esp is enabled in your plugin list but Synthesis did not load it for this group* (for example when the plugin is blacklisted for the group)
- *SomeMod.esp is the patch this patcher writes*
- *No mod to clean up is set (CleanedMod)*
- *Nothing to check.* (the cleaned mod has no objects left to check)

Each of these messages ends with *No changes made.* In Batch mode the same stops read differently (see *What stops the whole run*).

It also stops when it cannot use the plugin list. The message says which of these it is: the plugin list file cannot be found; the file does not exist; an entry in it is malformed (the message names the line); a plugin Synthesis loads is missing from it; or a plugin Synthesis loaded above the patch is listed after it, in the form *SomeMod.esp is loaded by Synthesis above the patch AddedObjectRemover.esp but is listed after it in the plugin list file C:\...\Plugins.txt*. Except when the file cannot be found at all, the message names the plugin list file. These messages end with *Nothing was run and no patch was written.* in both modes.

An enabled mod that requires the cleaned mod (has it as a master) and loads after the patch beats the patch for every object of the cleaned mod that it overrides. Before anything is changed, the patcher stops with a message that names the one of these mods that loads last, for example *SomeMod - Patch.esp requires SomeMod.esp and loads after the patch AddedObjectRemover.esp (position 310 > 250): move AddedObjectRemover.esp below it in your plugin list, or turn on "Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch"*. Moving the patch below that mod ends the stop for all of them. With *Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch* on, the run goes on: the log has one warning per such mod, and the cleaned mod's objects that it overrides are left unchanged.

A plugin that is enabled in your plugin list after the patch but whose file is not in your Data folder does not stop the run: it is skipped, and the log says so in a warning.

## Settings

### Mode

| Setting | Default | Meaning |
| --- | --- | --- |
| Mode | `Standard` | `Standard` cleans up the one mod set under *Standard*. `Batch` cleans up the mods of the config files listed in a group file, one after another (see *Batch mode*). |
| Detailed log and reports | `false` | Lists every removed object and why in the log, plus per-space counts, unreadable meshes, timings and processing statistics, and saves CSV report files for checking the results (see *Reports and log*). Applies in `Standard` and `Batch` mode. Does not change the patch. |
| Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch | `false` | Such a mod below the patch in your plugin list overrides what the patch writes, so the run normally stops (see *How to use*); moving the patch below it fixes that. On: the log has one warning per mod and the run goes on; the cleaned mod's objects that it overrides are left unchanged. Applies in `Standard` and `Batch` mode. |

### Batch

| Setting | Default | Meaning |
| --- | --- | --- |
| Group file name | *(empty)* | The name of the group file, with or without `.json`; spaces around it are ignored (see *Batch mode*). Only used in `Batch` mode; it must be set there. |

### Standard

Every setting below is in the *Standard* section of the settings screen. Only used in `Standard` mode; a config file of `Batch` mode holds the same settings.

#### Mod to clean up

| Setting | Default | Meaning |
| --- | --- | --- |
| Mod to clean up | *(empty)* | The cleaned mod: the plugin whose added objects are checked, picked from your load order, e.g. `SomeMod.esp`. It must be enabled and load above the patch. If it is empty, not in your plugin list, disabled, loads after the patch, was not loaded by Synthesis or is the patch itself, the run stops and nothing is changed (see *How to use*). |

#### Check other mods' objects the cleaned mod moved

| Setting | Default | Meaning |
| --- | --- | --- |
| Check other mods' objects the cleaned mod moved farther than (game units) | `10` | The cleaned mod can override an object of another mod or of the base game and move it. Moved farther than this from where that object stood before, it is checked like the cleaned mod's own objects: it can be removed, shrunk or turned. Moved this far or less, it counts as standing where it stood and is always kept, even if the cleaned mod changed its model, rotation or size. Distance is measured in a straight line, in any direction. 0 or more; 0 checks every one that moved at all. A negative value, or text that is not a number, is replaced by the default (10), with a warning in the log. See *Objects of other mods that the cleaned mod moved*. |

#### Which other mods can cause removals

Only untrusted mods' objects and terrain changes can remove the cleaned mod's objects. Every other plugin is one of three kinds:

- **Trusted mod**: everything it contains is accepted, even values it only carries over from another mod, everywhere.
- **Mod trusted for its own changes**: a record it adds is accepted in full; in other records, only the values that differ from
  the plugin loading just before it are accepted. Its masters are not affected.
- **Untrusted mod**: every other mod. Its objects and terrain changes can cause removals.

Trusted unless listed under *Never trusted mods*: the base game (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`, `HearthFires.esm`,
`Dragonborn.esm`), the cleaned mod (which cannot be listed there), and its masters (the plugins it requires). Creation Club plugins are not part of the base
game. The patch is neither trusted nor untrusted: it holds what this and earlier patchers of your group wrote.

| Setting | Default | Meaning |
| --- | --- | --- |
| Trusted mods, and their masters | *(empty)* | Plugins, picked from your load order, whose content and whose masters' content is accepted, even values they only carry over, everywhere. |
| Mods trusted for their own changes only | *(empty)* | Plugins, picked from your load order: a record they add is accepted in full; in other records, only the values that differ from the plugin loading just before them are accepted. Their masters are not affected. For example, a bird-sound mod's new sound markers are accepted, but a wall it only carries over from a city mod is not. |
| Never trusted mods | *(empty)* | Plugins, picked from your load order, that can always cause removals, even when they are listed above, require the cleaned mod, load before it, or are its masters. The cleaned mod and the patch cannot be listed; the log warns about such an entry, which has no effect. |
| Also trust mods that require the cleaned mod, and their masters | `true` | A plugin that has the cleaned mod as a master, such as a compatibility patch, was made to fit it, so it is trusted like a listed mod, masters included: `CityMod - CleanedMod Patch.esp` makes `CityMod.esp` trusted. An Added Object Remover patch from another Synthesis group is never trusted this way. |
| ...only if it has at most this many other masters | `5` | Other masters are the masters besides the cleaned mod, the base game and the cleaned mod's masters: a patch requiring `Skyrim.esm`, the cleaned mod and `CityMod.esp` has 1. Mods with more (a merged plugin, a Bashed Patch) are not trusted, and the log lists them. 0 trusts only mods with no other masters (0-100). Only used when the switch above is on. |
| Trust mods loading before the cleaned mod | `true` | The plugins above the cleaned mod in your plugin list, Creation Club content included; the cleaned mod was usually made to fit them. Off: they can cause removals, except the always-trusted ones. |

When several of these name the same plugin, the first that applies decides: *Never trusted mods*; the always-trusted plugins;
*Trusted mods, and their masters*; *Mods trusted for their own changes only*; then the two rules for mods that require the
cleaned mod and mods loading before it.

**Example.** The cleaned mod puts a cart where `CityMod.esp` builds a wall, and you use `CityMod - CleanedMod Patch.esp`, which
requires both. With the defaults, that patch requires the cleaned mod and has one other master, so it and `CityMod.esp` are
trusted: the wall does not remove the cart. Without such a patch, listing `CityMod.esp` under *Trusted mods, and their
masters* does the same.

**Other mods' objects.** Each object of another mod is compared, as the game shows it, with its accepted values: its position,
rotation, size, base object and enable state (deleted, Initially Disabled, enable parent), each taken from the last plugin in the
load order that is trusted for it. The object can cause removals only when one of these differs, or when no plugin trusted for it
has the object (an object an untrusted mod adds). Tiny differences in position, rotation or size, as a plugin that re-saves the
record rounds them, are no change, and neither is the same rotation stored a full turn apart. A trusted mod that only carries over an untrusted mod's change, such as a
compatibility patch that keeps a moved wall, makes that change accepted. A plugin that changes only other data of an object, such
as its ownership, changes nothing here: when it carries another mod's move, that other mod is the one named. The patch is neither
trusted nor untrusted: its values are the final ones, so objects that earlier patchers of your group added or moved can cause removals like any other change.

**Other patchers' outputs.** The output plugin of another Synthesis group, or of another patcher, carries values copied from
many mods. Every Added Object Remover patch has the text *Added Object Remover patch* in its description (the plugin's header,
visible in xEdit); it is added on a new line after a description another patcher of the group already set. An Added Object
Remover patch from another Synthesis group is never trusted by the two automatic rules (mods that require the cleaned mod, and
mods loading before it); you can still list it under *Trusted mods, and their masters*. The output of any other patcher is not
recognised, so the automatic rules can trust it: list it under *Never trusted mods*.

The settings lines at the start of the log name the trusted plugins, one line per reason (for the plugins trusted for loading
before the cleaned mod, only their number), the plugins listed under *Never trusted mods*, and each mod that requires the
cleaned mod but is not trusted: one with too many other masters, or an Added Object Remover patch of another group, which is
named as such whatever its number of other masters. A patch of another group that really loads before the
cleaned mod, and is therefore skipped by the rule for mods loading before it, has its own line (*Loads before SomeMod.esp but
not trusted, Added Object Remover patch of another group: X.esp*). So has such a patch that is a master of a trusted mod that
requires the cleaned mod (*Master of a mod that requires SomeMod.esp but not trusted, Added Object Remover patch of another
group: X.esp*).
A plugin named in one of the three lists that is not in your plugin list gets a warning in the log. If the cleaned mod's file
cannot be read, the log has *Warning: could not read SomeMod.esp to find its masters.*: no plugin is then trusted as one of its
masters.

#### Remove objects too close to other mods' objects

An object of the cleaned mod is removed when an object from another mod comes within a margin around it. There are two margins; switch on one or both.
When *Shrink or turn objects that are too close* is on, an object is first tried shrunk or turned where it stands before it is removed here (see *Shrink or turn objects to make them fit*).

| Setting | Default | Meaning |
| --- | --- | --- |
| Margin by object size: Use this margin | `true` | Switches this margin on. Bigger objects get a bigger margin. |
| Margin by object size: Margin on every side (× object size) | `0.5` | The margin is this multiple of the object's own size, added on every side (0-5): 0.5 adds half the object's size on every side, so the checked area is twice as large as the object; 1 adds a whole size on every side (three times as large). Larger removes more; 0.25-1 is typical. |
| Margin by fixed distance: Use this margin | `true` | Switches this margin on. Every object gets the same margin, however big it is. |
| Margin by fixed distance: Margin on every side (game units) | `50` | The margin in game units, added on every side (0-1024). 0 means only objects that touch or overlap. |
| Use rough boxes instead of exact shapes (faster) | `false` | Off: the object's own shape, enlarged by the margin, is compared with the other object's shape. On: the object's box, enlarged by the margin, is compared with the other object's centre point (faster, less exact). Applies to both margins. |
| Other mods' NPCs and creatures cause removal | `OnlyWhenStuckInsideTheObjectWithCollision` | Whether other mods' NPCs and creatures cause removals: `AlwaysLikeAnyObject` treats them like any other object; `OnlyWhenStuckInsideTheObject` only when they are stuck in the object at its real size; `OnlyWhenStuckInsideTheObjectWithCollision` the same, but only when the object is one you cannot walk through (a rock, a tree, a wall), so plants you walk through (ferns, shrubs, thickets, kelp) are never removed because an NPC stands in them; `Never` never. An NPC is stuck when its body sinks more than 8 units into the object, or when it stands inside the object (for example inside a boulder, or in a room of a building); standing on or leaning against the object does not count. An NPC placed through a leveled list counts when any of the NPCs it can be is stuck. Whether an object can be walked through is read from the collision in its mesh file; when the mesh cannot be read, the object counts as one you cannot walk through. |

The two margins work on their own and both can be on together; an object found by either is removed once. With both off, nothing is removed for being too close (and NPCs stuck in objects are not checked either), and the log warns about it. The defaults switch on both margins.

#### Remove objects on terrain changed by other mods

Removes the cleaned mod's objects when another mod changed the terrain under or around them, for
example raised or flattened the ground, turned grass into a road, or raised, lowered, added or
removed the water there, so the objects do not fit (they float, sink, stand under water or on
dry land where water was).
When on, it runs before the too-close check, so an object found by both is listed as removed for changed terrain. Objects in interiors, and in worlds without terrain, are not checked.
An object on changed terrain is removed, never shrunk or turned: that would not change the ground under it.

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove objects on changed terrain | `true` | Switches this removal on. |
| Which of the cleaned mod's objects are checked | `AllObjectsIncludingInvisibleOnes` | `VisibleObjectsOnTheGround` only visible objects that reach the ground (objects on roofs, bridges or high shelves are skipped); `VisibleObjectsAtAnyHeight` every visible object; `AllObjectsIncludingInvisibleOnes` also markers, lights and sounds. |
| Margin by object size: Use this margin | `true` | Switches this margin on. Bigger objects get a bigger margin. |
| Margin by object size: Margin on every side (× object size) | `0.5` | The margin is this multiple of the object's own size, added on every side (0-5): 0.5 adds half the object's size on every side, so the checked area is twice as large as the object. |
| Margin by fixed distance: Use this margin | `true` | Switches this margin on. Every object gets the same margin. |
| Margin by fixed distance: Margin on every side (game units) | `64` | The margin in game units, added on every side (0-1024). |
| Count height changes | `true` | Whether a change of the ground's height counts. |
| Count only height changes larger than (game units) | `16` | A height change of up to this many game units is not counted (0-256). 0 counts every change. Small smoothing edits are common and rarely visible under an object. Counting uses it only when *Count height changes* is on. With that switch on or off, it also decides which height changes a mod trusted for its own changes owns: only those larger than this (see *What is compared*). |
| Count ground type changes (for example grass to road) | `true` | Whether a change of the kind of ground counts (see below). |
| Remove only when this share of the ground points changed type (%) | `20` | A ground type change removes an object only when at least this share of the terrain points checked around it changed type (0-100). Small touch-ups at the edges of painted patches therefore don't remove objects. 0 removes on any one changed point. Height changes are not affected. Only used when *Count ground type changes* is on. |
| Count water height changes | `true` | Whether another mod raising or lowering a cell's water counts, when that puts ground around the object under water or out of it (see below). Water over ground that stays covered is not seen (see *Known limitations*). |
| Count only water height changes larger than (game units) | `16` | A water height change of up to this many game units is not counted (0-256). 0 counts every change. Counting uses it only when *Count water height changes* is on. With that switch on or off, it also decides which water height changes a mod trusted for its own changes owns: only those larger than this (see *What is compared*). |
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
world. A world that stores no default water height has no default water, so a cell that uses the
default has no water there: a mod that adds a default water height to such a world adds water to
those cells, and a mod that drops the default takes their water away. Water types, colours and other
looks of the water are not compared.

**What counts as a ground type.** The ground type is the kind of ground: grass, dirt, stone,
snow and so on. A road counts by its ground type: grass changed to a road counts as a change, but a
dirt patch repainted as a dirt road of the same ground type does not. A different texture of the
same ground type (one grass texture replaced by another) is never a change. Where several types
are mixed, the one that covers most of a point counts.

**What is compared.** At each terrain point checked around an object, the height and the ground type the
game shows are compared with the accepted ones: those of the last plugin in the load order that is
trusted for them (see *Which other mods can cause removals*). A trusted mod is trusted for all of its
terrain, also for a height or ground type it only carries over from another mod: an untrusted mod's
change that a trusted mod, such as a compatibility patch, carries is accepted. A mod trusted for its
own changes is trusted for a point's height only where it differs from the plugin loading just
before it by more than *Count only height changes larger than*, and for a point's ground type only
where it changes it; a mod that re-saves the terrain with small rounding differences therefore makes
no height its own. A mod that requires the cleaned mod is trusted for terrain exactly as for objects.
The patch is neither trusted nor untrusted, so an earlier patcher's copy of another mod's change does not make that change accepted.
Height and ground type are accepted point by point and apart: one point can take its height from
one plugin and its ground type from another. A point with neither an accepted height nor an accepted
ground type (for example in a world or cell that only untrusted mods add) is not checked, and does not
count in the share of changed ground points or of ground covered or uncovered by water.

A change counts when the terrain the game shows differs from the accepted one by more than the
switched-on checks allow; only those points make up the share of changed ground points. Water is
accepted the same way, cell by cell, in three parts: whether the cell has water and the cell's own
water height, each from the last plugin trusted for it that has the cell, and the world's default
water height, from the last plugin trusted for it that has the world. A mod trusted for its own
changes is trusted for a water height only where it differs from the plugin loading just before it
by more than *Count only water height changes larger than*. The accepted water is made of the
accepted parts the way the game makes the water it shows, and the two are compared. A trusted mod
that carries another mod's water change makes it accepted, as for terrain. A cell is not checked for
water when a part its accepted water is made of has no trusted plugin: when no trusted plugin has
the cell, or when the cell uses the default water height and no trusted plugin has the world. Nor is
it checked in a cell none of whose land points is checked (for example when no trusted plugin has the cell's land).

The detailed log names the mod that made the change for each removed object, and what changed:
height, ground type, water height, water added or water removed (for example `terrain changed by
SomeMod.esp (water height)`). For a height or ground type change, that is the first plugin after the
accepted one whose terrain differs from the accepted one by more than the checks allow and already
holds the value the game ends up with, so a mod that only copies another mod's change is not named. For a water
change, a plugin is found for each changed part of the water, and the one that loads later is
named: a mod that changed the world's default water height is not named for a
cell that uses its own water height. For a ground type or water change, it is the mod that changed
the most points. Where several changes count, the one of those mods that loads last is named. A
change that only the patch holds is named only when there is no other: a mod's change is always named
before it, and `an earlier patcher in this group` stands only when the patch alone holds every
counted change.

If *Remove objects on changed terrain* is on but no height, ground type or water change is counted,
nothing is removed for changed terrain and the log warns about it. With the default settings, all
objects (invisible ones included) are checked, within half their size and 64 units around them, for height changes over
16 units, for ground type changes at 20% of the points or more, and for water changes (height
changes over 16 units, water added or removed) that put 20% of the points or more under water or
out of it.

#### Objects of other mods that the cleaned mod moved

The cleaned mod can change an object another mod (or the base game) placed. When it leaves the object where that mod
placed it (within *Check other mods' objects the cleaned mod moved farther than*), other mods were made to fit it there, so the object is never checked, even if the cleaned
mod gave it another model, rotation or size. When it moves the object, the moved object is checked like the cleaned mod's
own objects (too close to other mods' objects, NPCs stuck in it, terrain changed by other mods, for a visible object whether its
surroundings were removed (see *Visible objects left behind*), and, for an invisible object
such as a sound or a marker, sitting inside another mod's object). If it fits where the cleaned mod put it, it stays there.
If it is too close to another mod's object, it is shrunk or turned there when that makes it fit (see below); otherwise it is
removed, the same way as the cleaned mod's objects. Objects resting on a removed moved object are handled
like objects resting on any removed object. A moved object that a plugin after the cleaned mod changes again is left
to that plugin and not checked. Objects the cleaned mod's objects or other records are linked to are kept, as for the cleaned
mod's own objects (see *What is always kept*); a moved object linked to another object of the cleaned mod is never shrunk or turned.

#### Shrink or turn objects to make them fit

These settings control the shrinking and turning of the cleaned mod's objects and of the moved objects of other mods (see *Objects of other mods that the cleaned mod moved*).

| Setting | Default | Meaning |
| --- | --- | --- |
| Shrink or turn objects that are too close | `true` | Off: an object too close to another mod's object is removed without first being tried shrunk or turned; the two settings below are then not used. Shrink 100 with Turn 0 also means nothing is shrunk or turned, and the log then shows this group as off. |
| Shrink down to (% of its size) | `80` | An object that would be removed for being too close to another mod's object may be shrunk where it stands, down to this share of its size, to make it fit, in steps of 5%; the largest size that fits is used. Applies to the cleaned mod's own objects and to the moved ones of other mods. 100 or more: objects are never shrunk. At least 1: a lower value is raised to 1, with a warning in the log. |
| Turn by at most (degrees either way) | `360` | Such an object may be turned about its upright axis by up to this many degrees to the left or to the right to make it fit, in steps of 15 degrees; the smallest turn that fits is used. It is never tilted. 0: objects are never turned. 180 or more: objects may face any way. A negative value becomes 0, with a warning in the log. |

The larger size always comes first: each size is tried with no turn, then with the smallest turns, one way before the other. The limit itself is always the last size tried (for `82`: 100, 95, 90, 85, 82). A shrunk or turned object must also stay off terrain another mod changed. An object that fits nowhere is removed.

Only objects that are too close are shrunk or turned. These are never shrunk or turned, and are removed as before:
- an object on terrain another mod changed (see *Remove objects on terrain changed by other mods*);
- an object that another object of the cleaned mod rests on: that object stands on it from above, it holds at least *remove
  when this much support is gone (%)* of that object's support, and without it that object would no longer rest on what is
  below it (see *When an object rests on another*). This is the same with `EverythingTouchingIt` and `OnlyWhatLosesItsSupport`;
  objects that only touch it from the side, such as flat plants lying edge to edge, do not count. Shrinking or turning it
  would leave the object on it floating; removing it lets *Also remove what rests on removed objects* handle that object.
  With `Nothing`, no object of the cleaned mod counts here;
- whatever the settings, an object that another mod's object rests on, of the plugins *Other mods' objects left without
  support* checks (see *When an object rests on another*). When it is removed, that setting decides what becomes of the other
  mod's object, and *Also remove what rests on removed objects* what becomes of the cleaned mod's objects on that one. With
  `KeepSupportingObject`, such an object is kept as it is.

Only visible objects are shrunk or turned. Objects that are always kept (see *What is always kept*) and objects linked to
other objects of the cleaned mod are never shrunk or turned. An object kept shrunk or turned still stands where
it was: *Also remove what rests on removed objects* removes it like any other object when what it rests on is removed.

#### Also remove what rests on removed objects

| Setting | Default | Meaning |
| --- | --- | --- |
| When an object is removed, also remove | `OnlyWhatLosesItsSupport` | What else goes with a removed object (see below). |
| Gap that still counts as touching (game units) | `8` | Two objects this close or closer count as touching (0-64). Used by `EverythingTouchingIt` and `OnlyWhatLosesItsSupport`, and, whatever *When an object is removed, also remove* is set to, for other mods' objects (see *Other mods' objects left without support*). |
| OnlyWhatLosesItsSupport: remove when this much support is gone (%) | `50` | `OnlyWhatLosesItsSupport`: remove an object once this much of its support is gone (1-100). Whatever *When an object is removed, also remove* is set to, it also decides when another mod's object rests on an object or is left without support (see *Other mods' objects left without support*). Unless that is `Nothing`, it also decides when an object of the cleaned mod rests on another, which is then never shrunk or turned (see *Shrink or turn objects to make them fit*). |
| Other mods' objects left without support | `PullObjectToTheGround` | What happens to another mod's object resting on an object of the cleaned mod that is removed (see *Other mods' objects left without support*). Whatever it is set to, an object such an object rests on is never shrunk or turned. |

- **Nothing**: of the cleaned mod's objects, only those the removal steps remove (too close, changed terrain, visible objects left behind) are removed. (Other mods' objects are decided by *Other mods' objects left without support*, in every mode.)
- **EverythingTouchingIt**: every object connected to a removed one through touching objects is removed too, spreading outward one touching object at a time until nothing more is added. This can spread through floors and walls to whole rooms. Only the cleaned mod's visible objects are removed this way; a kept object (see *What is always kept*) is not removed and the spread does not pass through it.
- **OnlyWhatLosesItsSupport**: a touching object is removed only when at least *remove when this much support is gone (%)* of what it rests on or touches was removed. The ground and objects of any plugin, the base game included, count as support. It works best for objects whose mesh origin is at their base (true for most free-standing props); `mesh-origins.csv` (see *Reports and log*) shows how well that holds for the cleaned mod.

#### Other mods' objects left without support

This setting is in the *Also remove what rests on removed objects* group. *When an object is removed, also remove* applies to the
objects removed by any removal step (too close, changed terrain, visible objects left behind) and decides only
about the cleaned mod's objects; this setting decides about the objects of the plugins listed below (see *Which objects are
checked*) that rest on an object of the cleaned mod the run removes. Whatever it is set to, an object of the cleaned mod that
one of these objects rests on is never shrunk or turned to make it fit (see *Shrink or turn objects to make them fit*): it is
kept as it stands or removed.

- **KeepSupportingObject**: an object of the cleaned mod that another mod's object rests on is kept as it is: it is never
  removed. When the other mod's object would be left without support only if several objects of the cleaned mod were all taken
  away (two crates under one lantern, for example), all of them are kept. So is an object of the cleaned mod that such a kept
  object rests on, by the same test, and so on down, so the whole support under the other mod's object stays (a lantern of a patch on a book of the
  cleaned mod on a rock of the cleaned mod keeps both the book and the rock). Objects linked to a kept object are kept with it,
  as for every reason to keep.
- **RemoveFloatingObject**: the cleaned mod's objects are removed, shrunk or turned as decided, and the other mods' objects left
  without support are removed, written the way the cleaned mod's removed objects are. An object resting on such an object
  is checked in turn, so a stack goes as a whole.
- **PullObjectToTheGround** (default): the cleaned mod's objects are removed, shrunk or turned as decided, and each other mod's
  object left without support is moved straight down, keeping its rotation, until it rests on what is below it as the world
  ends up: the terrain, or an object of any plugin that stays, at its new size and turn when it was shrunk or turned. A stack
  lands from the bottom up: the lowest object first, each next one onto the one below it at its new place. When nothing is
  below it within 2048 units (for example in an interior whose floor was the removed object), it is removed instead.

**Which objects are checked:** the visible objects with a readable mesh in the spaces where the cleaned mod places objects, that
belong to one of these plugins:
- a base game plugin (Skyrim.esm, Update.esm, Dawnguard.esm, HearthFires.esm, Dragonborn.esm), whether or not the cleaned mod
  lists it as a master;
- a master of the cleaned mod;
- a mod trusted by your trust settings: one listed under *Trusted mods, and their masters* (its masters are not included), one
  that requires the cleaned mod (with *Also trust mods that require the cleaned mod, and their masters* on; its masters are not
  included), or one that loads before it (with *Trust mods loading before the cleaned mod* on).

A plugin listed under *Never trusted mods* is not one of these, even when it is a base game plugin or a master of the cleaned
mod. An object belongs to a plugin when that plugin created its record, or when the winning version of the record comes from it.
Objects that an earlier patcher in your Synthesis group (an earlier run of this one included) added to the patch are not
checked either; an object such a patcher changed in the patch is checked as an object of the plugin that created it.
Objects of untrusted mods, of never trusted mods, of mods trusted for their own changes only, and of plugins that are trusted
only because a trusted mod has them as a master (and that are not masters of the cleaned mod) are ignored: whatever this
setting is, they neither keep the cleaned mod's object nor are removed or pulled down. Placed NPCs and creatures, markers and
other invisible objects, effect meshes (fog, light rays) and the cleaned mod's own objects that a later plugin overrides are
never checked. Any object that stays, of any plugin, and the terrain still count as the ground an object is pulled down onto.

**When an object rests on another** and when it is left without support:
- An object rests on what is below it when it cannot move straight down by more than *Gap that still counts as touching*. An
  object that only touches the cleaned mod's object from the side, with nothing right below it, does not rest on it. An object
  partly sunk into another solid object rests on it when it is sunk deeper than the gap, or when its lower half is sunk in, however
  thin that object is (a plate sunk into a board). An object whose model is open somewhere (a room shell with an opening, for
  example) does not hold up what stands inside it, but it still counts as touching it, so an object standing inside such a model is
  never found left without support.
- An object is left without support when it rested on something before, at least *remove when this much support is gone (%)*
  of its surface that touched anything touches nothing any more, and it can then move straight down by more than the gap. A
  part of its surface that also touches the terrain or an object that stays is not lost. An object that only lost what stood
  on it or beside it, such as a base game floor under a removed table, stays as it is. All three choices use this one test.
- With `RemoveFloatingObject` and `PullObjectToTheGround`, the test is made once the cleaned mod's objects are removed, shrunk
  or turned and the other objects left without support are taken away.
- Another mod's object rests on objects of the cleaned mod when it would be left without support without them: each object
  of the cleaned mod that this is true of when it alone is taken away, or, when none is, all the cleaned mod's objects it
  touches, when it is true with all of them taken away. This decides which objects `KeepSupportingObject` keeps and which
  objects are never shrunk or turned, so an object that the other two choices would not find left without support keeps
  nothing. A base game floor under a table of the cleaned mod, for example, does not keep the table: the floor does not rest
  on the table.

Some objects of other mods are never removed, because something may depend on them or control them: persistent ones (quests,
packages and scripts can only point to those), teleport doors, objects that carry a map marker, objects with an enable parent, objects whose own
script names a quest or scene, objects with a location ref type, and objects another placed object links to (as its enable
parent, linked reference or script property, for example). When such an object would be removed (with `RemoveFloatingObject`,
or with `PullObjectToTheGround` when nothing is below it within 2048 units), it is left where it stands instead, and keeps
holding up what rests on it. Pulling an object down only changes its position, so with `PullObjectToTheGround` these objects
are pulled down like any other.

An object whose winning record comes from a plugin that loads after the patch cannot be changed by the patch: it is left as it is.

**The cleaned mod's objects on other mods' objects that are removed or pulled down.** With `RemoveFloatingObject` and
`PullObjectToTheGround`, an object of the cleaned mod that rests on or touches another mod's object that is removed or pulled
down goes as *When an object is removed, also remove* says for any object whose support is removed:
- `EverythingTouchingIt`: every visible object of the cleaned mod that touched that object where it stood is removed;
- `OnlyWhatLosesItsSupport`: an object of the cleaned mod that touched it is removed when at least *remove when this much support
  is gone (%)* of its support was on removed objects and on other mods' objects removed or pulled down;
- `Nothing`: the cleaned mod's object stays where it is, even when it is left floating.

What goes with these removals goes as usual (touching or resting objects of the cleaned mod, linked objects), and the other mods'
objects are then checked again: a lantern of a patch on a removed bowl of the cleaned mod is pulled down or removed in turn. This
repeats until no further object of the cleaned mod is removed. A kept object (see *What is always kept*) stays, as always.

The other mods' objects are checked once, after *Also remove what rests on removed objects* has followed up the main removals, *Visible objects left behind*
has run and *Also remove what rests on removed objects* has followed up the objects that step removed, and before
*Invisible objects left behind*, which also sees the cleaned mod's objects removed this way. That step and the step that moves kept markers see the
other mods' objects as this one leaves them: an invisible object of the cleaned mod does not count as inside another mod's
object that was removed or pulled down, and a kept marker is moved among the pulled-down objects at their new places, with the
removed ones gone.

#### Visible objects left behind

Removes the cleaned mod's visible objects when most of the cleaned mod's visible objects around them were removed. It runs after
*Also remove what rests on removed objects* has followed up the main removals (too close, changed terrain), so it sees all of those removals.
The objects it removes are then followed up by *Also remove what rests on removed objects* as well, the same way.

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove visible objects whose surroundings were removed | `true` | Turns this removal on. |
| When the cleaned mod's visible objects around it were removed: Look this far around it (game units) | `1024` | How far around to look for the cleaned mod's other visible objects (64-8192). |
| When the cleaned mod's visible objects around it were removed: Decide only if at least this many of the 8 directions have objects | `6` | A direction has objects when another visible object of the cleaned mod lies that way. With fewer such directions the object is kept (1-8). |
| When the cleaned mod's visible objects around it were removed: A direction is cleared when this much of it was removed (%) | `80` | Share removed for a direction to count as cleared. |
| When the cleaned mod's visible objects around it were removed: Remove when this share of the directions with objects is cleared (%) | `80` | Counted only among the directions that have objects, not among all 8. Example: objects lie in 7 directions and 6 of them are cleared: 6 of 7 is 86%, so it is removed with 80 or less and kept with 90 or more. |
| Never remove these as visible objects left behind (ticked = kept) | *(none ticked)* | One tick box per kind (see below). A ticked kind is kept by this step, an unticked kind can be removed by it. Does not affect the too-close or changed-terrain removals, and *Also remove what rests on removed objects* can still remove a ticked kind. |

The two percentages take values from 10 to 100 in steps of 10.

A visible object is removed when its surroundings were removed: the area around it is split into 8 directions (north, north-east,
east, ...), and at least *Decide only if at least this many of the 8 directions have objects* of the directions hold other visible objects of the cleaned mod,
and at least *Remove when this share of the directions with objects is cleared (%)* of those lost at least *A direction is cleared
when this much of it was removed (%)* of their ground area. Each object is judged against what the earlier steps removed; objects this
step removes do not count towards each other.

Only the cleaned mod's visible objects, including other mods' objects it moved, count as surroundings, never an object itself or invisible objects.
The objects listed under *What is always kept* are kept in any case, and so are the kinds ticked under *Never remove these as visible objects left behind*,
unless they are linked to a removed object. Every kind can be removed by default. The tick boxes are: NPCs and creatures, containers, furniture and activators.
Other visible objects (doors, lights, trees and plants, loose items, statics) have no tick box: only their surroundings decide.

Numbers out of range are corrected to the nearest valid value (a value that is not a number becomes
the default), and percentages are rounded to the nearest step of 10, each with a warning in the log.

#### Invisible objects left behind

| Setting | Default | Meaning |
| --- | --- | --- |
| Remove invisible objects left inside other mods' objects or alone | `true` | Removes the cleaned mod's invisible objects (sounds, lights, markers, spawners and the like) that sit inside another mod's object or whose surroundings were removed. Works with any *When an object is removed, also remove* setting. |
| When the cleaned mod's visible objects around it were removed: Look this far around it (game units) | `1024` | How far around to look for the cleaned mod's visible objects (64-8192). A light, sound or trigger box that reaches less far uses its own reach. |
| When the cleaned mod's visible objects around it were removed: Decide only if at least this many of the 8 directions have objects | `4` | A direction has objects when a visible object of the cleaned mod lies that way. With fewer such directions the invisible object is kept (1-8). |
| When the cleaned mod's visible objects around it were removed: A direction is cleared when this much of it was removed (%) | `50` | Share removed for a direction to count as cleared. |
| When the cleaned mod's visible objects around it were removed: Remove when this share of the directions with objects is cleared (%) | `60` | Counted only among the directions that have objects, not among all 8. Example: objects lie in 5 directions and 3 of them are cleared: 3 of 5 is 60%, so it is removed with 60 or less and kept with 70 or more. |
| Never remove these as invisible objects left behind (ticked = kept) | *(none ticked)* | One tick box per kind (see below). A ticked kind is kept by this step, an unticked kind can be removed by it. Does not affect the too-close or changed-terrain removals, and *Also remove what rests on removed objects* can still remove a ticked kind. |
| Move kept markers out of other mods' objects | `true` | Move a kept map, X (including heading), idle or other marker that sits inside another mod's object to the nearest free spot on the navmesh or, failing that, the ground. Only its position changes. Needs *Remove invisible objects left inside other mods' objects or alone* on; markers are kept when *Never remove these as invisible objects left behind* ticks them or something depends on them. Lights, sounds, acoustic spaces, trigger boxes, critter spawners, decals, furniture and door markers are never moved. |

The two percentages take values from 10 to 100 in steps of 10.

An invisible object is removed when:
- it sits inside another mod's object, such as a building or a boulder — not merely under a bridge or overhang, or
- its surroundings were removed: the area around it is split into 8 directions (north, north-east,
  east, ...), and at least *Decide only if at least this many of the 8 directions have objects* of the directions hold the cleaned mod's objects,
  and at least *Remove when this share of the directions with objects is cleared (%)* of those lost at least *A direction is cleared
  when this much of it was removed (%)* of their ground area. An object of the cleaned mod that the invisible object sits in counts in every direction.

Only the cleaned mod's visible objects, including other mods' objects it moved, count as surroundings. The objects listed under *What is always
kept* are kept in any case, and so are the kinds ticked under *Never remove these as invisible objects left behind*,
unless they are linked to a removed object.
The tick boxes are: map markers, position markers (XMarker, XMarkerHeading), idle animation spots, furniture use spots,
invisible marker doors (doors flagged as markers that lead nowhere), lights without a model, sound sources, echo areas (acoustic spaces), critter spawners,
invisible trigger areas, surface decals (dirt, blood) and, last, other invisible objects.

Numbers out of range are corrected to the nearest valid value (a value that is not a number becomes
the default), and percentages are rounded to the nearest step of 10, each with a warning in the log.
A setting with an invalid value (for example text that is not one of the dropdown choices) stops the
run; the log names the setting and what is allowed there.

## Batch mode

Batch mode cleans up several mods in one run, one after another, all into the same patch. Each mod has its own **config file** with its own settings. The **group file** is the list of config names to run, in order; it has nothing to do with a Synthesis group.

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

**Config file** (`configs\town_walls.json`): the same settings as the *Standard* section, written under their key names. **A config does not take any value from *Standard*; a setting it leaves out uses the built-in default.** Only `CleanedMod` is required; list just the settings you want to change.

To find the key names, copy them from the entries inside `Standard` in the patcher's `settings.json` (in its Synthesis folder `...\Synthesis\Data\<profile>\<patcher name>`) or from the configs in the `Example mod` folder, which list every setting. Dropdown choices are written as text (`"NpcHandling": "Never"`), plugins as `"Name.esp"`, lists as arrays (`["A.esp", "B.esp"]`).

```json
{
  "CleanedMod": "SomeTownMod.esp",
  "TooClose": { "BySize": { "SizeMultiplier": 1.0 } }
}
```

- *Detailed log and reports* is set once, next to *Mode*, and applies to every config of the batch.
- *Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch* is set once, next to *Mode*, and applies to every config of the batch.
- Upper and lower case in keys do not matter.
- With *Detailed log and reports* on, each config's log section also lists the settings that file changes from the default.
- Comments and trailing commas are allowed in the files.

**Order is priority.** The configs run in the listed order, and each one reads what the earlier ones wrote from the patch itself, as it reads any other plugin. A later config treats the objects an earlier one removed as gone, and the markers an earlier one moved as standing at their new position. An object that only clashed with something an earlier config removed is therefore kept.

**Same result as several copies of the patcher.** Adding this patcher several times to one Synthesis group, each copy in `Standard` mode with the settings of one config and in the order of the group file, gives the same patch as one `Batch` run of those configs, and for each mod the same log lines (apart from the lines that open a config's section) and the same report files. Such a chain may also clean up the same mod twice; the second copy then starts from what the first one wrote, like any later config.

**What stops the whole run.** Before anything is written, the patcher reads the group file and every config and checks them against your plugin list. If any of these is wrong, the run stops with a message that names the file and the problem, and no patch is written:
- no group file name is set, or a name is a path or has characters a file name cannot have;
- the group file or a listed config file is missing;
- a file is not valid JSON, the group file is not a list of names or the config is not an object;
- the group file lists nothing, or lists the same config twice;
- a setting has a value that cannot be read (for example text that is not one of the dropdown choices);
- a config sets no *Mod to clean up*, or the mod is not in your plugin list, is disabled, loads after the patch, was not loaded by Synthesis or is the patch this patcher writes;
- an enabled mod that requires a config's cleaned mod loads after the patch (unless *Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch* is on);
- the plugin list cannot be read (see *How to use*);
- two configs clean up the same mod.

A config that stops fails the whole batch. Its message names the config file, then gives the same sentence as the Standard stop and ends with *Nothing was run and no patch was written.*, for example *Config file X.json: CleanedMod.esp is disabled. Nothing was run and no patch was written.* A config that sets no mod to clean up reads *Config file <path> sets no mod to clean up (CleanedMod). Nothing was run and no patch was written.* The other messages of this list also end with *Nothing was run and no patch was written.*

**What does not stop the run.**
- A key the patcher does not know is ignored and listed in the log, so a misspelling is easy to find.
- A setting a config leaves out uses its default; the log lists those settings with the default used.
- A number out of range is corrected, with a warning in that config's log section.
- A mod with nothing to check ends its own section with *Nothing to check for this config. No changes made for it; the batch goes on.*

## What never causes removals

- Trusted mods (see *Which other mods can cause removals*): the base game (`Skyrim.esm`, `Update.esm`,
  `Dawnguard.esm`, `HearthFires.esm`, `Dragonborn.esm`), the cleaned mod and its masters; the plugins listed under
  *Trusted mods, and their masters*, with their masters; and, with the default settings, every plugin that loads before the
  cleaned mod (Creation Club plugins included) and every mod that requires the cleaned mod and has at most 5 other masters,
  with its masters. A plugin listed under *Never trusted mods*, an Added Object Remover patch of another group, and a mod that
  requires the cleaned mod but has more other masters (a merged plugin, a Bashed Patch), are untrusted. A trusted mod's object
  that an untrusted mod moves, resizes, swaps or switches on or off can cause removals (see *Other mods' objects*).
- Invisible objects, such as markers, lights without a model, sounds, trigger boxes and decals, and
  objects whose base record is missing: they never cause removals.
- Other mods' NPCs and creatures, except as set in *Other mods' NPCs and creatures cause removal*.
- Effect meshes, such as fog, light rays and water spray: they never cause removals.
- Objects the cleaned mod overrides or replaces (e.g. a piece of clutter the cleaned mod swaps
  for its own version of about the same size at the same spot). An object of another mod that the cleaned mod moved is
  checked instead (see *Objects of other mods that the cleaned mod moved*).
- Removed objects: an object whose final version is deleted, is initially disabled without an enable parent, or has
  the player as enable parent with the opposite state (the way xEdit, other mods and this patcher switch an object off
  for good). Every rule skips it: it is never checked, never causes a removal, and nothing is kept because of it.
- Other objects that start disabled: the cleaned mod's are never checked or removed; other mods' never
  cause removals, unless a parent object can enable them in game.
- The cleaned mod's objects listed under *What is always kept*.

Objects that patchers running earlier in the same Synthesis group added to the patch, or moved in it, can cause removals; the
patch itself is neither trusted nor untrusted.

## What is always kept

Whatever the settings, the cleaned mod's objects of these kinds are never removed (the last one only with one setting):
- teleport doors, and the doors they lead to;
- anything another mod's object, a quest, an AI package, a scene, a location or a navmesh points to
  (or any other record, such as a faction, a form list or dialogue);
- objects whose own script names a quest or a scene, or whose base object comes from the cleaned mod and
  has a script that names one. A base object from another plugin also serves that plugin's own copies, so
  its script does not keep the cleaned mod's copies;
- objects with a location role: a location ref type of their own. Map markers are not kept for this reason alone;
- with *Other mods' objects left without support* on `KeepSupportingObject`: objects that another mod's object rests on, and
  the objects those rest on (see *Other mods' objects left without support*).

Objects of the cleaned mod that are linked to each other (enable parent, linked references, script
properties, ...) are always removed together or kept together. The log names the reason each object was kept.

**Map markers** stay removable. A removed map marker is disabled where it stands instead of being
moved, so it is hidden from the world map. Markers a location or quest uses are always kept.

## Reports and log

The normal log shows: the settings in use, warnings and errors, the final summaries of removed, kept and moved objects, one line per kept object and a summary of unreadable meshes. Turn on *Detailed log and reports* for every removed object and why, per-space counts, unreadable-mesh details, timings, processing statistics and the report files. Unreadable meshes are listed together near the end of the log, after the patch is written.

In the detailed log, each object removed for being too close names the other mod's object and how that object changed, for
example `too close to Wall [012345:Skyrim.esm] (winning override in CityMod.esp), changed by CityMod.esp (accepted value from
Skyrim.esm)`: `CityMod.esp` is the first plugin after the accepted value that already holds the value the game ends up with, and
`Skyrim.esm` holds the accepted value. When several of the object's values changed, the plugin that loads last of those that
changed them is named, with the accepted value of the value it changed. `(new object)` stands instead when no trusted plugin has
the object. A change that only the patch holds is named only when there is no other: `an earlier patcher in this group` stands
instead of a plugin only when the patch alone holds every change of the object. The scan lines of the detailed log also count
the objects of other mods that the game shows with their accepted values.

Every run's summary, in both logs, has one line with where the run started and what it did, like `Objects of SomeTownMod.esp: 1240; already removed by earlier patchers in this group: 12; not judged (winning version from a later plugin, removed by a later plugin, deleted, start disabled, or without a valid position or rotation): 5; judged in this run: 1223; removed in this run: 37.` The first number counts all objects of the cleaned mod. The second counts those a patcher that ran earlier in the same Synthesis group (an earlier config of the same Batch run, or an earlier copy of this patcher, included) already removed in the patch; they are not checked again. The third counts those this run does not check: objects whose winning version comes from a later-loading plugin, objects a later-loading plugin removes, and objects that are deleted, start disabled or have no valid position or rotation (the scan lines count each kind). The fourth counts those this run checked, the fifth those this run removed. The line is printed in every run, also in one that stops with *Nothing to check*; the fourth and fifth numbers are then 0.

In `Batch` mode the banner prints once, then each config has its own section that starts with a line like `=== Config 1 of 2: town_walls.json, mod to clean up: SomeTownMod.esp ===`. Inside it come that config's notes (keys that were ignored, settings left at their defaults) and then the usual log of that run, ending with its own *Done* line. The notes look like this:

```
Ignored in town_walls.json, because this patcher has no such setting (check the spelling and the section it is in): Colour.
Not set in town_walls.json, defaults used: TooClose.UseRoughBoxes = false, TooClose.NpcHandling = "OnlyWhenStuckInsideTheObjectWithCollision", FollowUpRemoval.TouchDistance = 8.0
```

Every setting the config leaves out is listed with its key and the default used, written as the config file would hold it, in the order of the settings screen.

After the removal summary, when the cleaned mod moved objects of other mods, a line counts them and what became of them, like `Objects of other mods that SomeTownMod.esp moved: 9; kept where SomeTownMod.esp put them: 7; kept there shrunk or turned: 1; removed: 1.`, and a line like `SomeTownMod.esp objects kept shrunk or turned: 4.` counts the cleaned mod's own objects kept shrunk or turned. The detailed log lists each of these objects, with its new size and turn. These moved objects are not counted in the run summary line; the removed ones are listed with the other removals.

The removal summary, in both logs, counts every removed object by why it was removed, like `Removed 2842 objects (238 on changed terrain, 2494 too close, 48 follow-up removals (24 of them first shrunk or turned), 17 visible objects left behind, 3 invisible objects left behind, 59 linked to removed objects); 2769 of them SomeTownMod.esp objects, 73 objects of other mods that SomeTownMod.esp moved.` The visible objects left behind item is printed only when that step is on. A follow-up removal *first shrunk or turned* was kept shrunk or turned to fit at first, then removed because what it rested on was removed. The split into the cleaned mod's objects and the moved objects of other mods is printed only when a moved object was removed; the first number matches *removed in this run* of the run summary line.

The lines that report how many objects were found on changed terrain or too close, like `Found 3071 of 4436 objects (4076 SomeTownMod.esp objects and 360 objects of other mods that SomeTownMod.esp moved) too close to other mods' objects.`, count the moved objects of other mods with the cleaned mod's objects and name both numbers; without moved objects they read like `Found 25 of 1098 SomeTownMod.esp objects too close to other mods' objects.` In the detailed log, the time on the too-close line does not include the shrink or turn check, which runs before it and checks many of the same objects; the line says so.

After those lines, when another mod's object was left without support, a line counts them by what became of them, like
`Other mods' objects left without support: 5; pulled down: 3; removed, nothing below within 2048 units: 1; left where they stand, not removable: 1.`
Only the outcomes that happened are named. They are: pulled down; removed (with `RemoveFloatingObject`); removed, nothing below
within 2048 units; left where they stand, still touching what is below them; left where they stand, not removable; left where
they stand, nothing below within 2048 units and not removable; left as they are, a plugin after the patch wins them. The detailed
log lists each object with its position, its outcome (how far it was pulled down, or what kept it from being removed, such as
`persistent` or `another placed object links to it`) and how much of its support is gone, and also gives the step's count and
time, with the number of the cleaned mod's objects removed with them when there are any. Those removals are listed with the
other removals and counted with the follow-up removals; in the detailed log their reason names the other mod's object, like
`touches Lantern [012345:SomePatch.esp], another mod's object left without support` (with `EverythingTouchingIt`) or
`60 % of its support was removed or pulled down (mostly Lantern [012345:SomePatch.esp], another mod's object left without
support)` (with `OnlyWhatLosesItsSupport`). With `KeepSupportingObject`, the detailed log counts the cleaned mod's objects kept
because other mods' objects rest on them, like `Objects of SomeTownMod.esp that other mods' objects rest on, kept: 29 (22
directly, 7 under those).`: those another mod's object rests on directly, and those these rest on, and so on down. Only the
ones that would otherwise have been removed get a *Kept* line.

With *Detailed log and reports* on, a line like `Objects too close where they stand: 126; not shrunk or turned because other objects rest on them: 6; tried shrunk or turned: 120; fit: 95.` counts the objects that may be shrunk or turned and were too close where they stood, those not tried because other objects rest on them (of the cleaned mod or of other mods), those tried, and those that fit; the others are removed. The line that reports the written patch, like `Wrote 140 overrides (40 removed, 5 kept markers moved, 95 shrunk or turned to fit).`, names the kept markers moved out of other mods' objects (see *Invisible objects left behind*) and the shrunk or turned objects only when there are any, and likewise the other mods' objects pulled down and removed (`Wrote 144 overrides (40 removed, 5 kept markers moved, 95 shrunk or turned to fit; other mods' objects: 3 pulled down, 1 removed).`).

Right after the kept summary, each object kept for a reason of its own gets a line saying why: *Kept, script names a quest or scene* (its own script, or its base object's from the cleaned mod), *Kept, location role* (a location ref type of its own) or, with `KeepSupportingObject`, *Kept, another mod's object rests on it* (naming that object, like `...: Lantern [012345:SomePatch.esp].`, or the kept object of the cleaned mod resting on it and what rests on that one, like `...: Book [...] rests on it; Lantern [...] rests on that.`). They are kept on purpose and are not in the *Kept objects that would have been removed, to check by hand* list. When an object is kept because another mod's object rests on it, the kept summary's first line also names that reason, and the summary counts these objects on a line of their own, like `another mod's object rests on it: 4`.

Near the end of the log come two lists, each under its own heading and printed only when it has objects: *Removed markers that actors or the map use* (removed door, map, idle and furniture markers) and *Kept objects that would have been removed, to check by hand* (grouped by why: part of a linked group, kept because something depends on it, or a teleport door). With *Detailed log and reports* on, the locations of the written files follow.

A record whose data is malformed in its plugin does not stop the run. The log has one warning line for it, right after the scan section, for example *Warning: could not read Perk SomePerk [...]: PerkEntryPointModifyActorValue did not have expected parameter type flag: Float. Its links were not all checked.* A record is reported once per run: in `Batch` mode, each config that reads it reports it in its own part of the log. The links of the record that were read before the problem count as usual, and nothing is kept or removed because of the record. The rest of that plugin is still read.

Before that section, the log warns about removed objects that can still affect the game:
- each removed unique NPC that a quest fills an alias with (as its *Unique Actor*), when that alias is neither *Optional* nor *Allow Disabled*, for example *Warning: removed unique NPC BanditChief [...] in ...: Quest SomeQuest [...] fills an alias with it as a Unique Actor; a disabled actor cannot fill that alias.*;
- the number of removed objects in their worldspace's large-reference list: *Warning: 3 removed objects are in their worldspace's large-reference list; distant LOD made before this run still shows them.*

The last section of the log says where these objects are: the removed objects, the kept ones (one tree for each reason they were kept), the removed markers that actors or the map use, the kept objects to check by hand, removed unique NPCs and removed large references, each only when there are any. Each is shown as a tree of three levels, each level indented further:
- **worldspace**: the worldspace (for example *Skyrim*), or for an object in an interior, the interior's name;
- **hold**: the hold-level location the object's cell belongs to (for example *The Pale hold*): the nearest location of the cell's location chain that the game marks as a hold; without one, the location just below the top of the chain;
- **place**: the most specific name: the cell's location (*Loreius Farm*), else the cell's own name, else *near* the location of the nearest named cell within 3 cells (*near Loreius Farm*), else the cell grid (*cell 7,6*).

Names are what the game shows (the location or cell name), or their editor ID when they have none. A cell without a location takes the hold of the nearest cell within 3 cells that has one; when there is none, the hold is *unnamed area*. Objects in a worldspace's persistent cell are counted in the grid cell they stand in.

Every area of the tree is listed. Each line ends with two numbers: the objects of that list in the area, and all checked objects there: the cleaned mod's objects and the objects of other mods it moved (for the removals: *removed / checked objects there*). The first line of each tree gives the same two numbers for the whole tree, e.g. *Removed 1284/9850 (removed/all checked objects):*; the other trees have matching headers (*Kept 12/9850 (kept/all checked objects), teleport door:*, *Removed markers that actors or the map use: 7 of 9850:*, *Kept objects that would have been removed, to check by hand: 3 of 9850:*). Areas with the most objects come first; areas with the same number come in order of fewer of the cleaned mod's objects there (a larger share removed), then by name. A place and the *near* place of the same name (e.g. *Loreius Farm* and *near Loreius Farm*) stay together, the place first, and are ranked as one by their combined numbers. A worldspace or hold with only one area below it gets no line of its own: its name is joined to that area's line with *›*, and the joined line sits at the level of its first name. A name that repeats an earlier name of the joined line is left out.

```
Removed 1284/9850 (removed/all checked objects):
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

If something goes wrong that the patcher did not expect, the log shows a line starting with "Unexpected error" that names the part that failed, the error, and what is missing from the results because of it. The run carries on and the patch is still written. When it happens for single objects (an object of the cleaned mod that is too close or on changed terrain, an other-mod object that can cause removals, an object of the cleaned mod that might have to be kept, or an object tried shrunk or turned), only that object is skipped: an object of the cleaned mod whose check failed is kept and not removed, an other-mod object that failed causes no removals, and a size or turn whose check failed is not used. When finding the objects other objects rest on fails, no object is shrunk or turned, so the objects that were too close are removed. Objects with the same error are grouped into one line with their number and a few examples; turn on the detailed log to list all of them. Besides the stops described in *How to use* and *What stops the whole run*, the run stops only if a plugin can't be read or the patch can't be written.

When such an error stops a whole part of the run (*Also remove what rests on removed objects* (either time it runs), *Visible objects left behind*, *Invisible objects left behind*, moving kept markers, finding the cleaned mod's objects other mods' objects rest on, the step for other mods' objects left without support, the report files, or the details in them), the log says so with a line like "Also remove: not performed because of an unexpected error.", and the rest of the run still goes on. When finding the objects other mods' objects rest on fails, no object is kept for that reason; when the step for other mods' objects left without support fails, no other mod's object is removed or pulled down, and no object of the cleaned mod is removed because of them.

If the report files from earlier runs cannot be deleted or a report file cannot be written, the log has a line like "Warning: writing removals.csv in <folder> failed: <error>" (or "Warning: deleting earlier diagnostics files in <folder> failed: <error>") that names the folder, the file and the error. When a game archive, the folder of archives or the archive list in the game INI cannot be read, the log has a line like "Warning: could not read archive <name>: <error>", "Warning: could not list archives in <folder>: <error>" or "Warning: could not read archive list from game INI: <error>".

With *Detailed log and reports* on, CSV files (UTF-8, comma-separated, header in the first row) are written to the `Reports` folder inside this patcher's own folder in Synthesis, `...\Synthesis\Data\<profile>\<patcher name>\Reports` (the folder that also holds its `settings.json`), where they last between runs. The log's settings lines show the full path. At the start of each run this patcher's report files from earlier runs are deleted from that folder. In `Batch` mode each config writes to a subfolder of it named after the config (`town_walls` for `town_walls.json`), and only that subfolder's earlier files are deleted. Which files appear depends on your settings:

- **`anchoring.csv`**: why each object was removed or kept, both when *Also remove what rests on removed objects* followed up the main removals and when it followed up the visible objects left behind. Only with *When an object is removed, also remove* `OnlyWhatLosesItsSupport` and something removed for being too close, on changed terrain or as a visible object left behind.
- **`mesh-origins.csv`**: how the origin of each mesh the cleaned mod's objects use sits (near the bottom, near the centre, or elsewhere); check it if `OnlyWhatLosesItsSupport` removes or keeps unexpected objects. Only with *When an object is removed, also remove* `OnlyWhatLosesItsSupport` and something removed for being too close or on changed terrain.
- **`edges.csv`** and **`components.csv`**: which objects were found touching and which chains were removed together. Only with *When an object is removed, also remove* `EverythingTouchingIt` and something removed for being too close or on changed terrain; the follow-up of the visible objects left behind is not in them.
- **`leftover-visible-objects.csv`** (the *Visible objects left behind* step): why each visible object checked was removed or kept, with the ground area covered in each of the 8 directions and how much of it was removed. Only with *Remove visible objects whose surroundings were removed* on.
- **`leftover-invisible-objects.csv`** (the *Invisible objects left behind* step): why each invisible object checked was removed or kept, and where it was moved. Only with *Remove invisible objects left inside other mods' objects or alone* on.
- **`removals.csv`**: every removed object of the cleaned mod (and every removed object of another mod that the cleaned mod moved) with where it is, always written. Columns: formKey, editorId, base, reason, worldspace, hold, place, cellFormKey, cellEditorId, gridX, gridY, x, y, z. Sorted by worldspace, hold and place, then by form key.
- **`moved-and-fitted-objects.csv`**: every object of another mod that the cleaned mod moved, with what became of it (kept where the cleaned mod put it, kept there shrunk or turned, removed), and every object of the cleaned mod kept shrunk or turned. Only when there is such an object. Columns: outcome, formKey, editorId, base, worldspace, x, y, z, rotZDeg, scale, and the new placement newX, newY, newZ, newRotZDeg, newScale (empty when the placement did not change). x, y, z, rotZDeg and scale are the placement before shrinking or turning. The outcome is `moved object kept where moved`, `moved object kept shrunk or turned` or `moved object removed` for an object of another mod that the cleaned mod moved, and `own object shrunk or turned` for an object of the cleaned mod.
- **`other-mods-objects-left-without-support.csv`**: every other mod's object left without support, with what became of it. Only when there is such an object. Columns: outcome, formKey, editorId, base, winningMod, worldspace, x, y, z, newZ (the height it was pulled down to; empty when it was not pulled down), supportGoneShare (0-1), notRemovableBecause (what kept it from being removed; empty otherwise). The outcome is worded as in the detailed log's lines, for example `pulled down`, `removed`, `left where it stands, not removable`. Other mods' objects removed this way are listed only here and in the detailed log.
- **`removed-markers-and-kept-objects.csv`**: the rows of the *Removed markers that actors or the map use* and *Kept objects that would have been removed, to check by hand* lists, always written. Columns: kind (`RemovedMarker` for a removed marker; `KeptLinkedGroup`, `KeptForNonPlacedReference` or `KeptTeleportDoor` for a kept object), formKey, editorId, space, cell, detail.

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
- The cleaned mod's objects that a later-loading plugin changes are left alone, even if they still clip with
  something in game. Those an earlier patcher in the same group changed in the patch are checked as that patcher left them.
- The cleaned mod's effect-only objects (such as fog or light rays) are never removed unless linked
  to a removed object. An object whose mesh can't be read is never removed by *Also remove what rests on removed objects*; the log
  lists unreadable meshes.
- Another mod's object is pulled down by points spread over its surface, so a narrow edge of what is below it that passes
  between those points may end up slightly inside it. The underside of something that rests on the object is not taken as
  ground to land on.
- A kept marker is moved only up to 2048 units and prefers staying in its own cell; in tight
  interiors a free spot often can't be found, so it may be left where it is (the log says so). A marker moved out of its own cell gets a warning that names the neighbouring cell it was moved into, such as *Warning: SomeMarker was moved into the neighbouring cell (7, 6) because its own cell has no free spot.*
- The cleaned mod must load above the patch in your plugin list, and so must every enabled mod that requires it;
  otherwise the run stops (see *How to use*). For a mod that requires the cleaned mod, *Only warn (don't stop) when a mod
  that requires the cleaned mod loads after the patch* turns the stop into a warning.
- Keep master-flagged plugins (`.esm`, `.esl`, and plugins flagged as masters) above the patch; Mod Organizer 2 and LOOT keep
  them at the top. The game loads them before all other plugins wherever they stand in your plugin list, but the patcher, like
  Synthesis, uses the plain order of the list: a master-flagged plugin listed after the patch is treated as loading after it, so
  it can stop the run as a mod that requires the cleaned mod, and its records count as beating the patch.
- What the previous Synthesis run wrote into the patch is not seen: the patch file it left is not read, and every run
  starts from an empty patch. Only what earlier patchers wrote in the same run (earlier patchers of the group, or earlier
  configs of a `Batch` run) is seen.
- Plugins you blacklisted for the group in Synthesis are left out above the patch, but a blacklisted plugin that loads
  after the patch is still judged as part of the world: the patcher cannot see the group's blacklist.
- Names and other translated text of plugins that load after the patch are read in the default language, so the log may
  show them differently from the game.

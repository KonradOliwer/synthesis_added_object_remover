# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, which is a common source of clipping and duplicated clutter when several
mods edit the same area. The output plugin is `AddedObjectRemover.esp`.

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| Target plugin | *(empty)* | File name of the plugin whose added objects are checked, e.g. `SomeMod.esp`. Case-insensitive. If it is empty or not in the load order, the patcher logs this and makes no changes. |
| Size multiplier | `0.5` | How far a target object reaches beyond its own bounds, as a fraction of its size on each axis (see below). |
| Excluded plugins | *(empty)* | Plugins whose objects are never treated as "other mods" objects. |
| Exclude masters of the target plugin | `true` | Masters of the target plugin are also excluded. |
| Measure size from meshes (NIF) | `true` | Measure base objects from their NIF mesh, falling back to Object Bounds (OBND). If disabled, only OBND is used. |
| Keep referenced objects | `true` | Never remove target objects that other placed objects use as Enable Parent or Linked Reference, teleport doors, or objects that are another door's teleport destination. Kept objects are listed in the log. |
| Verbose logging | `false` | Log every removed object (FormKey, EditorID, base, cell/worldspace, the conflicting object and its plugin), per-space counts and unreadable meshes. |

The base game plugins `Skyrim.esm`, `Update.esm`, `Dawnguard.esm`, `HearthFires.esm` and
`Dragonborn.esm` never count as other mods, and neither does the patcher's own output plugin
(`AddedObjectRemover.esp`), so objects placed by an earlier patcher in the same Synthesis run
are never treated as "other mods" objects either. Creation Club plugins are **not** excluded
automatically; add them to *Excluded plugins* if needed.

## Algorithm

1. **Collect placed objects.** Every winning placed record is scanned: REFR, ACHR and all placed
   trap/hazard/projectile types (arrow, barrier, beam, cone, flame, missile, trap). Each plugin's
   cell tree (interior cells, worldspace persistent cells and exterior cells) is walked once, in
   load-order priority, and the first version found of each record is its winning override.
   Deleted and Initially Disabled records are ignored.
   - *Target objects*: records created by the target plugin (FormKey belongs to it) whose winning
     version is also from the target plugin. Records that a later plugin overrides are left alone
     (this includes the patcher's own output plugin, if an earlier patcher in the same Synthesis
     run already touched the object), and overrides by the target plugin of other plugins' records
     are not target objects.
   - *Other objects*: records created by any plugin except the base game, the target plugin, the
     patcher's own output plugin, the excluded plugins and (optionally) the target's masters.
     Their winning version is used.
2. **Spaces.** Interior objects are only compared with objects in the same cell. Exterior objects
   (including the worldspace's persistent cell) are compared across the whole worldspace in world
   coordinates.
3. **Bounds.** A base object's local bounding box comes from its NIF mesh (loose file in the Data
   folder first, then BSA archives, later archives overriding earlier ones), else from its OBND,
   else a zero-size box at the origin. NPCs use OBND. Results are cached per model and per base.
   Mesh bounds only count triangle geometry reachable from the NIF's root node. Hidden and
   `EditorMarker` shapes/nodes, particle systems and orphan (unreferenced) blocks are ignored. For
   `BSDynamicTriShape` the dynamic vertex data is used. A shape without usable vertices falls
   back to its bounding sphere, unless that sphere has zero radius.
4. **Too-close test.** For each target object:
   - Its local box is scaled by the reference scale. With `d = (max - min) * scale` per axis, the
     box is grown to `[min*scale - multiplier*d, max*scale + multiplier*d]`.
   - Each other object is represented by the world-space center of its own bounds:
     `position + R * (scale * localCenter)`.
   - That point is transformed into the target's local frame, `R^T * (p - position)`, and tested
     against the grown box (inclusive). One hit is enough.
   - A uniform spatial hash (512-unit cells) of the other objects keeps this fast. Only the cells
     overlapping the world AABB of the rotated grown box, widened by 4096 units, are checked.
5. **Rotation convention.** Placement rotations are radians. The engine rotates clockwise
   (left-handed) about each axis, so the world matrix is `R = Rx(-x) * Ry(-y) * Rz(-z)` using
   standard right-handed matrices applied to column vectors. This lives in one function,
   `Geometry.RotationFromEuler`, and still needs to be verified in-game.

## How objects are removed

Each object to remove is copied into the patch as an override, and only two things change:

- the **Initially Disabled** flag is set;
- the Z position is set to **-30000** (X/Y unchanged).

Nothing else on the record changes. No Enable Parent is added, and no records are deleted, so
references from scripts, quests and other records still resolve.

## Limitations

- An object's size is approximated by an axis-aligned box in its own local space. Another object
  counts only by the center of its bounds, so big objects that overlap only at their edges are not
  detected.
- NIF bounds come from mesh geometry and can differ from what you see in-game (e.g. particles are
  ignored; collision-only or skinned parts). OBND values can be missing or inaccurate.
- Other objects are found through a spatial hash of their raw positions, searched 4096 units
  beyond the target's box. An object whose mesh center is more than that far from its own
  origin (e.g. some combined meshes) can be missed.
- The rotation convention above has not yet been checked in-game.
- References are only checked through Enable Parent, Linked References and door teleports. Scripts,
  quest aliases, packages and other dependencies are **not** checked. Review the verbose log if
  the target mod relies on scripted objects.
- The archive list comes from the game INI plus `<Plugin>.bsa` / `<Plugin> - *.bsa` for each
  plugin in load order. Archives loaded in other ways are not searched.
- Only the target plugin's own, unmodified objects are checked. Objects that a later plugin
  overrides are skipped.

## Building

```
dotnet build AddedObjectRemover.sln
```

Requires the .NET 10 SDK. Add the repository or the built project to a Synthesis profile as usual.

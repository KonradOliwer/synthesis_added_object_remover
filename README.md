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
| Remove touching objects | `true` | Also remove target objects whose mesh touches a removed target object, repeatedly (whole touching groups), so e.g. a tree standing on a removed rock does not stay floating. See *Touching objects* below. |
| Touch tolerance | `8` | Maximum gap in game units between two mesh surfaces for them to count as touching. Must be 0 or more. |
| Voxel size | `8` | Edge length (mesh units) of the voxels indexing mesh surfaces for the touch test; surfaces are sampled every voxel size / 2. Minimum 1. Smaller is more precise but slower and uses more memory. |
| Verbose logging | `false` | Log every removed object (FormKey, EditorID, base, cell/worldspace, the conflicting object and its plugin), per-space counts and unreadable meshes. |
| Ignore replaced objects | `true` | Do not treat another mod's object as an "other mod" object when a target plugin object in the same space sits at essentially the same position and has a similar size (looks like the target plugin replaced it). See *Replaced objects* below. Records the target plugin itself overrides are always ignored this way, regardless of this setting. |
| Replacement position tolerance | `16` | Maximum distance, in game units, between a target object's position and another mod's object's position to be a possible replacement match. Must be 0 or more. |
| Replacement size similarity | `0.75` | Minimum smallest-to-largest ratio, per matching sorted dimension, between a target object's and another mod's object's scaled bounds for them to count as a replacement match (`0.75` = within about 25%). Clamped to 0-1. |

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
   `EditorMarker` shapes/nodes, particle systems and orphan (unreferenced) blocks are ignored.
   Objects with a time controller are never treated as hidden (animated NIFs show such parts at
   runtime), and if the hidden flag leaves a mesh with no geometry at all, it is read again
   ignoring the hidden flag. A mesh that still yields nothing is logged (verbose) with per-reason
   shape counts (hidden, editor marker, unsupported types, no vertices, unreachable). For
   `BSDynamicTriShape` the dynamic vertex data is used. A shape without usable vertices falls
   back to its bounding sphere, unless that sphere has zero radius.
4. **Too-close test.** For each target object:
   - Its local box is scaled by the reference scale. With `d = (max - min) * scale` per axis, the
     box is grown to `[min*scale - multiplier*d, max*scale + multiplier*d]`.
   - Each other object is represented by the world-space center of its own bounds:
     `position + R * (scale * localCenter)`.
   - That point is transformed into the target's local frame, `R^T * (p - position)`, and tested
     against the grown box (inclusive). One hit is enough.
   - A uniform spatial hash (512-unit cells) of the other objects' raw positions keeps this fast.
     Only the cells overlapping the world AABB of the rotated grown box, widened by 4096 units,
     are checked. An other object's bounds center (and so its mesh) is computed only the first
     time a query turns it up, then cached; objects that are never candidates are never measured.
5. **Touching objects** (if *Remove touching objects* is on and meshes are used). Only target
   plugin objects are considered, and only within the same interior cell or worldspace.
   - *Broad phase*: each target's oriented (rotated, scaled) bounding box, grown by the touch
     tolerance, is tested against nearby targets' boxes (spatial hash of world AABBs, then an
     exact oriented-box separating-axis test). Boxes only select candidate pairs; they never
     decide that two objects touch.
   - *Narrow phase*: the actual render triangles (same shapes and node transforms as the bounds).
     Each unique mesh is voxelized once: every triangle is sampled in rows at most *voxel size / 4*
     apart with points at most *voxel size / 2* apart, so every surface point is within
     `0.71 * voxel size / 2` of a sample; samples are bucketed in voxels of *voxel size*, and each
     voxel lists the triangles sampled into it. For a pair, the samples of the mesh with fewer
     voxels are transformed to world space (position, rotation, scale) and into the other mesh's
     local frame, and the **exact point-to-triangle distance** to the nearby triangles is
     compared with the tolerance. The voxels are only an index.
   - *Accuracy*: a sample lies on its mesh's surface, so a pair is only reported touching if the
     real surfaces come within the tolerance (no false positives beyond it, whatever the voxel
     size). A gap smaller than `tolerance - 0.71 * voxelSize / 2 * scale` of the sampled object is
     always found (defaults: gaps up to about 5.2 units always count, gaps over 8 never count).
     Meshes that would need more than 2,000,000 samples are sampled more coarsely (logged).
   - Objects without mesh triangles (OBND fallback, unreadable mesh, NPCs) never touch anything;
     such pairs are counted in the log.
   - *Clusters*: every target object connected to a too-close removal through a chain of
     touching target objects is removed as well, however large the group (connected components
     of the "touches" graph, no size cap). Objects kept by *Keep referenced objects* stay and do
     not pass the removal on. The log reports components, the largest component and pair counts.
6. **Replaced objects.** The target plugin often *replaces* an existing object (e.g. a tree) with
   its own new one at the same spot; such replaced other-mod objects must not trigger proximity
   removals. Two exclusions are applied to the "other objects" set before the too-close test:
   - *Overridden by the target plugin* (always on): any placed record present in the target
     plugin's own cell tree that originates from another plugin (i.e. the target plugin overrides
     it) is never an "other object", no matter which plugin ends up winning that record. Collected
     during the existing pass over the target plugin's own cells (no extra full load-order pass).
   - *Same-position lookalike* (*Ignore replaced objects*): an other object is excluded from
     proximity checks entirely when some target object in the same space has its position within
     *Replacement position tolerance* of it (`Vector3.Distance` between the two reference
     positions) **and** a similar size: each object's scaled local bounds dimensions
     (`(max - min) * scale`) are sorted largest-first, and the three matching pairs must each have
     a min/max ratio of at least *Replacement size similarity*. If either object has no bounds
     (zero-size box - unresolved base, missing OBND/mesh), it is never treated as a match. Only
     other objects near a target object are ever measured, by reusing each space's own spatial
     grid (a small position-tolerance query per target); bounds are computed lazily and cached, as
     elsewhere. The pass runs in parallel per target and is thread-safe (an interlocked
     compare-exchange marks an object excluded at most once); matches are logged (verbose) in a
     fixed, deterministic order afterwards.
7. **Rotation convention.** Placement rotations are radians. The engine rotates clockwise
   (left-handed) about each axis, so the world matrix is `R = Rx(-x) * Ry(-y) * Rz(-z)` using
   standard right-handed matrices applied to column vectors. This lives in one function,
   `Geometry.RotationFromEuler`, and still needs to be verified in-game.

## Performance

- The too-close tests (one per target object) and the touch broad/narrow phases run in
  parallel on all CPU cores; the thread count is logged. Writing the overrides into the patch
  is single-threaded and happens after all computation.
- Target base objects are measured first in a parallel warm-up. Each mesh is read and parsed
  once; the same parse yields its bounds and, for target meshes, its triangles.
- Other objects' bounds centers are computed lazily, once each, and only for objects a query
  turns up.
- NIFs are parsed in parallel. NiflySharp's only static state touched while loading (a
  block-type lookup table) is built once under a lock before parallel parsing starts. BSA reads
  open their own file stream per read and need no lock.
- Verbose output from parallel phases (mesh messages) is collected and printed afterwards in a
  fixed order, so logs are deterministic.
- The log shows the time of each phase: scan, indexing, bounds warm-up, replacement matching,
  too-close search, touch setup / broad phase / narrow phase / clusters, and writing.

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
- Touching uses render triangles only (no collision), so an object fully inside another one
  without their surfaces coming within the tolerance does not count as touching. Legacy
  `NiTriStrips` shapes contribute only their vertices (as points) to the touch test.
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

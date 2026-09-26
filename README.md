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
| Keep referenced objects | `true` | Never remove target objects that other placed objects link to (Enable Parent, Linked Reference, Activate Parent, door teleport destination, script properties or any other reference field), or teleport doors. Kept objects are listed in the log. |
| Remove touching objects | `true` | Also remove target objects whose mesh touches a removed target object, repeatedly (whole touching groups), so e.g. a tree standing on a removed rock does not stay floating. See *Touching objects* below. |
| Touch tolerance | `8` | Maximum gap in game units between two mesh surfaces for them to count as touching. Must be 0 or more. The test is exact (see *Touching objects*). |
| Voxel size | `8` | No longer used: the touch test compares triangles exactly and needs no voxels. Kept so saved settings still load. |
| Verbose logging | `false` | Log every removed object (FormKey, EditorID, base, cell/worldspace, the conflicting object and its plugin), per-space counts and unreadable meshes. |
| Ignore replaced objects | `true` | Do not treat another mod's object as an "other mod" object when a target plugin object in the same space sits at essentially the same position and has a similar size (looks like the target plugin replaced it). See *Replaced objects* below. Records the target plugin itself overrides are always ignored this way, regardless of this setting. |
| Replacement position tolerance | `16` | Maximum distance, in game units, between a target object's position and another mod's object's position to be a possible replacement match. Must be 0 or more. |
| Replacement size similarity | `0.75` | Minimum smallest-to-largest ratio, per matching sorted dimension, between a target object's and another mod's object's scaled bounds for them to count as a replacement match (`0.75` = within about 25%). Clamped to 0-1. |
| Touch diagnostics file | *(empty)* | Optional path of a file to write touch-diagnostics CSVs to (see *Touch diagnostics* below). Empty writes nothing (default) and has no effect on results or performance. Relative paths resolve against the patcher's working directory; an absolute path is recommended. |

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
   back to its bounding sphere, unless that sphere has zero radius. A mesh with any non-finite
   vertex, or any coordinate beyond ±1,000,000 units (broken exports, sentinel values), is treated
   as unreadable (logged with the reason) and the base falls back to OBND. Mesh failures are
   counted per kind (not found, NiflySharp exception type, ...) in the final bounds summary. Running
   out of memory while parsing a mesh stops the run instead of counting as a mesh failure, so
   results never depend on memory pressure.
4. **Replaced objects.** The target plugin often *replaces* an existing object (e.g. a tree) with
   its own new one at the same spot; such replaced other-mod objects must not trigger proximity
   removals. Two exclusions are applied to the "other objects" set before the too-close test:
   - *Overridden by the target plugin* (always on): any placed record present in the target
     plugin's own cell tree that originates from another plugin (i.e. the target plugin overrides
     it) is never an "other object", no matter which plugin ends up winning that record.
   - *Same-position lookalike* (*Ignore replaced objects*): an other object is excluded from
     proximity checks entirely when some target object in the same space has its position within
     *Replacement position tolerance* of it (`Vector3.Distance` between the two reference
     positions) **and** a similar size: each object's scaled local bounds dimensions
     (`(max - min) * scale`) are sorted largest-first, and the three matching pairs must each have
     a min/max ratio of at least *Replacement size similarity*. If either object has no bounds
     (zero-size box - unresolved base, missing OBND/mesh), or the other object is invisible (see
     below), it is never treated as a match. Matches are found in parallel
     and then applied in target scan order, so each excluded object is attributed to its first
     matching target object; the verbose log (sorted by the excluded object) is the same on every
     run.
5. **Too-close test.** For each target object:
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
   - *Invisible other objects never count.* When a query first turns up an other object, it is
     first checked for map marker data on the placed reference itself (`XMRK`, e.g. a fast-travel
     marker); if so it is invisible ("map marker reference"), regardless of its base. Otherwise
     its base is classified once (cached per base): a `Static`, `Furniture`, `Activator` or `Door`
     base whose major record flags carry the engine's `IsMarker` bit (these are the only base
     types that define that bit with this meaning) is invisible ("marker base (IsMarker flag)"),
     checked before any mesh read; lights without a mesh, sound markers, acoustic spaces, texture
     sets (decals) and idle markers are invisible by record type; any base whose mesh parses but
     has no visible render geometry (e.g. heading and other marker meshes that only hold
     `EditorMarker` shapes) is invisible; so is a base with no mesh and zero-size bounds, and a
     primitive box reference (trigger/activator volume) whose base has no readable visible mesh
     (with *Measure size from meshes (NIF)* off: whose base names no model at all). NPCs always
     count. Missing or unreadable meshes do not make a base invisible. The marker-mesh check needs
     *Measure size from meshes (NIF)*; the map marker reference and `IsMarker` base checks do not.
     The number of ignored objects is logged (per reason with verbose logging).
6. **Touching objects** (if *Remove touching objects* is on and meshes are used). Only target
   plugin objects are considered, and only within the same interior cell or worldspace.
   - *Broad phase*: in every space that contains a too-close removal, each target's oriented
     (rotated, scaled) bounding box, grown by the touch tolerance, is tested against nearby
     targets' boxes (spatial hash of world AABBs, then an exact oriented-box separating-axis
     test), once per pair. Boxes only select candidate pairs; they never decide that two objects
     touch. Too-close targets kept as referenced never take part.
   - *Which pairs are tested*: only a removed object passes a removal on, so a pair can matter
     only if one end is a seed or a not-kept target that is connected to a seed through a chain of
     candidate pairs between such objects. These pairs are found with a union-find over the
     candidate graph (no mesh work); all other candidate pairs are skipped.
   - *Narrow phase*: the actual render triangles (same shapes and node transforms as the bounds).
     Each unique mesh gets a bounding volume hierarchy over its triangles. For a pair, the other
     mesh's bounds are placed into the walked mesh's frame (position, rotation, scale) and grown
     by the tolerance; only the walked mesh's triangles inside this **overlap region** are
     visited (the mesh with fewer triangles is walked). Each is placed into the other mesh's frame
     and compared with that mesh's triangles near it by an **exact triangle-to-triangle distance
     test** (vertex-to-face and edge-to-edge distances plus edge-through-face intersection).
   - *Accuracy*: there is no sampling, so the result is exact up to floating-point rounding: two
     objects touch if and only if some triangle of each comes within the tolerance of the other
     (no false positives beyond the tolerance, and every gap up to the tolerance is found,
     including intersecting surfaces).
   - *Scheduling and memory*: all tested pairs are known before the narrow phase, so the mesh
     cache is told how often each mesh is used. A mesh's triangles are read and indexed on its
     first use and dropped right after its last pair, so each mesh is built once and only meshes
     with pending pairs stay resident. Pairs are processed in parallel, roughly in scan order
     (cell by cell), which keeps that working set small. As a fallback, if the resident meshes
     exceed about 1 GB, least recently used ones are dropped and rebuilt if needed again. A mesh
     over 2,000,000 triangles is not indexed and never touches anything. At most four large
     meshes are indexed at the same time. The log reports meshes built, rebuilt and evicted,
     triangles indexed, peak resident meshes and size, pairs tested and triangle pairs tested.
   - Objects without mesh triangles (OBND fallback, unreadable mesh, NPCs) never touch anything;
     such pairs are counted in the log.
   - *Clusters*: every target object connected to a too-close removal through a chain of
     touching target objects is removed as well, however large the group (connected components
     of the "touches" graph, no size cap). The components are explored breadth first from each
     too-close removal in removal order; each reached object is attributed to the first object of
     the previous level (in index order) that touches it, so the log is the same on every run.
     Objects kept by *Keep referenced objects* stay and do not pass the removal on. The log
     reports components, the largest component and pair counts.
7. **Touch diagnostics** (optional, off by default). When *Touch diagnostics file* is set, two CSV
   files are written after touch computation, single-threaded, so they never slow down or change
   the normal run: `<path>.edges.csv` and `<path>.components.csv`. They let a touching chain be
   judged from the log alone, without opening xEdit. Both are UTF-8, comma-separated, invariant
   culture, RFC 4180-style quoted when a field holds a comma, quote or newline, and written in a
   fixed order (by component, then by FormKey) so they are the same on every run.
   - **`.edges.csv`**: every touching edge found among target objects in the explored components,
     including seed-to-seed touches (which the console log does not otherwise report). Columns:
     `componentId`, `fromFormKey`, `toFormKey`, `fromIsSeed`, `toIsSeed`,
     `measuredMinSurfaceDistance`, `tolerance`, `centerToCenterDistance`, then, for `from_` and
     `to_` separately: `editorId`, `base` (base object FormKey and EditorID), `modelPath`,
     `spaceFormKey`, `posX/Y/Z`, `rotXDeg/YDeg/ZDeg`, `scale`, `halfExtentX/Y/Z` (half the scaled
     local bounds size, for a quick ratio against the measured distance). `measuredMinSurfaceDistance`
     is not merely the first triangle pair found under tolerance: the diagnostics pass re-walks the
     same tolerance-grown overlap region as the real touch test but does not stop early, so it
     reports the true minimum surface distance between the two meshes' triangles in that region (a
     value that is always ≤ tolerance, since the pair is already known to touch). Rotation is shown
     in degrees for readability; the engine's own convention (see below) still applies to it.
   - **`.components.csv`**: one row per explored component. Columns: `componentId`, `size` (every
     member: seeds, touch-removed and kept-as-referenced objects alike), `seedCount`,
     `removedByTouchCount`, `keptAsReferencedCount`, `spaces` (distinct cell/worldspace names,
     truncated to 10), `worldAabbMinX/Y/Z`, `worldAabbMaxX/Y/Z` (world-space bounds of every
     member), `topBaseEditorIds` (the 5 most common base objects, as `EditorID:count`),
     `longestChainLength` and `longestChainFormKeys` (the longest seed-to-leaf path through the
     touching graph, by edge count, and that path's FormKeys in order), and `seedReasons` (each
     seed's too-close conflicting object, as `FormKey (Plugin)`).
   - Mesh triangles are re-read for diagnostics edges (cached per mesh path for the duration of the
     write, but not shared with the main run's mesh cache), so a diagnostics file with many edges
     takes some extra time; this only happens when the setting is non-empty.
8. **Rotation convention.** Placement rotations are radians. The engine rotates clockwise
   (left-handed) about each axis, so the world matrix is `R = Rx(-x) * Ry(-y) * Rz(-z)` using
   standard right-handed matrices applied to column vectors. This lives in one function,
   `Geometry.RotationFromEuler`, and still needs to be verified in-game.

## Performance

- The too-close tests (one per target object) and the touch broad/narrow phases run in
  parallel on all CPU cores; the thread count is logged. Narrow-phase results are stored per
  pair and the clusters are built from them single-threaded, so results do not depend on thread
  scheduling. Writing the overrides into the patch
  is single-threaded and happens after all computation.
- Target base objects are measured first in a parallel warm-up. Each mesh is read and parsed
  once for its bounds; the triangles of a mesh are read again only if it takes part in a touch
  candidate pair.
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

Each object to remove is copied into the patch as an override, and only these things change:

- the **Initially Disabled** flag is set;
- the Z position is set to **-30000** (X/Y unchanged);
- if the object has an **Enable Parent** (which would otherwise re-enable it while the parent is
  enabled), it is replaced by the player reference with *Set Enable State to Opposite of Parent*,
  as xEdit's "Undelete and Disable References" does. The log counts these.

Nothing else on the record changes. No Enable Parent is added to objects that had none, and no
records are deleted, so references from scripts, quests and other records still resolve.

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
  `NiTriStrips` shapes are converted to triangles (read from NiflySharp's strip data by
  reflection; if a NiflySharp update removes those fields, a warning is logged and such shapes
  count as points only; a shape whose strip lengths do not match its points also counts as points
  and is logged with verbose logging).
- References are only checked through form links of other placed objects (Enable Parent, Linked
  References, Activate Parents, door teleports, script properties on placed objects and any other
  reference field), and only in plugins that can link to the target plugin's objects (the target
  itself, plugins with it as a master, and the patch). Quest aliases, packages, scripts on
  non-placed records and other non-placed dependencies are **not** checked. Review the verbose
  log if the target mod relies on scripted objects.
- The archive list comes from the game INI, then, for each plugin in load order, the Data-folder
  archives Mutagen considers applicable to it: `<Plugin>.bsa`, then `<Plugin> - <Suffix>.bsa` by
  name, where the plugin name is everything before the last ` - `. Archives loaded in other ways
  are not searched.
- Only the target plugin's own, unmodified objects are checked. Objects that a later plugin
  overrides are skipped.

## Building

```
dotnet build AddedObjectRemover.sln
```

Requires the .NET 10 SDK. Add the repository or the built project to a Synthesis profile as usual.

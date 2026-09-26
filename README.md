# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods, which is a common source of clipping and duplicated clutter when several
mods edit the same area. The output plugin is `AddedObjectRemover.esp`.

## Settings

The settings are grouped into four sections. **Upgrading from an earlier version:** the settings
were regrouped and renamed, so previously saved settings are reset to their defaults once;
re-enter the target plugin and check the other values after updating.

**What to check**

| Setting | Default | Meaning |
| --- | --- | --- |
| Target plugin | *(empty)* | File name of the plugin whose added objects are checked, e.g. `SomeMod.esp`. Case-insensitive. If it is empty or not in the load order, the patcher logs this and makes no changes. |
| Size multiplier | `0.5` | How far a target object reaches beyond its own bounds, as a fraction of its size on each axis (see *Too-close test*). Must be 0 or more. |

**What to ignore**

| Setting | Default | Meaning |
| --- | --- | --- |
| Excluded plugins | *(empty)* | Plugins whose objects never count as a conflict. |
| Ignore the target's masters | `true` | Masters of the target plugin are ignored as well. |

**Follow-up removal** (what happens to target objects touching a removed one)

| Setting | Default | Meaning |
| --- | --- | --- |
| Follow-up removal mode | `AnyTouch` | `Off`: only too-close objects are removed. `AnyTouch`: every target object connected to a removed one through touching target objects is removed too (see *AnyTouch*). `Anchoring`: a touching target object is removed only when most of its support is gone (see *Anchoring*). |
| Touch distance | `8` | Largest gap, in game units, between two surfaces for them to count as touching. Used by `AnyTouch` and `Anchoring`. Must be 0 or more. |
| Anchoring threshold | `50` | `Anchoring` only: percentage (1-99) of an object's support that must come from removed objects for it to be removed. |

**Diagnostics**

| Setting | Default | Meaning |
| --- | --- | --- |
| Detailed log | `false` | Log every removed object (FormKey, EditorID, base, cell/worldspace, the conflicting object and its plugin), replaced and overridden objects, invisible objects per reason, per-space counts and unreadable meshes. |
| Diagnostics folder | *(empty)* | Folder to write diagnostics CSV files to (see *Diagnostics files*). Empty writes nothing. Writing never changes the results but adds run time; a write error is only a warning. Relative paths resolve against the patcher's working directory; an absolute path is recommended. |

Values out of range are replaced (with a warning in the log) by the nearest valid value, or by 0 /
the default when not a number.

Fixed behaviour (no longer settings): base objects are always measured from their NIF mesh, with
the Object Bounds (OBND) fallback; referenced objects are always kept (see *Kept objects*);
replaced objects are always ignored, with a position tolerance of 16 units and a size similarity of
0.75 (see *Replaced objects*).

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
   - *Kept objects*: target objects that other placed objects link to (Enable Parent, Linked
     Reference, Activate Parent, door teleport destination, script properties or any other
     reference field), and teleport doors, are never removed. Kept objects are listed in the log.
   - *Terrain* (`Anchoring` only): the winning LAND record of every exterior cell in a worldspace
     that holds target objects.
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
   ignoring the hidden flag. A mesh that still yields nothing is logged (*Detailed log*) with per-reason
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
   - *Same-position lookalike* (always on): an other object is excluded from the too-close test
     when some target object in the same space has its position within 16 units of it
     (`Vector3.Distance` between the two reference positions) **and** a similar size: each
     object's scaled local bounds dimensions (`(max - min) * scale`) are sorted largest-first, and
     the three matching pairs must each have a min/max ratio of at least 0.75. If either object has no bounds
     (zero-size box - unresolved base, missing OBND/mesh), or the other object is invisible (see
     below), it is never treated as a match. Matches are found in parallel
     and then applied in target scan order, so each excluded object is attributed to its first
     matching target object; the detailed log (sorted by the excluded object) is the same on every
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
     primitive box reference (trigger/activator volume) whose base has no readable visible mesh.
     NPCs always count. Missing or unreadable meshes do not make a base invisible. The number of
     ignored objects is logged (per reason with *Detailed log*).
6. **AnyTouch** (*Follow-up removal mode* `AnyTouch`). Only target plugin objects are considered,
   and only within the same interior cell or worldspace.
   - *Search by levels*: the touching groups are explored breadth first from the too-close
     removals. Each level takes the objects just removed (at first the too-close removals) and
     tests only the pairs between them and objects not reached yet, in parallel; the results are
     then applied in a fixed order. So only pairs next to an object that is actually removed are
     ever tested, however many objects stand close together elsewhere.
   - *Broad phase*: in every space that contains a too-close removal, each target's oriented
     (rotated, scaled) bounding box, grown by the touch distance, is tested against nearby
     targets' boxes (spatial hash of world AABBs, then an exact oriented-box separating-axis
     test). Boxes only select candidate pairs; they never decide that two objects touch.
     Too-close targets kept as referenced and objects without a mesh never take part.
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
     including intersecting surfaces). An edge that runs almost parallel to the other triangle's
     plane (within about 0.06°) is not trusted to the edge-through-face test, whose result would be
     rounding noise there; such an edge that does pass through the triangle stays within 0.1% of
     its length of the triangle, and the vertex and edge distance tests find it.
   - *Memory*: a mesh's triangles are read and indexed on its first use and kept for later levels,
     so each mesh is normally built once. If the resident meshes exceed about 1 GB, the least
     recently used meshes not in use by a running test are dropped, and rebuilt if needed again.
     A mesh over 2,000,000 triangles is not indexed and never touches anything. At most four
     large meshes are indexed at the same time. The log reports meshes built, rebuilt and
     evicted, triangles indexed, peak resident meshes and size, levels, pairs tested and triangle
     pairs tested.
   - Objects without mesh triangles (OBND fallback, unreadable mesh, NPCs) never touch anything
     and never pass a removal on; pairs whose mesh turns out to have no usable triangles are
     counted in the log.
   - *Clusters*: every target object connected to a too-close removal through a chain of
     touching target objects is removed as well, however large the group (connected components
     of the "touches" graph, no size cap). The components are explored from each too-close
     removal in removal order; each reached object is attributed to the first object of the
     previous level (in the order the previous level was reached) that touches it, so the log is
     the same on every run. Kept objects stay and do not pass the removal on. The log reports
     components, the largest component and pair counts.
7. **Anchoring** (*Follow-up removal mode* `Anchoring`). Removes only the touching target objects
   that lose most of what holds them in place.
   - *Candidates*: target objects with mesh triangles, not removed and not kept, that touch a
     removed target object (same broad and narrow phase as *AnyTouch*, with the touch distance).
     Kept objects that touch a removed one are logged once and stay.
   - *Supporters* of a candidate: every target object with a mesh whose oriented box comes within
     the touch distance of the candidate's (removed or not, kept objects included), every visible
     other-mod object with a mesh doing the same (same visibility rules as the too-close test;
     replaced objects count, since they are still in the game), and in exterior cells the terrain.
   - *Contact points*: the candidate's surface is sampled in its local frame, about 4096 points
     per mesh spread by triangle area (at most 16 per triangle, fixed pattern, so the same on
     every run). A sample is a contact point of a mesh supporter when it lies within the touch
     distance of one of its triangles, or is embedded in it: a line through the sample along the
     supporter's local Z axis crosses the supporter's mesh an odd number of times both above and
     below it (exact for closed meshes; open meshes, such as rocks without a bottom, only count by
     their surface). A sample is a contact point of the terrain when it lies at or below the
     terrain height plus the touch distance.
   - *Terrain height*: from the winning LAND record's vertex height map (VHGT): 33x33 vertices
     128 units apart per 4096-unit cell; each byte is a height delta (the first of a row relative
     to the first vertex of the previous row, starting from the record's offset, the others to the
     previous vertex in the row), and the running value times 8 is the height in game units.
     Between vertices the height is interpolated bilinearly (the game splits each quad into two
     triangles, so this can differ slightly on uneven ground). Cells without a LAND record have no
     terrain; interiors never do.
   - *Weights*: each contact point is weighted by its closeness to the candidate's mesh origin,
     measured in the mesh's local frame (not in the world), because most objects are placed with
     the origin where they rest:
     `weight = 1 / (1 + (d / L)^2)`, where `d` is the point's distance from the origin and
     `L = 0.25 * the diagonal of the mesh's local bounds` (a point at distance `L` has half
     weight). A contact point touching several supporters splits its weight equally among them.
   - *Decision*: each supporter's share is the weight it holds divided by the total weight of all
     contact points. The candidate is removed when the removed target objects together hold at
     least the *Anchoring threshold*. A candidate without any contact point is kept (counted in
     the log).
   - *Iterations*: objects removed in one iteration make every object touching them a candidate in
     the next (objects kept earlier are re-evaluated with the new removals), until nothing more is
     removed. All candidates of one iteration are judged against the removals of earlier
     iterations only, so the result does not depend on their order. Contact points of a candidate
     are found once, in parallel, and reused.
   - The log reports removals, iterations, candidates and evaluations, candidates without contact
     points, pair and mesh counts and timings. With *Detailed log*, each removal names the share of
     support removed and the removed object holding most of it.
8. **Diagnostics files** (optional, off by default). When *Diagnostics folder* is set, these CSV
   files are written into it (existing files are replaced). They never change the results, but
   finding and measuring every edge adds run time, and an error writing them (e.g. a folder that
   cannot be created) is only logged as a warning. They let decisions be judged from the output
   alone, without opening xEdit. All are UTF-8, comma-separated, invariant culture, RFC 4180-style
   quoted when a field holds a comma, quote or newline, and written in a fixed order so they are
   the same on every run.
   - **`mesh-origins.csv`** (always): one row per mesh used by target objects, by model path.
     Columns: `modelPath`, `baseEditorIds` (the target bases using it), `referenceCount`,
     `localMinX/Y/Z`, `localMaxX/Y/Z` (the mesh's local bounds), `originFractionX/Y/Z` (where the
     origin sits inside the bounds per axis: 0 at the minimum, 1 at the maximum, `NaN` on an axis
     without size) and `classification`: `origin near bottom` (Z fraction at most 0.1),
     `origin near centre` (Z fraction 0.35-0.65) or `other`. The log prints the count per
     classification. It shows how well *Anchoring*'s assumption (origin where the object rests)
     holds for the target plugin's meshes.
   - **`anchoring.csv`** (`Anchoring` only): one row per candidate evaluation, by iteration and
     FormKey. Columns: `iteration`, `formKey`, `editorId`, `base`, `modelPath`, `space`,
     `contactPoints`, `totalWeight`, `removedTargetShare`, `keptTargetShare`, `otherPluginShare`,
     `terrainShare`, `threshold`, `decision` (`removed`, `kept` or `kept (no contact points)`) and
     `topSupporters` (the 5 largest, as `FormKey Category share`, or `terrain Terrain share`).
   - **`edges.csv`** (`AnyTouch` only): every touching pair of target objects that belong to the same explored
     component, including seed-to-seed touches (which the console log does not otherwise report).
     Pairs inside a component that the search itself never needed are tested for this file.
     Columns: `componentId`, `firstFormKey`, `secondFormKey`, `firstIsSeed`, `secondIsSeed`,
     `measuredMinSurfaceDistance`, `tolerance`, `centerToCenterDistance`, then, for `first_` and
     `second_` separately (an edge has no direction): `editorId`, `base` (base object FormKey and EditorID), `modelPath`,
     `spaceFormKey`, `posX/Y/Z`, `rotXDeg/YDeg/ZDeg`, `scale`, `halfExtentX/Y/Z` (half the scaled
     local bounds size, for a quick ratio against the measured distance). `measuredMinSurfaceDistance`
     is not merely the first triangle pair found under tolerance: the diagnostics pass re-walks the
     same tolerance-grown overlap region as the real touch test but does not stop early, so it
     reports the true minimum surface distance between the two meshes' triangles in that region (a
     value that is always ≤ tolerance, since the pair is already known to touch). Rotation is shown
     in degrees for readability; the engine's own convention (see below) still applies to it.
   - **`components.csv`** (`AnyTouch` only): one row per explored component. Columns: `componentId`, `size` (every
     member: seeds, touch-removed and kept-as-referenced objects alike), `seedCount`,
     `removedByTouchCount`, `keptAsReferencedCount`, `spaces` (distinct cell/worldspace names,
     truncated to 10), `worldAabbMinX/Y/Z`, `worldAabbMaxX/Y/Z` (world-space bounds of every
     member), `topBaseEditorIds` (the 5 most common base objects, as `EditorID:count`),
     `deepestChainLength` and `deepestChainFormKeys` (the path by which the search reached the
     member farthest from the component's first seed: its edge count and its FormKeys in order,
     a shortest touching path to that member), and `seedReasons` (each seed's too-close
     conflicting object, as `FormKey (Plugin)`).
   - The edges use the same mesh cache as the search, so the memory limit above applies.
9. **Rotation convention.** Placement rotations are radians. The engine rotates clockwise
   (left-handed) about each axis, so the world matrix is `R = Rx(-x) * Ry(-y) * Rz(-z)` using
   standard right-handed matrices applied to column vectors. This lives in one function,
   `Geometry.RotationFromEuler`, and still needs to be verified in-game.

## Performance

- The too-close tests (one per target object), the touch broad/narrow phases and the anchoring
  contact points run in parallel on all CPU cores; the thread count is logged. Narrow-phase results are stored per
  pair and each level is applied single-threaded, so results do not depend on thread
  scheduling. Writing the overrides into the patch is single-threaded and happens after all
  computation.
- Target base objects are measured first in a parallel warm-up. Each mesh is read and parsed
  once for its bounds; the triangles of a mesh are read again only if it takes part in a tested
  touch pair.
- Other objects' bounds centers are computed lazily, once each, and only for objects a query
  turns up.
- NIFs are parsed in parallel. NiflySharp's only static state touched while loading (a
  block-type lookup table) is built once under a lock before parallel parsing starts. BSA reads
  open their own file stream per read and need no lock.
- Detailed-log output from parallel phases (mesh messages) is collected and printed afterwards in a
  fixed order, so logs are deterministic.
- The log shows the time of each phase: scan, indexing, bounds warm-up, replacement matching,
  too-close search, touch setup / broad phase / narrow phase (and diagnostics edges, when
  requested) or anchoring setup / touch search / contact points, and writing.

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
  and is logged with *Detailed log*).
- References are only checked through form links of other placed objects (Enable Parent, Linked
  References, Activate Parents, door teleports, script properties on placed objects and any other
  reference field), and only in plugins that can link to the target plugin's objects (the target
  itself, plugins with it as a master, and the patch). Quest aliases, packages, scripts on
  non-placed records and other non-placed dependencies are **not** checked. Review the detailed
  log if the target mod relies on scripted objects.
- The archive list comes from the game INI, then, for each plugin in load order, the Data-folder
  archives Mutagen considers applicable to it: `<Plugin>.bsa`, then `<Plugin> - <Suffix>.bsa` by
  name, where the plugin name is everything before the last ` - `. Archives loaded in other ways
  are not searched.
- Only the target plugin's own, unmodified objects are checked. Objects that a later plugin
  overrides are skipped.
- *Anchoring* samples surfaces (about 4096 points per mesh), so a very small contact area can be
  missed or under-weighted; its origin-based weights assume the origin marks where an object
  rests (check `mesh-origins.csv`); terrain is interpolated bilinearly rather than per game
  triangle; only closed meshes can enclose an embedded point.

## Building

```
dotnet build AddedObjectRemover.sln
```

Requires the .NET 10 SDK. Add the repository or the built project to a Synthesis profile as usual.

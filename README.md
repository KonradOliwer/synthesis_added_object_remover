# Added Object Remover

A [Synthesis](https://github.com/Mutagen-Modding/Synthesis) patcher for Skyrim Special Edition.

## What it does

It removes objects added by one plugin (the *target plugin*) when they sit too close to objects
added by other mods — a common source of clipping and duplicated clutter when several mods edit
the same area. Optionally, it also removes target objects that were resting on or touching an
already-removed one, so a chain of clutter does not get left floating or half-buried. Removal
never deletes anything: the object is disabled and moved far below the world, so scripts, quests
and other records that still reference it keep working. The output plugin is
`AddedObjectRemover.esp`.

## Settings

The settings are grouped into four sections, matching what Synthesis shows.

### What to check

| Setting | Default | Meaning |
| --- | --- | --- |
| Target plugin | *(empty)* | File name of the plugin whose added objects are checked, e.g. `SomeMod.esp`. If it is empty or not in the load order, no changes are made. |
| Size multiplier | `0.5` | How far past its own edges a target object reaches, as a fraction of its size (0.5 = half its size further out on every side). Another mod's object whose centre falls inside that range makes the target object too close. Must be 0 or more. |

### What to ignore

| Setting | Default | Meaning |
| --- | --- | --- |
| Excluded plugins | *(empty)* | Plugins whose objects never count as a conflict. |
| Ignore the target's masters | `true` | Also ignore objects from the plugins the target plugin was built on. |

### Follow-up removal

| Setting | Default | Meaning |
| --- | --- | --- |
| Follow-up removal mode | `AnyTouch` | What happens to target objects touching a removed one. See below. |
| Touch distance | `8` | Largest gap, in game units, between two surfaces for them to count as touching. Used by `AnyTouch` and `Anchoring`. Must be 0 or more. |
| Anchoring threshold | `50` | `Anchoring` only: percentage (1-99) of an object's support that must come from removed objects for it to be removed. |

Follow-up removal mode:
- **Off** — only the too-close objects themselves are removed.
- **Any touch** — every target object connected to a removed one, through a chain of touching target objects, is removed too.
- **Anchoring** — a touching target object is only removed once most of what was holding it up is gone.

### Diagnostics

| Setting | Default | Meaning |
| --- | --- | --- |
| Detailed log | `false` | Log every removed and kept object with its reason, per-space counts and unreadable meshes. |
| Diagnostics folder | *(empty)* | Folder to write diagnostics spreadsheets (CSV files) to. Leave empty to write nothing. An absolute path is recommended; writing these files never changes the result. |

Values out of range are replaced (with a warning in the log) by the nearest valid value, or by the
default when not a number.

## What is never counted as a conflict

- The base game and its official updates (`Skyrim.esm`, `Update.esm`, `Dawnguard.esm`,
  `HearthFires.esm`, `Dragonborn.esm`). Creation Club plugins are **not** excluded automatically;
  add them to *Excluded plugins* if needed.
- Invisible objects, such as map markers, fast-travel markers, light-only markers and other
  editor-only markers.
- Objects the target plugin overrides or replaces (e.g. a tree the target plugin swaps for its own
  version at the same spot).
- The patcher's own output plugin, so objects placed by an earlier Added Object Remover run in the
  same Synthesis pipeline are never treated as a conflict either.

### Safety

- Objects that another placed object depends on (a linked door, activator, script target, etc.)
  are never removed.

## Diagnostics

Written only when a *Diagnostics folder* is set:

- **`mesh-origins.csv`** — one row per mesh used by target objects; shows whether the mesh's
  origin sits near where the object visibly rests, which is what *Anchoring* assumes.
- **`anchoring.csv`** (*Anchoring* mode only) — one row per object *Anchoring* evaluated, with the
  share of support held by removed objects, kept objects, other plugins and terrain, and the
  final decision.
- **`edges.csv`** (*Any touch* mode only) — every touching pair of target objects found while
  exploring a group of removed/connected objects.
- **`components.csv`** (*Any touch* mode only) — one row per such group, with its size and the
  objects that started it.

## Known limitations

- Objects are compared by an approximate bounding box, so two large objects that only overlap at
  their edges may not be detected as too close.
- Object size comes from the mesh (or its Object Bounds as a fallback), which can differ slightly
  from what you see in-game.
- Touching is based on visible mesh surfaces, not game collision, so an object fully inside
  another one without their surfaces actually meeting does not count as touching.
- Dependencies are only found through direct references on other placed objects (linked doors,
  enable parents, script properties, etc.); quest-only or purely scripted dependencies are not
  checked. Review the detailed log if the target mod relies on scripted objects.
- Only the target plugin's own, unmodified objects are checked; objects a later plugin overrides
  are skipped.

## Upgrading from an earlier version

Settings were regrouped and renamed, so settings saved by an older version are reset to their
defaults once. Re-enter the target plugin and check the other values after updating.

## Building

```
dotnet build AddedObjectRemover.sln
```

Requires the .NET 10 SDK. Add the repository or the built project to a Synthesis profile as usual.

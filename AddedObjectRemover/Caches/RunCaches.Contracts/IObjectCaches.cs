using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>Cache C9: the objects added by other mods, by space; not narrowed by visibility or replacement. Thread-safe.</summary>
public interface IOtherModObjectsBySpace
{
    OtherObject Get(OtherId id);

    /// <summary>The objects of the space, in <see cref="OtherId"/> order.</summary>
    IReadOnlyList<OtherObject> ObjectsIn(RecordKey space);

    /// <summary>The index of the space; its slots follow <see cref="ObjectsIn"/>.</summary>
    IOtherObjectIndex IndexOf(RecordKey space);

    /// <summary>The index of the object's space and its slot there.</summary>
    (IOtherObjectIndex Index, int Slot) Locate(OtherId id);
}

/// <summary>Cache C10: the placed NPCs of each space with their possible bodies, replaced ones included. Thread-safe.</summary>
public interface IPlacedNpcsBySpace
{
    IPlacedNpcIndex IndexOf(RecordKey space);
}

/// <summary>Cache C11: the objects of any plugin by space, other mods' objects and support-only objects together. Thread-safe.</summary>
public interface IObjectsOfAnyPluginBySpace
{
    OtherObject Get(OtherId id);

    IOtherObjectIndex IndexOf(RecordKey space);
}

/// <summary>Cache C12: the visible target objects, removed or not, as oriented boxes by space. Thread-safe.</summary>
public interface IVisibleTargetObjects
{
    /// <summary>The spaces holding a visible target object, in the order their first one appears among the targets.</summary>
    IReadOnlyList<RecordKey> Spaces { get; }

    /// <summary>Replaces <paramref name="into"/> with the visible targets whose box lies within <paramref name="radius"/> of <paramref name="point"/>, ascending.</summary>
    void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into);

    /// <summary>Whether the mesh of some included visible target surrounds <paramref name="point"/>.</summary>
    bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch);

    /// <summary>The boxes of the included visible targets that may come within <paramref name="radius"/> of <paramref name="point"/>, and possibly a few farther ones.</summary>
    IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include);
}

/// <summary>A visible target object near a point: the directions it counts in and its ground area.</summary>
/// <param name="Sectors">The one direction it lies in, or all of them when its box contains the point.</param>
public readonly record struct VisibleTargetNear(TargetId Target, IReadOnlyList<DirectionSector> Sectors, float GroundArea);

/// <summary>The caches built from the placed objects, after they are collected.</summary>
public interface IObjectCaches
{
    /// <summary>C9.</summary>
    IOtherModObjectsBySpace OtherModObjects { get; }

    /// <summary>C10.</summary>
    IPlacedNpcsBySpace PlacedNpcs { get; }

    /// <summary>C11: built on the first ask, and only when the run collected the support-only objects.</summary>
    IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin { get; }

    /// <summary>C12.</summary>
    IVisibleTargetObjects VisibleTargets { get; }
}

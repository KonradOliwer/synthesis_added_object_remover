using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>The objects added by other mods, by space; not narrowed by visibility or replacement. Thread-safe.</summary>
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

/// <summary>The placed NPCs of each space with their possible bodies, replaced ones included. Thread-safe.</summary>
public interface IPlacedNpcsBySpace
{
    IPlacedNpcIndex IndexOf(RecordKey space);
}

/// <summary>The objects of any plugin by space, other mods' objects and support-only objects together. Thread-safe.</summary>
public interface IObjectsOfAnyPluginBySpace
{
    OtherObject Get(OtherId id);

    IOtherObjectIndex IndexOf(RecordKey space);
}

/// <summary>The visible target objects, removed or not, as oriented boxes by space. Thread-safe.</summary>
public interface IVisibleTargetObjects
{
    /// <summary>The spaces holding a visible target object, in the order their first one appears among the targets.</summary>
    IReadOnlyList<RecordKey> Spaces { get; }

    /// <summary>Replaces <paramref name="into"/> with the visible targets whose box lies within <paramref name="radius"/> of <paramref name="point"/>, ascending.</summary>
    void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into);

    /// <summary>
    /// Replaces <paramref name="into"/> with the visible targets whose box centre lies within <paramref name="radius"/> of
    /// <paramref name="point"/> horizontally, at any height, ascending.
    /// </summary>
    void InColumn(RecordKey space, Vector2 point, float radius, List<VisibleTargetNear> into);

    /// <summary>Whether the mesh of some included visible target surrounds <paramref name="point"/>.</summary>
    bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch);

    /// <summary>The boxes of the included visible targets that may come within <paramref name="radius"/> of <paramref name="point"/>, and possibly a few farther ones.</summary>
    IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include);
}

/// <summary>A visible target object near a point, with its box.</summary>
public readonly record struct VisibleTargetNear(TargetId Target, OrientedBox Box);

/// <summary>The caches built from the placed objects, after they are collected.</summary>
public interface IObjectCaches
{
    IOtherModObjectsBySpace OtherModObjects { get; }

    IPlacedNpcsBySpace PlacedNpcs { get; }

    /// <summary>Built on the first ask.</summary>
    IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin { get; }

    IVisibleTargetObjects VisibleTargets { get; }

    /// <summary>A new index like <see cref="VisibleTargets"/> over <paramref name="targets"/>: the collected targets, some shrunk or turned. <see cref="VisibleTargets"/> stays as it is.</summary>
    IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets);

    /// <summary>
    /// The visible targets where the rounds measure them: a new index with each object kept shrunk or turned at its new placement
    /// and every other object, a restored one too, where the cleaned mod put it, or <see cref="VisibleTargets"/> when there is no
    /// fit. An object kept shrunk or turned and removed later stands where it stood, as the patch leaves it.
    /// </summary>
    IVisibleTargetObjects IndexVisibleTargetsAsTheRoundsMeasureThem(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions);

    /// <summary>
    /// The visible targets where they stand: a new index with each object kept shrunk or turned at its new placement and each
    /// restored object at its earlier placement, or <see cref="VisibleTargets"/> when none stands elsewhere. An object kept shrunk
    /// or turned and removed later stands where it stood, as the patch leaves it.
    /// </summary>
    IVisibleTargetObjects IndexVisibleTargetsAsTheyStand(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions);

    /// <summary>The visible objects of <see cref="ObjectsOfAnyPlugin"/>; asking builds it.</summary>
    IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin();

    /// <summary>
    /// The visible objects of <see cref="ObjectsOfAnyPlugin"/> with some moved or taken out, without changing it: each changed object where it stands now, or
    /// gone when it maps to null. Asking builds <see cref="ObjectsOfAnyPlugin"/>.
    /// </summary>
    IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed);
}

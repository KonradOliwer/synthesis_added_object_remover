using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>Kinds of visible placed objects that can be kept as a whole, by their base record type; every other visible object is judged like any other.</summary>
public enum VisibleObjectKind
{
    NpcsAndCreatures,
    Containers,
    Furniture,
    Activators,
}

public static class VisibleObjectKinds
{
    /// <summary>By the record type of the object's base, which a visible object always has; null for an object of no listed kind.</summary>
    public static VisibleObjectKind? Of(TargetObject target, IBaseFacts bases) => bases.Of(target.Base!.Value).Kind switch
    {
        BaseRecordKind.Npc => VisibleObjectKind.NpcsAndCreatures,
        BaseRecordKind.Container => VisibleObjectKind.Containers,
        BaseRecordKind.Furniture => VisibleObjectKind.Furniture,
        BaseRecordKind.Activator => VisibleObjectKind.Activators,
        _ => null,
    };
}

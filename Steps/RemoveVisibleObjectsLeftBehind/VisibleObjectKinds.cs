using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

internal static class VisibleObjectKinds
{
    private static readonly IReadOnlyDictionary<BaseRecordKind, VisibleObjectKind> ByRecordKind = new Dictionary<BaseRecordKind, VisibleObjectKind>
    {
        [BaseRecordKind.Npc] = VisibleObjectKind.NpcsAndCreatures,
        [BaseRecordKind.Container] = VisibleObjectKind.Containers,
        [BaseRecordKind.Furniture] = VisibleObjectKind.Furniture,
        [BaseRecordKind.Activator] = VisibleObjectKind.Activators,
    };

    /// <summary>By the record type of the object's base, which a visible object always has; null for an object of no listed kind.</summary>
    public static VisibleObjectKind? Of(TargetObject target, IBaseFacts bases) =>
        ByRecordKind.TryGetValue(bases.Of(target.Base!.Value).Kind, out var kind) ? kind : null;
}

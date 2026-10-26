using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

internal static class VisibleObjectKinds
{
    private static readonly IReadOnlyDictionary<BaseRecordKind, VisibleObjectKind> ByRecordKind = new Dictionary<BaseRecordKind, VisibleObjectKind>
    {
        [BaseRecordKind.Npc] = VisibleObjectKind.NpcsAndCreatures,
        [BaseRecordKind.Door] = VisibleObjectKind.Doors,
        [BaseRecordKind.Container] = VisibleObjectKind.Containers,
        [BaseRecordKind.Furniture] = VisibleObjectKind.Furniture,
        [BaseRecordKind.Activator] = VisibleObjectKind.Activators,
        [BaseRecordKind.Light] = VisibleObjectKind.LightsWithModel,
        [BaseRecordKind.TreeOrPlant] = VisibleObjectKind.TreesAndPlants,
        [BaseRecordKind.LooseItem] = VisibleObjectKind.LooseItems,
    };

    /// <summary>By the record type of the object's base; a visible object always has a base.</summary>
    public static VisibleObjectKind Of(TargetObject target, IBaseFacts bases) =>
        ByRecordKind.GetValueOrDefault(bases.Of(target.Base!.Value).Kind, VisibleObjectKind.OtherVisibleObjects);
}

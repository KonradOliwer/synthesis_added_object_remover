using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// The parts the write gives back to the version just below the cleaned mod, one per part: those of the targets a removal step or the
/// restore's follow-up puts back, and those of the restore where the cleaned mod was removed. A copy the restore adds is new content,
/// not a part given back, so it is none of them; nor is a navmesh, or a door link a navmesh group copies on its own. An object the
/// follow-up pulls down or removes holds no earlier value, so it is none of them either.
/// </summary>
internal static class RestoredPartsWritten
{
    /// <param name="targetsPutBack">Each target a removal step or the restore's follow-up puts back, with the version it goes back to.</param>
    /// <returns>By record key, then by part.</returns>
    public static ImmutableArray<RestoredPart> List(
        IEnumerable<(RecordKey Target, EarlierVersion Earlier)> targetsPutBack, RestoreWhereRemovedResult whereRemoved, PluginName cleanedMod) =>
    [
        .. targetsPutBack.SelectMany(target => RestoredPartFacts.ListPlaced(target.Target, target.Earlier, cleanedMod))
            .Concat(whereRemoved.Objects.Where(judged => judged.IsRestored)
                .SelectMany(judged => RestoredPartFacts.ListPlaced(judged.Change.Key, judged.Change.Earlier, cleanedMod)))
            .Concat(whereRemoved.Cells.Where(judged => judged.IsRestored).SelectMany(judged => RestoredPartFacts.ListCell(judged.Change, judged.Undone, cleanedMod)))
            .Concat(whereRemoved.Lands.Where(judged => judged.IsRestored)
                .SelectMany(judged => RestoredPartFacts.ListLand(judged.Change, judged.Heights, judged.Textures, cleanedMod)))
            .OrderBy(part => part.Record, RecordKeyOrder.Comparer),
    ];
}

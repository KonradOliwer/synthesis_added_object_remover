using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

public sealed partial class LogAndReports
{
    private TooCloseSection BuildTooCloseSection(TooCloseResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        var meshes = _caches.Triangles.GetStats();
        var bodies = _caches.Bodies.GetStats();
        return new TooCloseSection(
            world,
            _settings.TargetAsTyped,
            [.. found.Passes.Where(pass => pass.Outcome == StepOutcome.Performed).Select(pass => new TooClosePassReport(
                pass,
                CountRemovedBeforeThisRound(pass, decisions),
                RemovalList.CountRestored(decisions, pass.Hits.Select(hit => new TargetId(hit.TargetIndex)))))],
            WordInvisibleOtherObjects(found.LeftOut),
            [.. RemovalList.KeptIn(decisions, decisions.Rounds[^1])],
            meshes.Built,
            meshes.Triangles,
            bodies.BodiesBuilt,
            bodies.BasesResolved,
            bodies.ListsResolved,
            _caches.CountBodyMeshSets(),
            _caches.CountEffectOnlyMeshes(),
            elapsed);
    }

    /// <remarks>Ways of being invisible that share one wording are listed as one reason.</remarks>
    private static InvisibleOtherObjectCounts WordInvisibleOtherObjects(OtherObjectsLeftOut leftOut) =>
        new(
            KeyedGroups.Rank(
                KeyedGroups.IndicesByKey(leftOut.Invisible, entry => ObjectVisibilityText.Describe(entry.Visibility), StringComparer.Ordinal)
                    .Select(reason => KeyValuePair.Create(reason.Key, reason.Indices.Sum(index => leftOut.Invisible[index].Count))),
                StringComparer.Ordinal),
            leftOut.PlacedNpcs);

    private static int CountRemovedBeforeThisRound(TooClosePass found, IRemovalDecisions decisions)
    {
        var thisRound = decisions.Rounds[^1];
        return found.Hits
            .Select(hit => new TargetId(hit.TargetIndex))
            .Count(target => decisions.IsRemoved(target) && decisions.Of(target)!.Round != thisRound);
    }
}

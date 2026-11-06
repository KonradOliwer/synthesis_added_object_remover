using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <param name="Leaving">Each record as its object where it stands now, with where it stands once back.</param>
/// <param name="InAnotherShape">Those with no place once back that do not end removed, each in the shape it takes then, in the same order.</param>
internal sealed record OnceBack(List<OtherLeaving> Leaving, List<OtherObject> InAnotherShape);

/// <summary>
/// The placed records the cleaned mod changed as they would stand once their undone parts went back, which is the same in every pass
/// of the restore: the run's objects of any plugin by key, the records the game no longer shows, and the records that are no candidate.
/// </summary>
/// <param name="OthersByKey">The run's objects of any plugin, where they stood before the run took any away.</param>
/// <param name="HiddenByKey">
/// The records of other plugins the game no longer shows that stood before the cleaned mod's change, each as it would stand once back,
/// with ids after <paramref name="OthersByKey"/> in key order.
/// </param>
/// <param name="NoCandidateOthers">The records that are no candidate and stand in the run's world, from where they stood before the run took any away.</param>
/// <param name="NoCandidateTargets">The target objects that are no candidate, each with the version it would go back to; none when it ends removed.</param>
internal sealed record ChangesOnceBack(
    IReadOnlyDictionary<RecordKey, OtherObject> OthersByKey,
    IReadOnlyDictionary<RecordKey, OtherObject> HiddenByKey,
    OnceBack NoCandidateOthers,
    ImmutableArray<GoingBack> NoCandidateTargets)
{
    /// <param name="cleared">The placed records whose areas let them go back.</param>
    public static ChangesOnceBack Of(IReadOnlyList<RecordChangedByCleanedMod> cleared, CollectedObjects collected)
    {
        var othersByKey = collected.OtherModAndSupportOnlyObjects.ToDictionary(other => other.Key);
        var candidates = cleared.Select(change => change.Key).ToHashSet();
        List<RecordChangedByCleanedMod> noCandidates = [.. collected.ChangedRecords.Where(change => change.HasPartsToUndo && !candidates.Contains(change.Key))];
        return new ChangesOnceBack(
            othersByKey,
            FindHidden(collected, othersByKey),
            Of(
                noCandidates.Where(change => change.Target is null && othersByKey.ContainsKey(change.Key))
                    .Select(change => (change, othersByKey[change.Key], change.RemovedBeforeTheCleanedMod)),
                collected.Targets),
            [
                .. noCandidates.Where(change => change.Target is not null)
                    .Select(change => new GoingBack(change.Target!.Value, new GoingBackWhereRemoved(), change.RemovedBeforeTheCleanedMod ? null : change.Earlier)),
            ]);
    }

    /// <summary>
    /// Each record from where it stands now with the parts that go back taken from its earlier version: where it stands then, or no place
    /// when it ends removed or those parts give it another shape, and then also in that shape.
    /// </summary>
    /// <remarks>Only the shapes are compared, so the id the record is placed with names nothing.</remarks>
    public static OnceBack Of(IEnumerable<(RecordChangedByCleanedMod Change, OtherObject Now, bool EndsRemoved)> records, ImmutableArray<TargetObject> targets)
    {
        var onceBack = new OnceBack([], []);
        foreach (var (change, now, endsRemoved) in records)
        {
            if (endsRemoved)
            {
                onceBack.Leaving.Add(new OtherLeaving(now, To: null));
                continue;
            }
            var standing = AfterRemovalWorld.AsItIsNow(change, default, targets).PlacedAt(new EulerPlacement(now.Position, now.Rotation, now.Scale));
            var back = standing.RestoredAs(change.Earlier);
            var keepsItsShape = PlacedShape.Of(back.PlacedAt(standing.Placement)).IsSameAs(PlacedShape.Of(standing));
            onceBack.Leaving.Add(new OtherLeaving(now, keepsItsShape ? back.Transform : null));
            if (!keepsItsShape) onceBack.InAnotherShape.Add(InItsShapeBack(now, back));
        }
        return onceBack;
    }

    /// <summary>The object with its id, plugin and roles, in the shape and placement it takes once back.</summary>
    private static OtherObject InItsShapeBack(OtherObject now, TargetObject back) =>
        now with
        {
            Base = back.Base,
            Position = back.Transform.Position,
            Rotation = back.Rotation,
            Scale = back.Transform.Scale,
            IsPrimitive = back.IsPrimitive,
            HasMapMarker = back.HasMapMarker,
        };

    private static Dictionary<RecordKey, OtherObject> FindHidden(CollectedObjects collected, Dictionary<RecordKey, OtherObject> othersByKey) =>
        collected.ChangedRecords
            .Where(change => change.Target is null && change.PresentBeforeTheCleanedMod && !othersByKey.ContainsKey(change.Key))
            .Select(change => (Change: change, Back: AsItWouldStandOnceBack(change, collected.Targets)))
            .Where(entry => entry.Back.Base is not null)
            .OrderBy(entry => entry.Change.Key, RecordKeyOrder.Comparer)
            .Select((entry, index) => AsShownAgain(entry.Change, entry.Back, new OtherId(othersByKey.Count + index)))
            .ToDictionary(other => other.Key);

    /// <summary>
    /// A record the game no longer shows, from where its winning version places it, or at its earlier version's placement when it has
    /// none, with only its undone parts taken from its earlier version, so a later plugin's change stays; with its earlier
    /// base when its winning version has none.
    /// </summary>
    private static TargetObject AsItWouldStandOnceBack(RecordChangedByCleanedMod change, ImmutableArray<TargetObject> targets)
    {
        var back = AfterRemovalWorld.AsItIsNow(change, default, targets).RestoredAs(change.Earlier);
        return back.Base is null ? back with { Base = change.Earlier.Base } : back;
    }

    /// <summary>As an object of its winning plugin, so a pull-down or removal of it is written as for any placed record.</summary>
    private static OtherObject AsShownAgain(RecordChangedByCleanedMod change, TargetObject back, OtherId id) =>
        back.AsOtherObject(id, change.WinningMod) with { Roles = change.IsPersistent ? ReferenceRoles.Persistent : ReferenceRoles.None };
}

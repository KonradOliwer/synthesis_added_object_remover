using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>The also-remove rounds seeded by given rounds (see <see cref="RestingObjectsFinder"/>).</summary>
/// <param name="timeSinceStart">The run's clock, which times the rounds alone.</param>
/// <param name="reportFiles">Whether the report files are written, which decides whether their details are found.</param>
/// <param name="caches">
/// Its base facts, shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the
/// support rounds have seeds: the first ask builds the supporters' bounds index.
/// </param>
internal sealed class RestingObjectsRounds(
    AlsoRemoveSettings settings, Execution execution, Func<TimeSpan> timeSinceStart, StepOutcome reportFiles, IRunCaches caches) : IRestingObjectsRounds
{
    public RestingObjectsResult Find(ImmutableArray<TargetObject> targets, IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds) =>
        RestingObjectsFinder.Find(new RestingObjectsInput(targets, earlierRounds, seedRounds, settings, execution, timeSinceStart, reportFiles), caches);
}

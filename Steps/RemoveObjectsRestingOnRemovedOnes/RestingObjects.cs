using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove step: seeded with the removals of the rounds before it, its rounds remove what goes with them, as the
/// also-remove mode says, until a round removes nothing. It decides the rounds on its own copy of the decisions, then adds
/// them to the run's decisions.
/// </summary>
public static class RestingObjects
{
    /// <summary>How many points the support rule samples on each mesh's surface, which the surface sample cache spreads.</summary>
    public static int SurfaceSamplesPerMesh() => SurfaceContacts.SamplesPerMesh;

    /// <param name="Targets">Where they stood before any decision.</param>
    /// <param name="Decisions">The decisions with the rounds of the steps before, whose removals the rounds follow up; the step's rounds are added to them.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the rounds alone.</param>
    /// <param name="ReportFiles">Whether the report files are written, which decides whether their details are found.</param>
    /// <param name="Caches">
    /// Its base facts, shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the
    /// support rounds have seeds: the first ask builds the supporters' bounds index.
    /// </param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IRunDecisions Decisions,
        AlsoRemoveSettings Settings,
        Execution Execution,
        Func<TimeSpan> TimeSinceStart,
        StepOutcome ReportFiles,
        IRunCaches Caches);

    /// <remarks>Seeded with the removals of every round so far, measuring the target objects where those rounds measure them.</remarks>
    public static Decided<RestingObjectsResult> Find(Input input)
    {
        var earlierRounds = input.Decisions;
        var result = RestingObjectsFinder.Find(
            new RestingObjectsInput(
                earlierRounds.TargetsAsTheRoundsMeasureThem(input.Targets),
                earlierRounds,
                earlierRounds.Rounds,
                input.Settings,
                input.Execution,
                input.TimeSinceStart,
                input.ReportFiles),
            input.Caches);
        return new Decided<RestingObjectsResult>(result, earlierRounds.ApplyRounds(AlsoRemoveRounds.Opening, result.ProposedRounds));
    }

    /// <param name="Targets">Where they stood before any decision.</param>
    /// <param name="Decisions">The decisions the rounds start from; the rounds measure the target objects where these place them.</param>
    /// <param name="TimeSinceStart">The run's clock, which measures the rounds.</param>
    /// <param name="Caches">
    /// Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the support rounds
    /// have seeds: the first ask builds the supporters' bounds index.
    /// </param>
    public sealed record FollowUpInput(
        ImmutableArray<TargetObject> Targets,
        IRemovalDecisions Decisions,
        AlsoRemoveSettings Settings,
        Execution Execution,
        Func<TimeSpan> TimeSinceStart,
        IRunCaches Caches);

    /// <param name="Targets">Where they stand before any object is shrunk or turned.</param>
    /// <param name="Drops">Asks the drop test about the same targets where they stand.</param>
    /// <param name="Caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when there is an object to check.</param>
    public sealed record RestingRuleInput(
        ImmutableArray<TargetObject> Targets, AlsoRemoveSettings Settings, ITargetObjectsDropTest Drops, Execution Execution, IRunCaches Caches);

    /// <param name="Caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked each time the rounds are created.</param>
    public sealed record RoundsAfterInput(AlsoRemoveSettings Settings, Execution Execution, IRunCaches Caches);

    /// <summary>
    /// The also-remove rounds that follow up removals pass by pass (see <see cref="IAlsoRemoveFollowUp"/>); its result
    /// computes no report-file details.
    /// </summary>
    public static IAlsoRemoveFollowUp CreateFollowUp(FollowUpInput input) => new AlsoRemoveFollowUp(input);

    /// <summary>The objects other target objects rest on, before any removal (see <see cref="RestingRule"/>).</summary>
    public static IRestingRule CreateRestingRule(RestingRuleInput input) => new RestingRule(input);

    /// <summary>The also-remove rounds decided on a working copy of the decisions, spreading from removals made there (see <see cref="AlsoRemoveRoundsAfter"/>).</summary>
    public static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RoundsAfterInput input) => new AlsoRemoveRoundsAfter(input);

    /// <summary>Measures the contacts that support an object (see <see cref="SurfaceContacts"/>).</summary>
    /// <param name="caches">Its triangles, surface samples and terrain are read.</param>
    public static ISupportContacts CreateSupportContacts(ILoadOrderCaches caches) => new SurfaceContacts(caches.Terrain, caches.Triangles, caches.SurfaceSamples);
}

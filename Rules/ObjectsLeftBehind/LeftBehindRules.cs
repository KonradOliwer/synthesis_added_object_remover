using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The rules of objects left behind, each built for the run.</summary>
public static class LeftBehindRules
{
    /// <summary>The direction rule: candidates, surroundings and their verdict (see <see cref="SurroundingsJudging"/>).</summary>
    public static ISurroundingsJudging CreateSurroundingsJudging() => new SurroundingsJudging();

    /// <summary>The rules that join a round's decisions into verdicts and proposals (see <see cref="LeftBehindRoundJoin"/>).</summary>
    public static ILeftBehindRoundJoin CreateLeftBehindRoundJoin() => new LeftBehindRoundJoin();

    /// <summary>How far an invisible object acts (see <see cref="ReachRule"/>).</summary>
    public static IInvisibleObjectReach CreateInvisibleObjectReach(IBaseFacts bases) => new ReachRule(bases);

    /// <summary>The visible objects of the listed kinds (see <see cref="VisibleKindsAlwaysKept"/>).</summary>
    /// <param name="caches">The base facts and shapes of the objects are read.</param>
    /// <param name="neverRemove">The kinds the settings list, also while the visible left-behind method is switched off.</param>
    public static IKindsAlwaysKept CreateVisibleKindsAlwaysKept(IRunCaches caches, NeverRemovedVisibleKinds neverRemove) =>
        new VisibleKindsAlwaysKept(caches.Shapes, caches.Bases, neverRemove.Kinds);

    /// <summary>The invisible objects of the listed kinds (see <see cref="InvisibleKindsAlwaysKept"/>).</summary>
    /// <param name="shapes">The kinds of the objects are read.</param>
    /// <param name="neverRemove">The kinds the settings list, also while the invisible left-behind method is switched off.</param>
    public static IKindsAlwaysKept CreateInvisibleKindsAlwaysKept(IBaseObjectShapes shapes, NeverRemovedInvisibleKinds neverRemove) =>
        new InvisibleKindsAlwaysKept(shapes, neverRemove.Kinds);

    /// <summary>
    /// The neighbours measured around a shown target with the look-around distance (see <see cref="VisibleNeighbours"/>); none while
    /// the method is switched off.
    /// </summary>
    /// <param name="decisions">The decisions the neighbours stand in: each visible target where their rounds measure it.</param>
    /// <param name="caches">Index the visible target objects; their shapes are read.</param>
    /// <param name="settings">Null when the method is switched off.</param>
    public static ILeftBehindNeighbours CreateVisibleNeighbours(
        ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, IRunCaches caches, VisibleLeftBehindSettings? settings) =>
        new VisibleNeighbours(caches.Shapes, SurroundingsIndex(targets, decisions, caches, settings?.Surroundings));

    /// <summary>
    /// The neighbours measured around an invisible target with the look-around distance and its reach (see
    /// <see cref="InvisibleNeighbours"/>); none while the method is switched off.
    /// </summary>
    /// <param name="decisions">The decisions the neighbours stand in: each visible target where their rounds measure it.</param>
    /// <param name="caches">Index the visible target objects; their base facts and shapes are read.</param>
    /// <param name="settings">Null when the method is switched off.</param>
    public static ILeftBehindNeighbours CreateInvisibleNeighbours(
        ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, IRunCaches caches, InvisibleLeftBehindSettings? settings) =>
        new InvisibleNeighbours(caches.Shapes, new ReachRule(caches.Bases), SurroundingsIndex(targets, decisions, caches, settings?.Surroundings));

    /// <summary>How much of the cleaned mod's drawn objects still stand around a point, judged on given decisions (see <see cref="ClearedAreasJudging"/>).</summary>
    /// <param name="targets">Every target, by id.</param>
    /// <param name="overriddenLater">The cleaned mod's objects later plugins hold, which the areas count as standing.</param>
    /// <param name="spaces">Every space the scan read.</param>
    /// <param name="caches">Index the shown targets where the rounds measure them; their shapes are read.</param>
    public static IClearedAreasJudging CreateClearedAreasJudging(
        ImmutableArray<TargetObject> targets, ImmutableArray<TargetObject> overriddenLater, IReadOnlyDictionary<RecordKey, SpaceFact> spaces, IRunCaches caches) =>
        new ClearedAreasJudging(targets, overriddenLater, spaces, caches);

    private static (IVisibleTargetObjects Index, float LookAround)? SurroundingsIndex(
        ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, IRunCaches caches, LeftBehindSurroundings? surroundings) =>
        surroundings is { } rule ? (caches.IndexVisibleTargets(decisions.TargetsAsTheRoundsMeasureThem(targets)), rule.LookAround) : null;
}

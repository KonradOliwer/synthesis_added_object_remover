using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups;

/// <summary>
/// Groups the cleaned mod's own objects, finds the groups the decisions of the earlier steps seeded, and judges each standing object
/// of a seeded group: removed, or kept as its kind is always kept.
/// </summary>
/// <param name="targets">Every target, by id, as collected.</param>
/// <param name="members">The cleaned mod's own objects where the rounds measure them, so a fitted object joins at its new place.</param>
/// <param name="grouping">The groups of <paramref name="members"/>, by position in it.</param>
internal sealed class WholeGroupRule(
    ImmutableArray<TargetObject> targets,
    ImmutableArray<TargetObject> members,
    ObjectGrouping grouping,
    IReadOnlyList<IKindsAlwaysKept> kindsAlwaysKept,
    ILeftBehindRoundJoin roundJoin)
{
    /// <summary>The position in <c>members</c> of each of the cleaned mod's own objects, by target index.</summary>
    private readonly int[] _positionOf = PositionsOf(targets.Length, members);

    public static WholeGroupRule Create(WholeGroups.Input input, WholeGroupsSettings settings)
    {
        ImmutableArray<TargetObject> members =
            [.. input.Decisions.TargetsAsTheRoundsMeasureThem(input.Targets).Where(target => target.Origin == TargetOrigin.CleanedMod)];
        var grouping = ObjectGrouping.Of(members, input.Shapes, settings.GapInUnits, input.Execution);
        return new WholeGroupRule(input.Targets, members, grouping, input.KindsAlwaysKept, input.RoundJoin);
    }

    /// <param name="decisions">The decisions of the earlier steps; every object they took from its place seeds.</param>
    /// <returns>The seeded groups, in the order of their lowest member.</returns>
    public ImmutableArray<SeededGroup> FindSeededGroups(IRemovalDecisions decisions)
    {
        var seedOf = new SortedDictionary<int, TargetId>();
        foreach (var target in targets.Where(target => decisions.HasLeftItsPlace(target.Id)))
        {
            foreach (var group in GroupsSeededBy(target)) seedOf.TryAdd(group, target.Id);
        }
        return [.. seedOf.Select(seeded => ToSeededGroup(grouping.Groups[seeded.Key], seeded.Value))];
    }

    /// <param name="groups">The seeded groups.</param>
    /// <param name="decisions">The decisions of the earlier steps.</param>
    /// <returns>A check for each member of a seeded group a round may still remove and that is no object to keep, in target order.</returns>
    public ImmutableArray<WholeGroupCheck> Judge(ImmutableArray<SeededGroup> groups, IRemovalDecisions decisions) =>
    [
        .. groups
            .SelectMany((group, groupIndex) => group.Members.Select(member => (Member: member, GroupIndex: groupIndex)))
            .Where(entry => decisions.MayARoundReplacingFitsStillRemove(entry.Member))
            .OrderBy(entry => entry.Member.Index)
            .Select(entry => new WholeGroupCheck(entry.Member.Index, entry.GroupIndex, Decide(targets[entry.Member.Index]))),
    ];

    private LeftBehindOutcome Decide(TargetObject target) => roundJoin.KeptWhenProtected(LeftBehindOutcome.RemovedInGroupWithClash, target, kindsAlwaysKept);

    /// <summary>
    /// The group of one of the cleaned mod's own objects, whatever its kind; for another mod's object the cleaned mod changed, every
    /// group with a drawn box within the gap of its box at its original place.
    /// The original place is its earlier version as a whole, also when a part of it cannot go back (such as a position a later plugin
    /// moved again), so the object seeds where it stood without the cleaned mod, never in the cleaned mod's layout.
    /// </summary>
    private IEnumerable<int> GroupsSeededBy(TargetObject target) =>
        target.Origin == TargetOrigin.CleanedMod
            ? [grouping.GroupOf(_positionOf[target.Id.Index])]
            : grouping.GroupsWithADrawnBoxNear(target.RestoredAs(EarlierVersionOf(target) with { Undo = WholeEarlierVersion }));

    /// <summary>The parts that make up where and as what an earlier version stands.</summary>
    private const PlacedParts WholeEarlierVersion = PlacedParts.Position | PlacedParts.Rotation | PlacedParts.Scale | PlacedParts.BaseAndData;

    private static int[] PositionsOf(int targetCount, ImmutableArray<TargetObject> members)
    {
        var positionOf = new int[targetCount];
        for (var position = 0; position < members.Length; position++) positionOf[members[position].Id.Index] = position;
        return positionOf;
    }

    private static EarlierVersion EarlierVersionOf(TargetObject target) =>
        target.EarlierVersion ?? throw new InvalidOperationException($"Target {target.Key} is not a changed object, so it has no earlier version.");

    private SeededGroup ToSeededGroup(int[] positions, TargetId seed)
    {
        var footprint = Box.UnionAll(positions.Select(grouping.BoxOfMember));
        return new SeededGroup(
            [.. positions.Select(position => members[position].Id)],
            seed,
            members[positions[0]].SpaceKey,
            new Vector2(footprint.Min.X, footprint.Min.Y),
            new Vector2(footprint.Max.X, footprint.Max.Y));
    }
}

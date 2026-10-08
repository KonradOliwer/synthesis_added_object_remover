using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Teleport doors and targets that a non-placed record or a placed object other than the checked
/// target objects links to are never removed, because the game may crash or break on them. Links
/// between target objects do not keep them: they form linked groups, which stay together, so a
/// group stays whole as soon as one member must stay.
/// </summary>
internal sealed class ObjectsToKeep : IObjectsToKeep
{
    private static readonly KeepReason TeleportDoor = new(KeepKind.TeleportDoor);

    private static readonly KeepReason CheckFailed = new(KeepKind.CheckFailed);

    private readonly KeepReason?[] _ownReasons;
    private readonly KeepReason?[] _reasons;

    private ObjectsToKeep(LinkedGroups groups, KeepReason?[] ownReasons, ImmutableArray<TargetFailure> checkFailures)
    {
        Groups = groups;
        CheckFailures = checkFailures;
        _ownReasons = ownReasons;
        _reasons = new KeepReason?[ownReasons.Length];
    }

    /// <summary>The linked groups of the target objects.</summary>
    public LinkedGroups Groups { get; }

    /// <summary>The target objects whose check failed unexpectedly, in target order; each of them is kept.</summary>
    public ImmutableArray<TargetFailure> CheckFailures { get; }

    ILinkedGroups IObjectsToKeep.Groups => Groups;

    /// <param name="references">By target index: why a non-target record depends on it.</param>
    public static ObjectsToKeep Build(
        IReadOnlyList<TargetObject> targets, IEnumerable<TargetLink> links, IReadOnlyList<KeepReason?> references)
    {
        var checkFailures = ImmutableArray.CreateBuilder<TargetFailure>();
        var ownReasons = targets.Select((target, index) => FindOwnReason(target, index, references, checkFailures)).ToArray();
        var protection = new ObjectsToKeep(LinkedGroups.Build(targets.Count, links), ownReasons, checkFailures.ToImmutable());
        foreach (var members in protection.Groups.All) protection.AssignGroupReasons(members, targets);
        return protection;
    }

    /// <remarks>A target object whose check fails is kept: keeping is safe when it is not known what depends on it.</remarks>
    private static KeepReason? FindOwnReason(TargetObject target, int index, IReadOnlyList<KeepReason?> references, ImmutableArray<TargetFailure>.Builder checkFailures)
    {
        try
        {
            return target.IsTeleportDoor ? TeleportDoor : references[index];
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            checkFailures.Add(new TargetFailure(index, Failures.Describe(ex)));
            return CheckFailed;
        }
    }

    /// <summary>Why the target stays: its own reason, or that of the first member of its group that has one.</summary>
    public bool TryGetKeepReason(int targetIndex, [NotNullWhen(true)] out KeepReason? reason)
    {
        reason = _reasons[targetIndex];
        return reason != null;
    }

    public bool IsProtected(int targetIndex) => _reasons[targetIndex] != null;

    /// <summary>Why the target itself must stay, ignoring its group; null when nothing depends on it.</summary>
    public KeepReason? GetOwnReason(int targetIndex) => _ownReasons[targetIndex];

    private void AssignGroupReasons(IReadOnlyList<int> members, IReadOnlyList<TargetObject> targets)
    {
        var keeper = members.FirstOrDefault(member => _ownReasons[member] != null, -1);
        if (keeper < 0) return;

        var keeperTarget = targets[keeper];
        var linkedReason = new KeepReason(
            KeepKind.LinkedGroup, Keeper: new GroupKeeper(keeperTarget.Key, keeperTarget.EditorId, _ownReasons[keeper]!));
        foreach (var member in members) _reasons[member] = _ownReasons[member] ?? linkedReason;
    }
}

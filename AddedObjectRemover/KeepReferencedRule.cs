using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>What makes a target object stay.</summary>
internal enum KeepKind
{
    /// <summary>A teleport door, or the destination of one.</summary>
    TeleportDoor,

    /// <summary>A placed object that is not a checked target object links to it.</summary>
    PlacedReference,

    /// <summary>A quest, package, location, script, ... links to it.</summary>
    NonPlacedReference,

    /// <summary>Another member of its linked group must stay.</summary>
    LinkedGroup,
}

/// <summary>Why a target object is never removed.</summary>
/// <param name="Category">Groups reasons in the summary, e.g. "placed object: Enable Parent" or "Quest record".</param>
/// <param name="Detail">The full reason for the log, naming the linking record.</param>
internal sealed record KeepReason(KeepKind Kind, string Category, string Detail);

/// <summary>
/// Teleport doors and targets that a non-placed record or a placed object other than the checked
/// target objects links to are never removed, because the game may crash or break on them. Links
/// between target objects do not keep them: they form linked groups, which stay together, so a
/// group stays whole as soon as one member must stay.
/// </summary>
internal sealed class KeepReferencedRule
{
    private const string LinkedGroupCategory = "linked to a kept object";

    private static readonly KeepReason TeleportDoor = new(KeepKind.TeleportDoor, "teleport door", "teleport door");

    private readonly KeepReason?[] _ownReasons;
    private readonly KeepReason?[] _reasons;

    /// <param name="targetReferences">Target FormKey -> why a non-target record depends on it.</param>
    public KeepReferencedRule(IReadOnlyList<TargetObject> targets, IReadOnlyDictionary<FormKey, KeepReason> targetReferences, LinkedGroups groups)
    {
        _ownReasons = targets.Select(target => FindOwnReason(target, targetReferences)).ToArray();
        _reasons = new KeepReason?[targets.Count];
        foreach (var members in groups.All) AssignGroupReasons(members, targets);
    }

    /// <summary>Why the target stays: its own reason, or that of the first member of its group that has one.</summary>
    public bool TryGetKeepReason(int targetIndex, [NotNullWhen(true)] out KeepReason? reason)
    {
        reason = _reasons[targetIndex];
        return reason != null;
    }

    /// <summary>Why the target itself must stay, ignoring its group; null when nothing depends on it.</summary>
    public KeepReason? GetOwnReason(int targetIndex) => _ownReasons[targetIndex];

    private static KeepReason? FindOwnReason(TargetObject target, IReadOnlyDictionary<FormKey, KeepReason> targetReferences) =>
        target.IsTeleportDoor ? TeleportDoor : targetReferences.GetValueOrDefault(target.Record.FormKey);

    private void AssignGroupReasons(IReadOnlyList<int> members, IReadOnlyList<TargetObject> targets)
    {
        var keeper = members.FirstOrDefault(member => _ownReasons[member] != null, -1);
        if (keeper < 0) return;

        var keeperReason = _ownReasons[keeper]!;
        var linkedReason = new KeepReason(
            KeepKind.LinkedGroup,
            LinkedGroupCategory,
            $"linked to {RecordNames.Describe(targets[keeper].Record)}, which is kept: {keeperReason.Detail}");
        foreach (var member in members) _reasons[member] = _ownReasons[member] ?? linkedReason;
    }
}

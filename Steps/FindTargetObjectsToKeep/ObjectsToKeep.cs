using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Teleport doors and targets that a non-placed record or a placed object other than the checked
/// target objects links to are never removed, because the game may crash or break on them. Nor are
/// targets whose own or base object's script names a quest, scene or global variable: a disabled
/// object's script can stop doing its part (OnLoad does not fire on it), so that quest or scene can stall.
/// Nor are targets with a location role, which a quest alias can pick them by.
/// Links between target objects do not keep them: they form linked groups, which stay together, so
/// a group stays whole as soon as one member must stay.
/// </summary>
internal sealed class ObjectsToKeep : IObjectsToKeep
{
    private static readonly KeepReason TeleportDoor = new(KeepKind.TeleportDoor);

    private static readonly KeepReason CheckFailed = new(KeepKind.CheckFailed);

    private static readonly KeepReason OwnLocationRefType = new(KeepKind.LocationRole);

    private readonly KeepReason?[] _ownReasons;
    private readonly KeepReason?[] _reasons;

    private ObjectsToKeep(LinkedGroups groups, KeepReason?[] ownReasons, ImmutableArray<PartFailure> checkFailures)
    {
        Groups = groups;
        CheckFailures = checkFailures;
        _ownReasons = ownReasons;
        _reasons = new KeepReason?[ownReasons.Length];
    }

    /// <summary>The linked groups of the target objects.</summary>
    public LinkedGroups Groups { get; }

    /// <summary>The target objects whose check failed unexpectedly, in target order; each of them is kept.</summary>
    public ImmutableArray<PartFailure> CheckFailures { get; }

    ILinkedGroups IObjectsToKeep.Groups => Groups;

    /// <param name="references">By target index: why a non-target record depends on it.</param>
    /// <param name="bases">The base records, whose scripts are checked.</param>
    public static ObjectsToKeep Build(
        IReadOnlyList<TargetObject> targets, IEnumerable<TargetLink> links, IReadOnlyList<KeepReason?> references, IBaseFacts bases)
    {
        var checkFailures = ImmutableArray.CreateBuilder<PartFailure>();
        var ownReasons = targets.Select((target, index) => FindOwnReason(target, index, references, bases, checkFailures)).ToArray();
        var protection = new ObjectsToKeep(LinkedGroups.Build(targets.Count, links), ownReasons, checkFailures.ToImmutable());
        foreach (var members in protection.Groups.All) protection.AssignGroupReasons(members, targets);
        return protection;
    }

    /// <remarks>A target object whose check fails is kept: keeping is safe when it is not known what depends on it.</remarks>
    private static KeepReason? FindOwnReason(
        TargetObject target, int index, IReadOnlyList<KeepReason?> references, IBaseFacts bases, ImmutableArray<PartFailure>.Builder checkFailures)
    {
        try
        {
            return target.IsTeleportDoor ? TeleportDoor : references[index] ?? FindScriptReason(target, bases) ?? FindLocationRoleReason(target);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            checkFailures.Add(new PartFailure(KeepParts.KeepCheck, Failures.DescribeFailure(ex), new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index)));
            return CheckFailed;
        }
    }

    /// <remarks>A script that names only target objects puts them in its object's linked group instead.</remarks>
    private static KeepReason? FindScriptReason(TargetObject target, IBaseFacts bases)
    {
        if (target.NamedByScript is { } own) return ScriptReason(own, onBase: false);
        return target.Base is { } baseKey && bases.Of(baseKey).NamedByScript is { } onBase ? ScriptReason(onBase, onBase: true) : null;
    }

    private static KeepReason ScriptReason(ScriptNamedRecord named, bool onBase) =>
        new(KeepKind.ScriptNamesQuestSceneOrGlobal, Script: new ScriptFinding(named, onBase));

    /// <remarks>
    /// A map marker's ref type only lets a quest show its location on the map, and a disabled marker leaves the quest
    /// working, so map markers stay removable.
    /// </remarks>
    private static KeepReason? FindLocationRoleReason(TargetObject target) =>
        target.HasLocationRefType && !target.HasMapMarker ? OwnLocationRefType : null;

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

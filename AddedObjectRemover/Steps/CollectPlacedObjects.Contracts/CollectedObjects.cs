using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>A link from one target object to another, which puts both in one linked group.</summary>
public readonly record struct TargetLink(TargetId From, TargetId To);

/// <summary>
/// Everything the run reads about the placed objects of the target spaces, fixed before any
/// decision and never changed afterwards.
/// </summary>
/// <param name="Targets">In <see cref="RecordKeyOrder"/>; the position is the <see cref="TargetId"/>.</param>
/// <param name="OtherModObjects">Other mods' objects that may make a target too close, in <see cref="RecordKeyOrder"/>.</param>
/// <param name="SupportOnlyObjects">
/// Every other present placed object of the target spaces (base game, masters, excluded plugins,
/// records the target overrides, target records a later plugin overrides), in <see cref="RecordKeyOrder"/>;
/// null unless objects of any plugin are needed as supporters or objects around a marker.
/// </param>
/// <param name="Links">Every link into the target plugin as read, placed records before non-placed ones, in the order found.</param>
/// <param name="OverriddenOthers">Only with the detailed log.</param>
public sealed record CollectedObjects(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<OtherObject> OtherModObjects,
    ImmutableArray<OtherObject>? SupportOnlyObjects,
    ImmutableArray<LinkFact> Links,
    IReadOnlyDictionary<RecordKey, SpaceFact> Spaces,
    ReadCounts Counts,
    ImmutableArray<OverriddenOtherRecord> OverriddenOthers)
{
    public TargetObject this[TargetId id] => Targets[id.Index];

    /// <summary>The spaces holding a target object.</summary>
    public IReadOnlySet<RecordKey> TargetSpaces() => Targets.Select(target => target.SpaceKey).ToHashSet();

    /// <summary>An other-mod object, or a support-only object when those were collected.</summary>
    public OtherObject Other(OtherId id) =>
        id.Index < OtherModObjects.Length ? OtherModObjects[id.Index] : RequireSupportOnlyObjects()[id.Index - OtherModObjects.Length];

    /// <summary>The other-mod objects and the support-only objects together, by <see cref="OtherId"/>; only when those were collected.</summary>
    public IEnumerable<OtherObject> OtherModAndSupportOnlyObjects => OtherModObjects.Concat(RequireSupportOnlyObjects());

    /// <summary>Reading the support-only objects when they were not collected in this run is a bug.</summary>
    public ImmutableArray<OtherObject> RequireSupportOnlyObjects() =>
        SupportOnlyObjects ?? throw new InvalidOperationException("The support-only objects were not collected in this run.");
}

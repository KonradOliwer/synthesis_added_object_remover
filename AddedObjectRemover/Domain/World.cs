using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A value a run collects only when a step needs it; reading one that was not collected is a bug.</summary>
internal readonly struct Collected<T>
{
    private readonly T _value;

    private Collected(T value)
    {
        _value = value;
        IsCollected = true;
    }

    public static Collected<T> NotCollected => default;

    public bool IsCollected { get; }

    public T Value => IsCollected ? _value : throw new InvalidOperationException($"{typeof(T).Name} was not collected in this run.");

    public static Collected<T> Of(T value) => new(value);
}

/// <summary>A link from one target object to another, which puts both in one linked group.</summary>
internal readonly record struct TargetLink(TargetId From, TargetId To);

/// <param name="TargetPluginLinks">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
/// <param name="OtherInvalidPlacements">Other mods' objects in target spaces that the game shows but whose position or rotation is out of range or not a number.</param>
internal sealed record ReadCounts(
    int RecordsScanned,
    int TargetsOverriddenLater,
    int TargetsHiddenOrWithoutPlacement,
    int OthersOverriddenByTarget,
    int OtherInvalidPlacements,
    int TargetPluginLinks,
    int NavmeshCount);

/// <summary>
/// Everything the run reads about the placed objects of the target spaces, fixed before any
/// decision and never changed afterwards.
/// </summary>
/// <param name="Targets">In <see cref="FormKeyOrder"/>; the position is the <see cref="TargetId"/>.</param>
/// <param name="Rivals">Other mods' objects that may make a target too close, in <see cref="FormKeyOrder"/>.</param>
/// <param name="Backdrop">
/// Every other present placed object of the target spaces (base game, masters, excluded plugins,
/// records the target overrides, target records a later plugin overrides), in <see cref="FormKeyOrder"/>;
/// collected only when objects of any plugin are needed as supporters or obstacles.
/// </param>
/// <param name="References">By <see cref="TargetId"/>: why a record that is not a checked target object depends on it; the first reason found, placed records before non-placed ones.</param>
/// <param name="OverriddenOthers">Only with the detailed log.</param>
internal sealed record World(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<OtherObject> Rivals,
    Collected<ImmutableArray<OtherObject>> Backdrop,
    ImmutableArray<TargetLink> Links,
    ImmutableArray<KeepReason?> References,
    IReadOnlyDictionary<FormKey, string> SpaceNames,
    ReadCounts Counts,
    ImmutableArray<OverriddenOtherRecord> OverriddenOthers)
{
    public TargetObject this[TargetId id] => Targets[id.Index];

    /// <summary>A rival, or a backdrop object when the backdrop was collected.</summary>
    public OtherObject Other(OtherId id) => id.Index < Rivals.Length ? Rivals[id.Index] : Backdrop.Value[id.Index - Rivals.Length];

    /// <summary>The rivals and the backdrop together, by <see cref="OtherId"/>; only when the backdrop was collected.</summary>
    public IEnumerable<OtherObject> RivalsAndBackdrop => Rivals.Concat(Backdrop.Value);
}

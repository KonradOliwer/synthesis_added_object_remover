using System.Numerics;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The parts of a placed record (<see cref="PlacedParts"/>), each with its compare and its copy.</summary>
internal sealed class PlacedRecordParts
{
    /// <summary>A placed reference without a scale shows at this scale.</summary>
    private const float UnscaledScale = 1f;

    /// <summary>The Persistent flag follows the child list the record stands in.</summary>
    private const int PersistentBit = (int)PlacedObject.DefaultMajorFlag.Persistent;

    private const int PresenceBits = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled | RecordHeader.DeletedBit;

    /// <summary>Never compared, never written.</summary>
    public const int ExcludedFlagBits = PersistentBit | RecordHeader.CompressedBit;

    /// <summary>The record flags the rest of the record holds.</summary>
    private const int RestFlagBits = ~(PresenceBits | ExcludedFlagBits);

    /// <summary>Never compared, never written.</summary>
    public static IReadOnlyList<string> ExcludedFields => RecordHeader.Fields;

    private readonly PlacedObject.TranslationMask _objectRest;
    private readonly PlacedNpc.TranslationMask _npcRest;
    private readonly APlacedTrap.TranslationMask _trapRest;

    public PlacedRecordParts(PlacedPartTolerances tolerances)
    {
        var listed = ListedParts(tolerances);
        NavDoorLink = listed.Single(entry => entry.Part == PlacedParts.NavDoorLink).Compare;
        var doorLinkFields = NavDoorLink.Fields;
        string[] shared = [.. listed.SelectMany(entry => entry.Compare.Fields).Except(doorLinkFields)
            .Concat(ExcludedFields).Concat(RecordHeader.FlagFields)];
        _objectRest = TranslationMasks.AllBut<PlacedObject.TranslationMask>(shared.Concat(doorLinkFields));
        _npcRest = TranslationMasks.AllBut<PlacedNpc.TranslationMask>(shared);
        _trapRest = TranslationMasks.AllBut<APlacedTrap.TranslationMask>(shared);
        Parts = [.. listed, (PlacedParts.BaseAndData, new([], RestDiffers, CopyRest, HoldsRawBytes))];
    }

    /// <summary>Every part, <see cref="PlacedParts.BaseAndData"/> last; together they hold every field but the excluded ones.</summary>
    public IReadOnlyList<(PlacedParts Part, RecordPart<IPlacedGetter, IPlaced> Compare)> Parts { get; }

    /// <summary>The <see cref="PlacedParts.NavDoorLink"/> part of <see cref="Parts"/>.</summary>
    public RecordPart<IPlacedGetter, IPlaced> NavDoorLink { get; }

    /// <summary>The parts with fields of their own; only placed objects (REFR) have the door link.</summary>
    private static List<(PlacedParts Part, RecordPart<IPlacedGetter, IPlaced> Compare)> ListedParts(PlacedPartTolerances tolerances) =>
        [
            PlacementPart(PlacedParts.Position, PlacedRecordExtensions.GetPosition, static (a, b) => Vector3.Abs(a - b), tolerances.Position, static (into, from) => into.Position = from.Position),
            PlacementPart(PlacedParts.Rotation, PlacedRecordExtensions.GetRotation, Vectors.ShortestAngles, tolerances.Rotation, static (into, from) => into.Rotation = from.Rotation),
            (PlacedParts.Scale, new([nameof(IPlacedGetter.Scale)],
                (one, other) => MathF.Abs((one.Scale ?? UnscaledScale) - (other.Scale ?? UnscaledScale)) > tolerances.Scale,
                static (target, source) => target.Scale = source.Scale)),
            (PlacedParts.Presence, new([nameof(IPlacedGetter.EnableParent)], PresenceDiffers, CopyPresence)),
            (PlacedParts.NavDoorLink, new([nameof(IPlacedObjectGetter.NavigationDoorLink)], NavDoorLinkDiffers, CopyNavDoorLink)),
        ];

    /// <param name="gapOf">The gap between two values, per axis.</param>
    /// <param name="copyValue">Writes the source placement's value into the target placement.</param>
    private static (PlacedParts Part, RecordPart<IPlacedGetter, IPlaced> Compare) PlacementPart(
        PlacedParts part, Func<IPlacementGetter, Vector3> valueOf, Func<Vector3, Vector3, Vector3> gapOf, float tolerance, Action<Placement, IPlacementGetter> copyValue) =>
        (part, new([nameof(IPlacedGetter.Placement)],
            (one, other) => DiffersBeyond(one.Placement, other.Placement, valueOf, gapOf, tolerance),
            (target, source) => CopyPlacement(target, source, copyValue)));

    private static bool DiffersBeyond(
        IPlacementGetter? one, IPlacementGetter? other, Func<IPlacementGetter, Vector3> valueOf, Func<Vector3, Vector3, Vector3> gapOf, float tolerance) =>
        (one, other) switch
        {
            ({ } a, { } b) => gapOf(valueOf(a), valueOf(b)) is var gap && (gap.X > tolerance || gap.Y > tolerance || gap.Z > tolerance),
            _ => (one is null) != (other is null),
        };

    /// <remarks>A source without placement data takes the target's away: position and rotation always differ together then.</remarks>
    private static void CopyPlacement(IPlaced target, IPlacedGetter source, Action<Placement, IPlacementGetter> copyValue)
    {
        if (source.Placement is not { } placement)
        {
            target.Placement = null;
            return;
        }
        target.Placement ??= new Placement();
        copyValue(target.Placement, placement);
    }

    private static bool PresenceDiffers(IPlacedGetter one, IPlacedGetter other) =>
        FlagBits.DifferIn(one, other, PresenceBits) || !Equals(one.EnableParent, other.EnableParent);

    private static void CopyPresence(IPlaced target, IPlacedGetter source)
    {
        FlagBits.CopyMajorBits(target, source, PresenceBits);
        target.EnableParent = MutagenCalls.Read(() => source.EnableParent?.DeepCopy());
    }

    private static bool NavDoorLinkDiffers(IPlacedGetter one, IPlacedGetter other) =>
        !Equals((one as IPlacedObjectGetter)?.NavigationDoorLink, (other as IPlacedObjectGetter)?.NavigationDoorLink);

    private static void CopyNavDoorLink(IPlaced target, IPlacedGetter source)
    {
        if (target is IPlacedObject door) door.NavigationDoorLink = MutagenCalls.Read(() => (source as IPlacedObjectGetter)?.NavigationDoorLink?.DeepCopy());
    }

    /// <remarks>Versions of different record types always differ here.</remarks>
    private bool RestDiffers(IPlacedGetter one, IPlacedGetter other) =>
        FlagBits.DifferIn(one, other, RestFlagBits) || !((one, other) switch
        {
            (IPlacedObjectGetter a, IPlacedObjectGetter b) => a.Equals(b, _objectRest),
            (IPlacedNpcGetter a, IPlacedNpcGetter b) => a.Equals(b, _npcRest),
            (IAPlacedTrapGetter a, IAPlacedTrapGetter b) => a.Equals(b, _trapRest),
            _ => false,
        });

    /// <remarks>Versions of different record types cannot be copied into each other; the caller keeps such a record.</remarks>
    private void CopyRest(IPlaced target, IPlacedGetter source)
    {
        FlagBits.CopyMajorBits(target, source, RestFlagBits);
        switch (target, source)
        {
            case (IPlacedObjectInternal copy, IPlacedObjectGetter version): MutagenCalls.Copy(() => copy.DeepCopyIn(version, errorMask: null, copyMask: _objectRest.GetCrystal())); break;
            case (IPlacedNpcInternal copy, IPlacedNpcGetter version): MutagenCalls.Copy(() => copy.DeepCopyIn(version, errorMask: null, copyMask: _npcRest.GetCrystal())); break;
            case (IAPlacedTrapInternal copy, IAPlacedTrapGetter version): MutagenCalls.Copy(() => copy.DeepCopyIn(version, errorMask: null, copyMask: _trapRest.GetCrystal())); break;
            default: throw new ArgumentException($"A {source.GetType().Name} cannot be copied into a {target.GetType().Name}.", nameof(source));
        }
    }

    private static bool HoldsRawBytes(IPlacedGetter version) => version switch
    {
        IPlacedObjectGetter placed => placed.XWCN is not null || placed.XWCS is not null || placed.DistantLodData is not null
            || placed.RagdollData is not null || placed.RagdollBipedData is not null || placed.XCVL is not null || placed.XCZA is not null || placed.XORD is not null,
        IPlacedNpcGetter npc => npc.RagdollData is not null || npc.RagdollBipedData is not null,
        IAPlacedTrapGetter trap => trap.IgnoredBySandbox is not null,
        _ => false,
    };
}

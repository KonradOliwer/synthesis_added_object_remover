using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Which part of a cell record: one of <see cref="CellPart"/>, and for <see cref="CellPart.Region"/> the region.</summary>
internal readonly record struct CellPartKey(CellPart Part, FormKey? Region);

/// <summary>The parts of a cell record (<see cref="CellPart"/>), each with its compare and its copy.</summary>
/// <param name="waterHeightTolerance">The largest water height gap that is no change, in game units.</param>
internal sealed class CellRecordParts(float waterHeightTolerance)
{
    private const int HasWaterBit = (int)Cell.Flag.HasWater;

    /// <summary>Never compared, never written: the cell's own kind.</summary>
    public const int ExcludedFlagBits = (int)Cell.Flag.IsInteriorCell;

    /// <summary>Never compared, never written: the Persistent flag follows the cell's children, and how the record is stored.</summary>
    public const int ExcludedMajorFlagBits = (int)Cell.MajorFlag.Persistent | RecordHeader.CompressedBit;

    /// <summary>Never compared, never written: identity, header stamps, group data, child records judged as records of their own, and the grid.</summary>
    public static readonly ImmutableArray<string> ExcludedFields =
    [
        .. RecordHeader.Fields,
        nameof(ICellGetter.Timestamp),
        nameof(ICellGetter.UnknownGroupData),
        nameof(ICellGetter.PersistentTimestamp),
        nameof(ICellGetter.PersistentUnknownGroupData),
        nameof(ICellGetter.TemporaryTimestamp),
        nameof(ICellGetter.TemporaryUnknownGroupData),
        nameof(ICellGetter.Persistent),
        nameof(ICellGetter.Temporary),
        nameof(ICellGetter.NavigationMeshes),
        nameof(ICellGetter.Landscape),
        nameof(ICellGetter.Grid),
    ];

    /// <summary>The parts that are whole fields, compared and copied through one translation mask each.</summary>
    private static readonly ImmutableArray<(CellPart Part, ImmutableArray<string> Fields)> FieldParts =
    [
        (CellPart.Lighting, [nameof(ICellGetter.Lighting), nameof(ICellGetter.LightingTemplate)]),
        (CellPart.ImageSpace, [nameof(ICellGetter.ImageSpace)]),
        (CellPart.AcousticSpace, [nameof(ICellGetter.AcousticSpace)]),
        (CellPart.Music, [nameof(ICellGetter.Music)]),
        (CellPart.WaterLook, [nameof(ICellGetter.Water), nameof(ICellGetter.WaterVelocity), nameof(ICellGetter.WaterNoiseTexture), nameof(ICellGetter.WaterEnvironmentMap)]),
        (CellPart.SkyAndWeatherFromRegion, [nameof(ICellGetter.SkyAndWeatherFromRegion)]),
        (CellPart.Name, [nameof(ICellGetter.Name)]),
        (CellPart.Ownership, [nameof(ICellGetter.Owner), nameof(ICellGetter.FactionRank)]),
        (CellPart.EncounterZone, [nameof(ICellGetter.EncounterZone)]),
        (CellPart.Location, [nameof(ICellGetter.Location)]),
    ];

    /// <summary>The parts that are one bit of the cell flags.</summary>
    private static readonly ImmutableArray<(CellPart Part, Cell.Flag Bit)> FlagParts =
    [
        (CellPart.CantTravelFromHere, Cell.Flag.CantTravelFromHere),
        (CellPart.NoLodWater, Cell.Flag.NoLodWater),
        (CellPart.PublicArea, Cell.Flag.PublicArea),
        (CellPart.HandChanged, Cell.Flag.HandChanged),
        (CellPart.ShowSky, Cell.Flag.ShowSky),
        (CellPart.UseSkyLighting, Cell.Flag.UseSkyLighting),
    ];

    /// <summary>The parts that are one bit of the record flags.</summary>
    private static readonly ImmutableArray<(CellPart Part, Cell.MajorFlag Bit)> MajorFlagParts =
    [
        (CellPart.OffLimits, Cell.MajorFlag.OffLimits),
        (CellPart.CantWait, Cell.MajorFlag.CantWait),
    ];

    private readonly IReadOnlyList<(CellPartKey Key, RecordPart<ICellGetter, ICell> Compare)> _fixedParts = FixedParts(waterHeightTolerance);

    /// <summary>Every part of the cell's versions: one per region any version lists, in the order first listed; <see cref="CellPart.RestOfCell"/> last.</summary>
    /// <param name="versions">The cell's versions in load order.</param>
    public IReadOnlyList<(CellPartKey Key, RecordPart<ICellGetter, ICell> Compare)> For(IReadOnlyList<ICellGetter> versions)
    {
        var regions = versions.SelectMany(version => version.Regions ?? []).Select(link => link.FormKey).Distinct()
            .Select(region => (new CellPartKey(CellPart.Region, region), RegionPart(region)));
        return [.. _fixedParts.SkipLast(1), .. regions, _fixedParts[^1]];
    }

    private static List<(CellPartKey Key, RecordPart<ICellGetter, ICell> Compare)> FixedParts(float waterHeightTolerance)
    {
        List<(CellPartKey Key, RecordPart<ICellGetter, ICell> Compare)> parts =
        [
            .. FieldParts.Select(entry => (Key(entry.Part), FieldPart(entry.Fields))),
            (Key(CellPart.WaterLevel), new([nameof(ICellGetter.WaterHeight)],
                (one, other) => WaterLevelDiffers(one, other, waterHeightTolerance), CopyWaterLevel, static _ => false)),
            .. FlagParts.Select(entry => (Key(entry.Part), FlagPart((int)entry.Bit))),
            .. MajorFlagParts.Select(entry => (Key(entry.Part), MajorFlagPart((int)entry.Bit))),
        ];
        parts.Add((Key(CellPart.RestOfCell), RestPart(parts.SelectMany(entry => entry.Compare.Fields).Append(nameof(ICellGetter.Regions)))));
        return parts;
    }

    private static CellPartKey Key(CellPart part) => new(part, Region: null);

    private static RecordPart<ICellGetter, ICell> FieldPart(ImmutableArray<string> fields)
    {
        var mask = TranslationMasks.Only<Cell.TranslationMask>(fields);
        return new(fields, (one, other) => !one.Equals(other, mask), (target, source) => ((ICellInternal)target).DeepCopyIn(source, errorMask: null, copyMask: mask.GetCrystal()), static _ => false);
    }

    private static bool WaterLevelDiffers(ICellGetter one, ICellGetter other, float tolerance) =>
        FlagBits.Differ((int)one.Flags, (int)other.Flags, HasWaterBit) || (one.WaterHeight, other.WaterHeight) switch
        {
            ({ } a, { } b) => MathF.Abs(a - b) > tolerance,
            var (a, b) => a.HasValue != b.HasValue,
        };

    private static void CopyWaterLevel(ICell target, ICellGetter source)
    {
        target.Flags = (Cell.Flag)FlagBits.CopyBits((int)target.Flags, (int)source.Flags, HasWaterBit);
        target.WaterHeight = source.WaterHeight;
    }

    private static RecordPart<ICellGetter, ICell> FlagPart(int bit) => new(
        [],
        (one, other) => FlagBits.Differ((int)one.Flags, (int)other.Flags, bit),
        (target, source) => target.Flags = (Cell.Flag)FlagBits.CopyBits((int)target.Flags, (int)source.Flags, bit),
        static _ => false);

    private static RecordPart<ICellGetter, ICell> MajorFlagPart(int bit) => new(
        [],
        (one, other) => FlagBits.Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, bit),
        (target, source) => target.MajorRecordFlagsRaw = FlagBits.CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, bit),
        static _ => false);

    private static RecordPart<ICellGetter, ICell> RegionPart(FormKey region) => new(
        [nameof(ICellGetter.Regions)],
        (one, other) => Lists(one, region) != Lists(other, region),
        (target, source) => CopyRegion(target, source, region),
        static _ => false);

    private static bool Lists(ICellGetter version, FormKey region) => version.Regions?.Any(link => link.FormKey == region) ?? false;

    private static void CopyRegion(ICell target, ICellGetter source, FormKey region)
    {
        if (!Lists(source, region))
        {
            target.Regions?.RemoveAll(link => link.FormKey == region);
            return;
        }
        if (Lists(target, region)) return;
        target.Regions ??= [];
        target.Regions.Add(new FormLink<IRegionGetter>(region));
    }

    /// <param name="partFields">The fields the other parts hold.</param>
    private static RecordPart<ICellGetter, ICell> RestPart(IEnumerable<string> partFields)
    {
        var flagBits = ~(HasWaterBit | ExcludedFlagBits | FlagParts.Aggregate(0, (bits, entry) => bits | (int)entry.Bit));
        var majorFlagBits = ~(ExcludedMajorFlagBits | MajorFlagParts.Aggregate(0, (bits, entry) => bits | (int)entry.Bit));
        var mask = TranslationMasks.AllBut<Cell.TranslationMask>(
            partFields.Concat(ExcludedFields).Concat(RecordHeader.FlagFields).Append(nameof(ICellGetter.Flags)));
        return new(
            [],
            (one, other) => FlagBits.Differ((int)one.Flags, (int)other.Flags, flagBits)
                || FlagBits.Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, majorFlagBits)
                || !one.Equals(other, mask),
            (target, source) =>
            {
                target.Flags = (Cell.Flag)FlagBits.CopyBits((int)target.Flags, (int)source.Flags, flagBits);
                target.MajorRecordFlagsRaw = FlagBits.CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, majorFlagBits);
                ((ICellInternal)target).DeepCopyIn(source, errorMask: null, copyMask: mask.GetCrystal());
            },
            static version => version.OcclusionData is not null || version.LNAM is not null || version.XWCN is not null
                || version.XWCS is not null || version.MaxHeightData is not null);
    }
}

using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The parts of a LAND record, each with its compare and its copy: one point part (height and normal) and one colour part per grid
/// point, one part per quarter (its base and alpha layers), and the rest.
/// </summary>
/// <remarks>
/// A version without a grid has no values at its points, so points are compared and copied only between versions that both
/// have the grid; whether a version has it belongs to the rest.
/// </remarks>
internal sealed class LandRecordParts
{
    private const int PointCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;

    /// <summary>Never compared, never written: the writer derives the flags from what the record holds.</summary>
    public static readonly string[] ExcludedFields = [.. RecordHeader.Fields, nameof(ILandscapeGetter.Flags)];

    /// <summary>Never compared, never written.</summary>
    public const int ExcludedFlagBits = RecordHeader.CompressedBit;

    /// <summary>The grids whose values the point and colour parts hold.</summary>
    private static readonly string[] GridFields =
        [nameof(ILandscapeGetter.VertexHeightMap), nameof(ILandscapeGetter.VertexNormals), nameof(ILandscapeGetter.VertexColors)];

    private readonly Landscape.TranslationMask _restMask = TranslationMasks.AllBut<Landscape.TranslationMask>(
        [.. GridFields, nameof(ILandscapeGetter.Layers), .. RecordHeader.FlagFields, .. ExcludedFields]);

    /// <param name="heightTolerance">The largest height gap at a point that is no change, in game units.</param>
    public LandRecordParts(float heightTolerance)
    {
        Points = [.. Enumerable.Range(0, PointCount).Select(index => PointPart(index, heightTolerance))];
        Colours = [.. Enumerable.Range(0, PointCount).Select(ColourPart)];
        var quarters = new RecordPart<ILandscapeGetter, ILandscape>[LandLayerReader.QuadrantPlaces.Length];
        foreach (var (quadrant, x, y) in LandLayerReader.QuadrantPlaces) quarters[TerrainQuadrants.IndexOf(x, y)] = LayerPart(quadrant);
        Quarters = quarters;
        Rest = new([], RestDiffers, CopyRest, static _ => false);
    }

    /// <summary>Per grid point (row by row from the bottom, x fastest): its height and normal.</summary>
    public IReadOnlyList<RecordPart<LandPoints, LandPoints>> Points { get; }

    /// <summary>Per grid point: its vertex colour.</summary>
    public IReadOnlyList<RecordPart<LandPoints, LandPoints>> Colours { get; }

    /// <summary>Per quarter (by <see cref="TerrainQuadrants.IndexOf"/>): its base and alpha layers.</summary>
    public IReadOnlyList<RecordPart<ILandscapeGetter, ILandscape>> Quarters { get; }

    /// <summary>Every field no other part holds, which grids the record has, and the layers in no quarter.</summary>
    public RecordPart<ILandscapeGetter, ILandscape> Rest { get; }

    private static RecordPart<LandPoints, LandPoints> PointPart(int index, float heightTolerance) => new(
        [],
        (one, other) => (one.Heights, other.Heights) is ({ } a, { } b) && MathF.Abs(a[index] - b[index]) > heightTolerance
            || (one.Normals, other.Normals) is ({ } m, { } n) && m[index] != n[index],
        (target, source) =>
        {
            if ((target.Heights, source.Heights) is ({ } into, { } from)) into[index] = from[index];
            if ((target.Normals, source.Normals) is ({ } normalsInto, { } normalsFrom)) normalsInto[index] = normalsFrom[index];
        },
        static _ => false);

    private static RecordPart<LandPoints, LandPoints> ColourPart(int index) => new(
        [],
        (one, other) => (one.Colours, other.Colours) is ({ } a, { } b) && a[index] != b[index],
        (target, source) =>
        {
            if ((target.Colours, source.Colours) is ({ } into, { } from)) into[index] = from[index];
        },
        static _ => false);

    /// <param name="quadrant">Null for the layers without a header, which lie in no quarter.</param>
    private static RecordPart<ILandscapeGetter, ILandscape> LayerPart(Quadrant? quadrant) => new(
        [nameof(ILandscapeGetter.Layers)],
        (one, other) => LayersDiffer(one, other, quadrant),
        (target, source) => CopyLayers(target, source, quadrant),
        static _ => false);

    private static bool LayersDiffer(ILandscapeGetter one, ILandscapeGetter other, Quadrant? quadrant)
    {
        var mine = LayersIn(one.Layers, quadrant).ToList();
        var theirs = LayersIn(other.Layers, quadrant).ToList();
        return mine.Count != theirs.Count || mine.Zip(theirs).Any(pair => !SameLayer(pair.First, pair.Second));
    }

    /// <summary>Layers are the same when they are of the same kind and hold the same values.</summary>
    private static bool SameLayer(IBaseLayerGetter one, IBaseLayerGetter other) =>
        (one is IAlphaLayerGetter) == (other is IAlphaLayerGetter) && Equals(one, other);

    private static void CopyLayers(ILandscape target, ILandscapeGetter source, Quadrant? quadrant)
    {
        target.Layers.RemoveAll(layer => layer.Header?.Quadrant == quadrant);
        target.Layers.AddRange(LayersIn(source.Layers, quadrant).Select(layer => layer.DeepCopy()));
    }

    private static IEnumerable<IBaseLayerGetter> LayersIn(IReadOnlyList<IBaseLayerGetter> layers, Quadrant? quadrant) =>
        layers.Where(layer => layer.Header?.Quadrant == quadrant);

    private bool RestDiffers(ILandscapeGetter one, ILandscapeGetter other) =>
        FlagBits.Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, ~ExcludedFlagBits)
        || !HasGrids(one).SequenceEqual(HasGrids(other))
        || (one.VertexHeightMap, other.VertexHeightMap) is ({ } a, { } b) && a.Unknown != b.Unknown
        || LayersDiffer(one, other, quadrant: null)
        || !one.Equals(other, _restMask);

    private void CopyRest(ILandscape target, ILandscapeGetter source)
    {
        target.MajorRecordFlagsRaw = FlagBits.CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, ~ExcludedFlagBits);
        var (targetHas, sourceHas) = (HasGrids(target), HasGrids(source));
        var gridsToCopy = GridFields.Where((_, grid) => targetHas[grid] != sourceHas[grid]).ToArray();
        if (gridsToCopy.Length > 0) CopyFields(target, source, TranslationMasks.Only<Landscape.TranslationMask>(gridsToCopy));
        if ((target.VertexHeightMap, source.VertexHeightMap) is ({ } into, { } from)) into.Unknown = from.Unknown;
        CopyLayers(target, source, quadrant: null);
        CopyFields(target, source, _restMask);
    }

    private static void CopyFields(ILandscape target, ILandscapeGetter source, Landscape.TranslationMask mask) =>
        ((ILandscapeInternal)target).DeepCopyIn(source, errorMask: null, copyMask: mask.GetCrystal());

    /// <summary>Per field of <see cref="GridFields"/>, whether the version has that grid.</summary>
    private static bool[] HasGrids(ILandscapeGetter version) =>
        [version.VertexHeightMap is not null, version.VertexNormals is not null, version.VertexColors is not null];
}

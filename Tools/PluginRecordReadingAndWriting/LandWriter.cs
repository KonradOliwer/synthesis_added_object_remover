using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using static AddedObjectRemover.TerrainGrid;

namespace AddedObjectRemover;

/// <summary>
/// Writes parts of LAND records the target plugin changed into the patch: each into a copy of the winning record in the patch's
/// override of its cell, made on first use. Not thread-safe.
/// </summary>
internal sealed class LandWriter(SynthesisWriteCache synthesis, CellOverrides cells, RecordHandles handles)
{
    /// <summary>A normal component of 1 as the vertex normals (VNML) store it: each component is a signed byte.</summary>
    private const float StoredUnitNormal = 127f;

    /// <summary>The record flags that say which grids and layers the record holds.</summary>
    private const Landscape.Flag HeldDataFlags = Landscape.Flag.VertexNormalsHeightMap | Landscape.Flag.VertexColors | Landscape.Flag.Layers;

    private readonly Dictionary<FormKey, ILandscape> _lands = [];

    public void CopyEarlierParts(RecordKey land, PointSet colours, QuarterSet quarters, bool rest)
    {
        var versions = handles.LandOf(land);
        var target = GetOrAddOverride(land, versions.Winner);
        if (rest) handles.LandParts.Rest.CopyInto(target, versions.Earlier);
        foreach (var quarter in quarters.Quarters) handles.LandParts.Quarters[quarter].CopyInto(target, versions.Earlier);
        UndoColours(target, versions.Earlier, colours);
        SetHeldDataFlags(target);
    }

    public void SetHeights(RecordKey land, float[] heights, PointSet restored, CellEdgeHeights beyond)
    {
        var steps = LandHeightEncoder.Encode(heights)
            ?? throw new InvalidOperationException($"The heights ordered for LAND record {land} have a step too steep to store.");
        var versions = handles.LandOf(land);
        var target = GetOrAddOverride(land, versions.Winner);
        WriteHeightMap(target, steps);
        WriteGrid(target.VertexNormals ??= NewGrid(), NormalsFor(heights, restored, beyond, LandPoints.Read(versions.Earlier), LandPoints.Read(versions.Winner)));
        SetHeldDataFlags(target);
    }

    private void UndoColours(ILandscape target, ILandscapeGetter earlier, PointSet colours)
    {
        if (colours.IsEmpty) return;
        var into = LandPoints.Read(target);
        var from = LandPoints.Read(earlier);
        foreach (var point in colours.Points) handles.LandParts.Colours[point].CopyInto(into, from);
        if ((target.VertexColors, into.Colours) is ({ } grid, { } values)) WriteGrid(grid, values);
    }

    private static void WriteHeightMap(ILandscape target, LandHeightSteps steps)
    {
        var map = target.VertexHeightMap ??= new LandscapeVertexHeightMap();
        map.Offset = steps.Offset;
        for (var y = 0; y < VerticesPerSide; y++)
        {
            for (var x = 0; x < VerticesPerSide; x++) map.HeightMap[x, y] = steps.Steps[y * VerticesPerSide + x];
        }
    }

    /// <remarks>A point not restored keeps the winner's normal whatever the heights around it, so a normal a later plugin set stays.</remarks>
    private static P3UInt8[] NormalsFor(float[] heights, PointSet restored, CellEdgeHeights beyond, LandPoints earlier, LandPoints winner)
    {
        var computed = TerrainNormals.Compute(heights, beyond);
        var normals = new P3UInt8[heights.Length];
        for (var point = 0; point < heights.Length; point++)
        {
            normals[point] = (restored.Contains(point), earlier.Normals, winner.Normals) switch
            {
                (false, _, { } winnerNormals) => winnerNormals[point],
                (true, { } earlierNormals, _) when HeightsAroundEqual(heights, earlier.Heights, point) => earlierNormals[point],
                (true, _, { } winnerNormals) when HeightsAroundEqual(heights, winner.Heights, point) => winnerNormals[point],
                _ => Stored(computed[point]),
            };
        }
        return normals;
    }

    /// <summary>Whether the point's height and those of its neighbours along x and y inside the cell equal the version's.</summary>
    private static bool HeightsAroundEqual(float[] heights, float[]? version, int point)
    {
        if (version is null) return false;
        var (x, y) = (point % VerticesPerSide, point / VerticesPerSide);
        return heights[point] == version[point]
            && (x == 0 || heights[point - 1] == version[point - 1])
            && (x == VerticesPerSide - 1 || heights[point + 1] == version[point + 1])
            && (y == 0 || heights[point - VerticesPerSide] == version[point - VerticesPerSide])
            && (y == VerticesPerSide - 1 || heights[point + VerticesPerSide] == version[point + VerticesPerSide]);
    }

    private static P3UInt8 Stored(System.Numerics.Vector3 normal) => new(StoredComponent(normal.X), StoredComponent(normal.Y), StoredComponent(normal.Z));

    private static byte StoredComponent(float component) => unchecked((byte)Convert.ToSByte(component * StoredUnitNormal));

    private static Array2d<P3UInt8> NewGrid() => new(new P2Int(VerticesPerSide, VerticesPerSide), default);

    private static void WriteGrid(IArray2d<P3UInt8> grid, P3UInt8[] values)
    {
        for (var y = 0; y < VerticesPerSide; y++)
        {
            for (var x = 0; x < VerticesPerSide; x++) grid[x, y] = values[y * VerticesPerSide + x];
        }
    }

    private static void SetHeldDataFlags(ILandscape land)
    {
        var held = (land.VertexHeightMap is not null && land.VertexNormals is not null ? Landscape.Flag.VertexNormalsHeightMap : 0)
            | (land.VertexColors is not null ? Landscape.Flag.VertexColors : 0)
            | (land.Layers.Count > 0 ? Landscape.Flag.Layers : 0);
        land.Flags = ((land.Flags ?? 0) & ~HeldDataFlags) | held;
    }

    /// <remarks>The cell comes from the LAND record's context in the Synthesis cache, so the patch's cell override is shared with the other writers.</remarks>
    private ILandscape GetOrAddOverride(RecordKey land, ILandscapeGetter winner)
    {
        var key = land.ToFormKey();
        if (_lands.TryGetValue(key, out var held)) return held;
        var cellKey = synthesis.Cache.ResolveContext<ILandscape, ILandscapeGetter>(key).Parent?.Record is ICellGetter cell
            ? cell.FormKey
            : throw new InvalidOperationException($"LAND record {land} lies in no cell.");
        var landscape = cells.GetOrAdd(cellKey).Landscape ??= (Landscape)winner.DeepCopy();
        _lands.Add(key, landscape);
        return landscape;
    }
}

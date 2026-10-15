using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Decodes the texture layers (BTXT, ATXT and VTXT) of a LAND record into <see cref="TerrainLayers"/>.</summary>
internal static class LandLayerReader
{
    /// <summary>Where each of Mutagen's quadrants lies in the cell: its column (x, to the right) and row (y, up).</summary>
    private static readonly (Quadrant Quadrant, int X, int Y)[] QuadrantPlaces =
    [
        (Quadrant.BottomLeft, 0, 0),
        (Quadrant.BottomRight, 1, 0),
        (Quadrant.TopLeft, 0, 1),
        (Quadrant.TopRight, 1, 1),
    ];

    /// <returns>Null for a deleted record; a record without layers gives quadrants without any.</returns>
    public static TerrainLayers? Read(ILandscapeGetter landscape, PluginNames names)
    {
        if (landscape.IsDeleted) return null;
        var quadrants = new QuadrantLayers[QuadrantPlaces.Length];
        foreach (var (quadrant, x, y) in QuadrantPlaces) quadrants[TerrainQuadrants.IndexOf(x, y)] = ReadQuadrant(landscape.Layers, quadrant, names);
        return new TerrainLayers(quadrants);
    }

    /// <remarks>If a quadrant stores several base layers, the last one stored shows.</remarks>
    private static QuadrantLayers ReadQuadrant(IReadOnlyList<IBaseLayerGetter> layers, Quadrant quadrant, PluginNames names)
    {
        var inQuadrant = LayersIn(layers, quadrant);
        return new QuadrantLayers(
            inQuadrant.Where(entry => entry.Layer is not IAlphaLayerGetter).Select(entry => (RecordKey?)names.KeyOf(entry.Header.Texture.FormKey)).LastOrDefault(),
            [.. inQuadrant.Select(entry => entry.Layer is IAlphaLayerGetter alpha ? ReadAlpha(alpha, entry.Header, names) : null).OfType<TerrainAlphaLayer>()]);
    }

    /// <summary>The quadrant's layers in the order stored, each with its header; a layer without a header is in no quadrant.</summary>
    private static List<(IBaseLayerGetter Layer, ILayerHeaderGetter Header)> LayersIn(IReadOnlyList<IBaseLayerGetter> layers, Quadrant quadrant)
    {
        var inQuadrant = new List<(IBaseLayerGetter Layer, ILayerHeaderGetter Header)>();
        foreach (var layer in layers)
        {
            if (layer.Header is { } header && header.Quadrant == quadrant) inQuadrant.Add((layer, header));
        }
        return inQuadrant;
    }

    /// <remarks>A position outside the quadrant's vertices (a malformed record) paints nothing.</remarks>
    private static TerrainAlphaLayer ReadAlpha(IAlphaLayerGetter layer, ILayerHeaderGetter header, PluginNames names)
    {
        var opacities = new float[TerrainQuadrants.VertexCount];
        foreach (var data in layer.AlphaLayerData ?? [])
        {
            if (data.Position < TerrainQuadrants.VertexCount) opacities[OpacityIndexOf(data.Position)] = data.Opacity;
        }
        return new TerrainAlphaLayer(names.KeyOf(header.Texture.FormKey), header.LayerNumber, opacities);
    }

    /// <summary>
    /// ASSUMPTION, not yet confirmed on real data, and the first thing to check if terrain type changes look wrong: an alpha
    /// layer's position numbers its quadrant's vertices row by row from the bottom, x fastest (position = y * 17 + x). That is
    /// the layout of <see cref="TerrainAlphaLayer.Opacities"/>, so the position is the opacity index unchanged.
    /// </summary>
    private static int OpacityIndexOf(int alphaPosition) => alphaPosition;
}

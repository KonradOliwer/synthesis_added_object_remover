using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Decodes the texture layers (BTXT, ATXT and VTXT) of a LAND record into <see cref="TerrainLayers"/>.</summary>
internal static class LandLayerReader
{
    /// <summary>Where each of Mutagen's quadrants lies in the cell: its column (x, to the right) and row (y, up).</summary>
    public static readonly ImmutableArray<(Quadrant Quadrant, int X, int Y)> QuadrantPlaces =
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
        var layers = ReadHeadedLayers(landscape);
        var quadrants = new QuadrantLayers[QuadrantPlaces.Length];
        foreach (var (quadrant, x, y) in QuadrantPlaces) quadrants[TerrainQuadrants.IndexOf(x, y)] = ReadQuadrant(layers, quadrant, names);
        return new TerrainLayers(quadrants);
    }

    /// <summary>The layers in the order stored that have a header; a layer without a header is in no quadrant.</summary>
    private static StoredLayer[] ReadHeadedLayers(ILandscapeGetter landscape)
    {
        var stored = MutagenCalls.Read(() => landscape.Layers
            .Select(layer => layer.Header is { } header
                ? (Headed: true, header.Quadrant, Texture: header.Texture.FormKey, header.LayerNumber,
                    Alphas: layer is IAlphaLayerGetter alpha ? (alpha.AlphaLayerData ?? []).Select(data => (data.Position, data.Opacity)).ToArray() : null)
                : default)
            .ToArray());
        return [.. stored.Where(layer => layer.Headed).Select(layer => new StoredLayer(layer.Quadrant, layer.Texture, layer.LayerNumber, layer.Alphas))];
    }

    /// <remarks>If a quadrant stores several base layers, the last one stored shows.</remarks>
    private static QuadrantLayers ReadQuadrant(StoredLayer[] layers, Quadrant quadrant, PluginNames names)
    {
        var inQuadrant = layers.Where(layer => layer.Quadrant == quadrant).ToList();
        return new QuadrantLayers(
            inQuadrant.Where(layer => layer.Alphas is null).Select(layer => (RecordKey?)names.KeyOf(layer.Texture)).LastOrDefault(),
            [.. inQuadrant.Select(layer => layer.Alphas is { } alphas ? ReadAlpha(layer, alphas, names) : null).OfType<TerrainAlphaLayer>()]);
    }

    /// <remarks>A position outside the quadrant's vertices (a malformed record) paints nothing.</remarks>
    private static TerrainAlphaLayer ReadAlpha(StoredLayer layer, (ushort Position, float Opacity)[] alphas, PluginNames names)
    {
        var opacities = new float[TerrainQuadrants.VertexCount];
        foreach (var (position, opacity) in alphas)
        {
            if (position < TerrainQuadrants.VertexCount) opacities[OpacityIndexOf(position)] = opacity;
        }
        return new TerrainAlphaLayer(names.KeyOf(layer.Texture), layer.LayerNumber, opacities);
    }

    /// <summary>
    /// ASSUMPTION, not yet confirmed on real data, and the first thing to check if terrain type changes look wrong: an alpha
    /// layer's position numbers its quadrant's vertices row by row from the bottom, x fastest (position = y * 17 + x). That is
    /// the layout of <see cref="TerrainAlphaLayer.Opacities"/>, so the position is the opacity index unchanged.
    /// </summary>
    private static int OpacityIndexOf(int alphaPosition) => alphaPosition;

    /// <param name="Alphas">The opacities by position of an alpha layer; null for a base layer.</param>
    private readonly record struct StoredLayer(Quadrant Quadrant, FormKey Texture, ushort LayerNumber, (ushort Position, float Opacity)[]? Alphas);
}

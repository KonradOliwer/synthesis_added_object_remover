namespace AddedObjectRemover;

/// <summary>Works out which landscape textures a terrain vertex shows.</summary>
public static class TerrainLayerShares
{
    /// <summary>
    /// Each quadrant's alpha layers are blended over its base layer from the lowest layer number up, each by its opacity at the
    /// vertex. A vertex on a quadrant edge belongs to every quadrant that has it, and their shares are averaged.
    /// </summary>
    /// <param name="vertexX">0 to 32 across the cell.</param>
    /// <param name="vertexY">0 to 32 up the cell.</param>
    /// <returns>Each texture once with its summed share, the default texture first and then in <see cref="RecordKeyOrder"/>; the shares add up to 1.</returns>
    public static TextureShare[] At(TerrainLayers layers, int vertexX, int vertexY)
    {
        var blended = new List<TextureShare>();
        var quadrants = 0;
        var lastLocal = TerrainQuadrants.VerticesPerSide - 1;
        for (var y = 0; y < TerrainQuadrants.PerSide; y++)
        {
            for (var x = 0; x < TerrainQuadrants.PerSide; x++)
            {
                var (localX, localY) = (vertexX - x * lastLocal, vertexY - y * lastLocal);
                if (localX < 0 || localX > lastLocal || localY < 0 || localY > lastLocal) continue;
                quadrants++;
                BlendQuadrant(layers.Quadrants[TerrainQuadrants.IndexOf(x, y)], localY * TerrainQuadrants.VerticesPerSide + localX, blended);
            }
        }
        if (quadrants == 0) throw new ArgumentOutOfRangeException(nameof(vertexX), $"The vertex ({vertexX}, {vertexY}) is outside the cell's terrain.");
        return SumByTexture(blended, quadrants);
    }

    /// <summary>Adds the quadrant's shares at the vertex to <paramref name="shares"/>: the base layer first, then each alpha layer as it is laid over.</summary>
    private static void BlendQuadrant(QuadrantLayers quadrant, int localIndex, List<TextureShare> shares)
    {
        var first = shares.Count;
        shares.Add(new TextureShare(quadrant.Base, 1f));
        Span<int> layerOrder = stackalloc int[quadrant.Alphas.Length];
        SortByLayerNumber(quadrant.Alphas, layerOrder);
        foreach (var layer in layerOrder)
        {
            var alpha = quadrant.Alphas[layer];
            var opacity = alpha.Opacities[localIndex];
            for (var index = first; index < shares.Count; index++) shares[index] = shares[index] with { Share = shares[index].Share * (1f - opacity) };
            shares.Add(new TextureShare(alpha.Texture, opacity));
        }
    }

    /// <summary>Fills <paramref name="order"/> with the layers' indexes by layer number, keeping the stored order of equal numbers.</summary>
    private static void SortByLayerNumber(TerrainAlphaLayer[] alphas, Span<int> order)
    {
        for (var next = 0; next < alphas.Length; next++)
        {
            var place = next;
            for (; place > 0 && alphas[order[place - 1]].LayerNumber > alphas[next].LayerNumber; place--) order[place] = order[place - 1];
            order[place] = next;
        }
    }

    /// <param name="blended">Every quadrant's shares, in the order blended.</param>
    /// <param name="quadrants">The number of quadrants the shares come from.</param>
    private static TextureShare[] SumByTexture(List<TextureShare> blended, int quadrants)
    {
        var sums = new List<(RecordKey? Texture, double Share)>();
        foreach (var share in blended)
        {
            if (IndexOf(sums, share.Texture) is { } index) sums[index] = (share.Texture, sums[index].Share + share.Share);
            else sums.Add((share.Texture, share.Share));
        }

        var shares = new List<TextureShare>(sums.Count);
        foreach (var (texture, sum) in sums)
        {
            var share = (float)sum / quadrants;
            if (share > 0) shares.Add(new TextureShare(texture, share));
        }
        shares.Sort(static (first, second) => CompareTextures(first.Texture, second.Texture));
        return [.. shares];
    }

    private static int? IndexOf(List<(RecordKey? Texture, double Share)> sums, RecordKey? texture)
    {
        for (var index = 0; index < sums.Count; index++)
        {
            if (Nullable.Equals(sums[index].Texture, texture)) return index;
        }
        return null;
    }

    private static int CompareTextures(RecordKey? first, RecordKey? second) =>
        (first, second) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => RecordKeyOrder.Comparer.Compare(first.Value, second.Value),
        };
}

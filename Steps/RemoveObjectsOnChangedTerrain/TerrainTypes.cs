namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>A kind of ground: the root material of a landscape texture, the game's default terrain, or unknown.</summary>
/// <param name="Material">The root material; null for the default terrain and for unknown.</param>
/// <remarks>Two unknown types are equal, so missing data never counts as a change.</remarks>
internal readonly record struct TerrainType(RecordKey? Material, bool IsUnknown) : IComparable<TerrainType>
{
    public static TerrainType DefaultTerrain => default;

    public static TerrainType Unknown => new(Material: null, IsUnknown: true);

    private int Rank => Material != null ? 2 : IsUnknown ? 1 : 0;

    /// <summary>The default terrain first, then unknown, then the materials in <see cref="RecordKeyOrder"/>.</summary>
    public int CompareTo(TerrainType other)
    {
        var byRank = Rank.CompareTo(other.Rank);
        return byRank != 0 || Material is not { } material || other.Material is not { } otherMaterial
            ? byRank
            : RecordKeyOrder.Comparer.Compare(material, otherMaterial);
    }
}

/// <summary>The terrain type at each vertex of a terrain version. Each texture's type is read once per step call. Thread-safe.</summary>
internal sealed class TerrainTypes(IPluginRecords records)
{
    private readonly ComputedOncePerKey<RecordKey, TerrainType> _textures = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    /// <summary>
    /// At each vertex (row by row, as the heights), the type with the largest share: the shares of textures of one type add up,
    /// and equal shares go to the first type in <see cref="TerrainType.CompareTo"/> order. A version without texture layers (a
    /// deleted record) shows the default terrain everywhere.
    /// </summary>
    public TerrainType[] AtEveryVertex(TerrainVersion version)
    {
        var types = new TerrainType[CellTerrain.VertexCount];
        if (version.Layers is not { } layers)
        {
            Array.Fill(types, TerrainType.DefaultTerrain);
            return types;
        }

        var textureTypes = new Dictionary<RecordKey, TerrainType>();
        var sums = new List<(TerrainType Type, double Share)>();
        for (var vertex = 0; vertex < types.Length; vertex++)
        {
            var shares = TerrainLayerShares.At(layers, vertex % TerrainHeights.VerticesPerSide, vertex / TerrainHeights.VerticesPerSide);
            types[vertex] = LargestType(SumByType(shares, textureTypes, sums));
        }
        return types;
    }

    /// <param name="textureTypes">The types of the version's textures met so far.</param>
    /// <param name="sums">Reused for every vertex; holds the types in the order of their first share.</param>
    private List<(TerrainType Type, double Share)> SumByType(
        TextureShare[] shares, Dictionary<RecordKey, TerrainType> textureTypes, List<(TerrainType Type, double Share)> sums)
    {
        sums.Clear();
        foreach (var share in shares)
        {
            var type = TypeOf(share.Texture, textureTypes);
            if (IndexOf(sums, type) is { } index) sums[index] = (type, sums[index].Share + share.Share);
            else sums.Add((type, share.Share));
        }
        return sums;
    }

    private int? IndexOf(List<(TerrainType Type, double Share)> sums, TerrainType type)
    {
        for (var index = 0; index < sums.Count; index++)
        {
            if (sums[index].Type == type) return index;
        }
        return null;
    }

    private TerrainType LargestType(List<(TerrainType Type, double Share)> sums)
    {
        var (largest, largestShare) = (sums[0].Type, (float)sums[0].Share);
        for (var index = 1; index < sums.Count; index++)
        {
            var (type, share) = (sums[index].Type, (float)sums[index].Share);
            if (share > largestShare || (share == largestShare && type.CompareTo(largest) < 0)) (largest, largestShare) = (type, share);
        }
        return largest;
    }

    private TerrainType TypeOf(RecordKey? texture, Dictionary<RecordKey, TerrainType> textureTypes)
    {
        if (texture is not { } key) return TerrainType.DefaultTerrain;
        if (!textureTypes.TryGetValue(key, out var type)) textureTypes[key] = type = _textures.Get(key, () => RootMaterialOf(key));
        return type;
    }

    /// <remarks>A material and its children are one type, so a mod's own material made a child of grass is still grass.</remarks>
    private TerrainType RootMaterialOf(RecordKey texture) =>
        records.ReadTextureMaterialChain(texture) is [.. , var root] ? new TerrainType(root, IsUnknown: false) : TerrainType.Unknown;
}

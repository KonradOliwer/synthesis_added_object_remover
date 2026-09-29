using System.Collections.Immutable;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover;

internal static partial class Tables
{
    private const string BaseSeparator = "; ";

    private static readonly ImmutableArray<string> MeshOriginsHeader =
    [
        "modelPath", "baseEditorIds", "referenceCount",
        "localMinX", "localMinY", "localMinZ", "localMaxX", "localMaxY", "localMaxZ",
        "originFractionX", "originFractionY", "originFractionZ", "classification",
    ];

    public static CsvTable MeshOrigins(IEnumerable<MeshOrigin> origins) =>
        new(MeshOriginsFileName, MeshOriginsHeader, [.. origins.Select(MeshOriginRow)]);

    private static ImmutableArray<string> MeshOriginRow(MeshOrigin origin) => Row(
    [
        Text(origin.Mesh), Text(string.Join(BaseSeparator, origin.BaseEditorIds)), Num(origin.References),
        Num(origin.TriangleBounds.Min.X), Num(origin.TriangleBounds.Min.Y), Num(origin.TriangleBounds.Min.Z),
        Num(origin.TriangleBounds.Max.X), Num(origin.TriangleBounds.Max.Y), Num(origin.TriangleBounds.Max.Z),
        Num(origin.OriginFractions.X), Num(origin.OriginFractions.Y), Num(origin.OriginFractions.Z),
        Text(origin.Class),
    ]);
}

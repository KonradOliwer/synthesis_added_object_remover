using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <summary>Separates the entries of a list inside one report cell.</summary>
    private const string CellSeparator = "; ";

    private static readonly ImmutableArray<string> MeshOriginsHeader =
    [
        "modelPath", "baseEditorIds", "referenceCount",
        "localMinX", "localMinY", "localMinZ", "localMaxX", "localMaxY", "localMaxZ",
        "originFractionX", "originFractionY", "originFractionZ", "classification",
    ];

    public static CsvTable MeshOrigins(IEnumerable<MeshOrigin> origins) =>
        new(ReportFileNames.MeshOriginsFileName, MeshOriginsHeader, [.. origins.Select(MeshOriginRow)]);

    private static ImmutableArray<string> MeshOriginRow(MeshOrigin origin) => CsvRow.Of(
    [
        Quote(origin.Mesh), Quote(string.Join(CellSeparator, origin.BaseEditorIds)), Int(origin.References),
        .. Numbers(origin.TriangleBounds.Min), .. Numbers(origin.TriangleBounds.Max), .. Numbers(origin.OriginFractions),
        Quote(origin.Class),
    ]);
}

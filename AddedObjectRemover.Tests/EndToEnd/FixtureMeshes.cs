using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The fixture's closed box meshes, written as loose NIFs under the Data folder. Every mesh stands
/// on its origin, centred across.
/// </summary>
internal static class FixtureMeshes
{
    public const float CrateSize = 100f;
    public const string CrateModel = "aor\\crate.nif";
    public const string PadModel = "aor\\pad.nif";
    public const string BuildingModel = "aor\\building.nif";

    /// <summary>Two pillars with a gap between them, so the mesh does not fill its own bounding box.</summary>
    public const string TwinPillarsModel = "aor\\twinpillars.nif";

    private const float PadWidth = 40f;
    private const float PadHeight = 20f;
    private const float BuildingWidth = 400f;
    private const float BuildingHeight = 300f;
    private const float PillarWidth = 40f;
    private const float PillarHeight = 200f;
    private const float PillarCentreX = 130f;
    private const string MeshesFolder = "meshes";

    public static readonly Box CrateBox = StandingBox(CrateSize, CrateSize, Vector3.Zero);

    private static readonly Box PadBox = StandingBox(PadWidth, PadHeight, Vector3.Zero);
    private static readonly Box BuildingBox = StandingBox(BuildingWidth, BuildingHeight, Vector3.Zero);
    private static readonly Box EastPillar = StandingBox(PillarWidth, PillarHeight, new Vector3(PillarCentreX, 0f, 0f));
    private static readonly Box WestPillar = StandingBox(PillarWidth, PillarHeight, new Vector3(-PillarCentreX, 0f, 0f));

    public static void WriteAll(string dataFolder)
    {
        Write(dataFolder, CrateModel, CrateBox);
        Write(dataFolder, PadModel, PadBox);
        Write(dataFolder, BuildingModel, BuildingBox);
        Write(dataFolder, TwinPillarsModel, EastPillar, WestPillar);
    }

    private static Box StandingBox(float width, float height, Vector3 footCentre) =>
        new(footCentre + new Vector3(-width / 2, -width / 2, 0f), footCentre + new Vector3(width / 2, width / 2, height));

    /// <param name="parts">One NIF shape per box.</param>
    private static void Write(string dataFolder, string model, params Box[] parts)
    {
        var nif = TestNifs.CreateWithRoot();
        foreach (var part in parts) TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(part));
        var path = Path.Combine(dataFolder, MeshesFolder, model);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TestNifs.Save(nif));
    }
}

namespace AddedObjectRemover;

/// <summary>The combined render-geometry bounds of the meshes of one body.</summary>
/// <param name="MeshPaths">The measured meshes, in path order.</param>
public sealed record BodyMeshSetBounds(IReadOnlyList<string> MeshPaths, Box Bounds);

/// <summary>The combined bounds of sets of body meshes (the addons one body armour gives one race and sex), each set measured once. Thread-safe.</summary>
public interface IBodyMeshBounds
{
    /// <param name="meshPaths">Normalized mesh paths; their order and case do not matter.</param>
    /// <returns>Null when none of the meshes can be read or their bounds have no height.</returns>
    Box? Of(IReadOnlyList<string> meshPaths);

    /// <summary>Every successful measurement so far, ordered by mesh set.</summary>
    IReadOnlyList<BodyMeshSetBounds> Computed();
}

/// <summary>
/// Meshes without triangles (only bounding-sphere shapes) do not count towards a body, although
/// their file bounds exist.
/// </summary>
public sealed class BodyMeshBounds(IMeshFiles meshFiles) : IBodyMeshBounds
{
    private const string KeySeparator = " + ";

    private readonly ComputedOncePerKey<string, BodyMeshSetBounds?> _bySet = new(Publication.BuiltOnce, StringComparer.OrdinalIgnoreCase);

    public Box? Of(IReadOnlyList<string> meshPaths)
    {
        var sorted = meshPaths.Order(StringComparer.OrdinalIgnoreCase).ToList();
        return _bySet.Get(SetKey(sorted), () => MeasureSet(sorted))?.Bounds;
    }

    public IReadOnlyList<BodyMeshSetBounds> Computed() =>
        _bySet.Contents()
            .OfType<BodyMeshSetBounds>()
            .OrderBy(measurement => SetKey(measurement.MeshPaths), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string SetKey(IReadOnlyList<string> sortedMeshPaths) => string.Join(KeySeparator, sortedMeshPaths);

    private BodyMeshSetBounds? MeasureSet(IReadOnlyList<string> meshPaths)
    {
        Box? bounds = null;
        foreach (var meshBox in meshPaths.Where(HasTriangles).Select(meshPath => meshFiles.Bounds.Of(meshPath).Box).OfType<Box>())
        {
            bounds = bounds is { } found ? found.Union(meshBox) : meshBox;
        }
        return bounds is { Size.Z: > 0 } measured ? new BodyMeshSetBounds(meshPaths, measured) : null;
    }

    private bool HasTriangles(string meshPath) => meshFiles.ReadTriangles(meshPath) != null;
}
